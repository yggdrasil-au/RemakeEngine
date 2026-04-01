
namespace EngineNet.Core.FileHandlers.Formats.txd;

using System;

internal static partial class TxdExtractor {

    private static class TextureFormatConverter {
        internal static ConversionResult Convert(
            int fmtCode,
            int width,
            int height,
            int mipMapCountFromFile,
            System.ReadOnlySpan<byte> swizzledBaseMipData,
            int actualMipDataSize,
            int segmentOriginalStartOffset,
            NameInfo nameInfo
        ) {

            bool needsUnswizzle = false;
            int bytesPerPixelForUns = 0;
            byte[]? ddsHeader = null;
            byte[]? outputPixels = null;
            string? exportFormat = fmtCode switch {
                0x52 => "DXT1",
                0x53 => "DXT3",
                0x54 => "DXT5",
                0x86 or 0x02 => "RGBA8888",
                _ => null
            };

            if (exportFormat == null) {
                throw new TxdExportException($"          FATAL ERROR: Unknown or unsupported format code 0x{fmtCode:X2} for texture '{nameInfo.Name}' (File 0x{nameInfo.OriginalFileOffset:X}).");
            }

            int expectedBaseSize = GetExpectedBaseMipSize(width, height, exportFormat);
            int safeMipMapCount = mipMapCountFromFile;

            if (actualMipDataSize <= expectedBaseSize && mipMapCountFromFile > 1) {
                DebugLog($"        WARNING: TXD specifies {mipMapCountFromFile} mipmaps, but payload size ({actualMipDataSize} bytes) only covers the base texture. Forcing mipmaps to 1.");
                safeMipMapCount = 1;
            }

            if (fmtCode == 0x52) {
                ddsHeader = CreateDdsHeaderDxt(width, height, safeMipMapCount, "DXT1");
                outputPixels = swizzledBaseMipData.ToArray();
                DebugLog($"        DXT1 format detected. Size: {actualMipDataSize} bytes.");
            } else if (fmtCode == 0x53) {
                ddsHeader = CreateDdsHeaderDxt(width, height, safeMipMapCount, "DXT3");
                outputPixels = swizzledBaseMipData.ToArray();
                DebugLog($"        DXT3 format detected. Size: {actualMipDataSize} bytes.");
            } else if (fmtCode == 0x54) {
                ddsHeader = CreateDdsHeaderDxt(width, height, safeMipMapCount, "DXT5");
                outputPixels = swizzledBaseMipData.ToArray();
                DebugLog($"        DXT5 format detected. Size: {actualMipDataSize} bytes.");
            } else if (fmtCode == 0x86) {
                exportFormat = "RGBA8888 (from Swizzled BGRA)";
                int expectedSize = width * height * 4;
                bytesPerPixelForUns = 4;
                needsUnswizzle = true;
                DebugLog($"        Swizzled BGRA format detected. Size: {actualMipDataSize} bytes.");
                if (actualMipDataSize < expectedSize) {
                    throw new TxdExportException($"          FATAL ERROR: Data size mismatch for BGRA '{nameInfo.Name}' (File 0x{nameInfo.OriginalFileOffset:X}): expected {expectedSize}, got {actualMipDataSize}.");
                }

                byte[]? linear = UnswizzleData(swizzledBaseMipData, width, height, bytesPerPixelForUns);
                if (linear != null) {
                    outputPixels = new byte[linear.Length];
                    for (int pix = 0; pix < linear.Length; pix += 4) {
                        outputPixels[pix + 0] = linear[pix + 2];
                        outputPixels[pix + 1] = linear[pix + 1];
                        outputPixels[pix + 2] = linear[pix + 0];
                        outputPixels[pix + 3] = linear[pix + 3];
                    }
                    ddsHeader = CreateDdsHeaderRgba(width, height, safeMipMapCount);
                }
            } else if (fmtCode == 0x02) {
                exportFormat = "RGBA8888 (from Swizzled A8 or P8A8)";
                needsUnswizzle = true;
                DebugLog($"        Swizzled A8 or P8A8 format detected. Size: {actualMipDataSize} bytes.");
                if (actualMipDataSize >= width * height && actualMipDataSize < width * height * 2) {
                    DebugLog($"        A8 format detected. Size: {actualMipDataSize} bytes.");
                    bytesPerPixelForUns = 1;
                    byte[]? linear = UnswizzleData(swizzledBaseMipData, width, height, bytesPerPixelForUns);
                    if (linear != null) {
                        outputPixels = new byte[width * height * 4];
                        for (int pix = 0; pix < width * height; pix++) {
                            byte alpha = linear[pix];
                            int idx = pix * 4;
                            outputPixels[idx + 0] = 0;
                            outputPixels[idx + 1] = 0;
                            outputPixels[idx + 2] = 0;
                            outputPixels[idx + 3] = alpha;
                        }
                        ddsHeader = CreateDdsHeaderRgba(width, height, safeMipMapCount);
                    }
                } else if (actualMipDataSize >= width * height * 2) {
                    bytesPerPixelForUns = 2;
                    byte[]? linear = UnswizzleData(swizzledBaseMipData, width, height, bytesPerPixelForUns);
                    if (linear != null) {
                        outputPixels = new byte[width * height * 4];
                        DebugLog($"        P8A8/L8A8 format detected. Size: {actualMipDataSize} bytes.");
                        for (int pix = 0; pix < width * height; pix++) {
                            int idx = pix * 2;
                            byte p8 = linear[idx + 0];
                            byte a8 = linear[idx + 1];
                            int outIdx = pix * 4;
                            outputPixels[outIdx + 0] = p8;
                            outputPixels[outIdx + 1] = p8;
                            outputPixels[outIdx + 2] = p8;
                            outputPixels[outIdx + 3] = a8;
                        }
                        ddsHeader = CreateDdsHeaderRgba(width, height, safeMipMapCount);
                    }
                } else {
                    throw new TxdExportException($"          FATAL ERROR: Data size mismatch for Format 0x02 '{nameInfo.Name}' (File 0x{nameInfo.OriginalFileOffset:X}): expected at least {width * height}, got {actualMipDataSize}.");
                }
            } else {
                throw new TxdExportException($"          FATAL ERROR: Unknown or unsupported format code 0x{fmtCode:X2} for texture '{nameInfo.Name}' (File 0x{nameInfo.OriginalFileOffset:X}).");
            }

            return new ConversionResult(ddsHeader, outputPixels, exportFormat, needsUnswizzle, bytesPerPixelForUns);
        }

        private static int GetExpectedBaseMipSize(int width, int height, string format) {
            if (format.StartsWith("DXT")) {
                int blockSize = (format == "DXT1") ? 8 : 16;
                int blocksX = Math.Max(1, (width + 3) / 4);
                int blocksY = Math.Max(1, (height + 3) / 4);
                return blocksX * blocksY * blockSize;
            }
            // Default for RGBA8888 (fmtCode 0x86, 0x02)
            return width * height * 4;
        }

    }

}
