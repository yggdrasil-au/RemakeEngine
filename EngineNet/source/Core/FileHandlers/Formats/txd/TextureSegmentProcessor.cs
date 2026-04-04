using SixLabors.ImageSharp;
using BCnEncoder.Decoder;
using BCnEncoder.ImageSharp;

namespace EngineNet.Core.FileHandlers.Formats.txd;

using System;

internal static partial class TxdExtractor {

    private sealed class TextureSegmentProcessor {
        private static readonly byte[] NameSignature = { 0x2D, 0x00, 0x02, 0x1C, 0x00, 0x00, 0x00, 0x0A };
        private static readonly HashSet<byte> KnownFormatCodes = [0x52, 0x53, 0x54, 0x86, 0x02];
        private static readonly int NameSignatureLength = NameSignature.Length;

        internal int ProcessSegment(Segment segment, string outputDir, string outputExtension = "dds") {
            byte[] segmentData = segment.Data;
            int segmentOriginalStartOffset = segment.StartOffset;
            int texturesFound = 0;
            int i = 0;
            NameInfo? currentName = null;

            Log.Cyan($"  Scanning data segment (len {segmentData.Length}) for textures using signature {System.BitConverter.ToString(NameSignature).Replace("-", string.Empty).ToLowerInvariant()}...");

            while (i < segmentData.Length) {
                if (currentName?.ProcessedMeta == true) {
                    currentName = null;
                }

                if (i + NameSignatureLength <= segmentData.Length && StartsWith(segmentData, i, NameSignature)) {
                    int nameSigOffset = i;
                    int nameStringStart = nameSigOffset + 12;
                    int nameEndScan = nameStringStart;
                    Log.Green($"    name_sig_offset_in_segment = 0x{nameSigOffset:X} (file offset 0x{segmentOriginalStartOffset + nameSigOffset:X})");
                    Log.Green($"    name_string_start_offset_in_segment = 0x{nameStringStart:X} (file offset 0x{segmentOriginalStartOffset + nameStringStart:X})");
                    Log.Green($"    name_end_scan_in_segment = 0x{nameEndScan:X} (file offset 0x{segmentOriginalStartOffset + nameEndScan:X})");
                    Log.Green($"    Found name signature {System.BitConverter.ToString(NameSignature).Replace("-", string.Empty).ToLowerInvariant()} at seg_offset 0x{nameSigOffset:X} (file offset 0x{segmentOriginalStartOffset + nameSigOffset:X})");

                    if (nameStringStart + 2 > segmentData.Length) {
                        Log.Yellow($"    WARNING: Found name signature {System.BitConverter.ToString(NameSignature).Replace("-", string.Empty).ToLowerInvariant()} at seg_offset 0x{nameSigOffset:X}, but not enough data for name string (expected at 0x{nameStringStart:X}).");
                        i = nameSigOffset + 1;
                        continue;
                    }

                    while (nameEndScan < segmentData.Length - 1 && !(segmentData[nameEndScan] == 0x00 && segmentData[nameEndScan + 1] == 0x00)) {
                        nameEndScan += 1;
                    }

                    if (nameEndScan < segmentData.Length - 1 && segmentData[nameEndScan] == 0x00 && segmentData[nameEndScan + 1] == 0x00) {
                        System.Span<byte> nameBytes = segmentData.AsSpan(nameStringStart, nameEndScan - nameStringStart);
                        string? nameValue;
                        try {
                            nameValue = Utf8NoBom.GetString(nameBytes).Trim();
                        } catch (System.Exception ex) {
                            Shared.Diagnostics.Bug($"[TextureSegmentProcessor::ProcessSegment()] Failed to decode name bytes at offset 0x{segmentOriginalStartOffset + nameSigOffset:X}.", ex);
                            nameValue = System.BitConverter.ToString(nameBytes.ToArray()).Replace("-", string.Empty);
                        }

                        if (string.IsNullOrWhiteSpace(nameValue)) {
                            nameValue = $"unnamed_texture_at_0x{segmentOriginalStartOffset + nameSigOffset:08X}";
                            Log.Red($"    WARNING: Name string parsing failed for signature {System.BitConverter.ToString(NameSignature).Replace("-", string.Empty).ToLowerInvariant()} at seg_offset 0x{nameSigOffset:X}. Using fallback name '{nameValue}' (sig at file 0x{segmentOriginalStartOffset + nameSigOffset:X}).");
                        }

                        if (currentName is not null && !currentName.ProcessedMeta) {
                            Log.Yellow($"    WARNING: Previous name '{currentName.Name}' (sig at file 0x{currentName.OriginalFileOffset:X}) was pending metadata but new name '{nameValue}' was found.");
                        }

                        currentName = new NameInfo(nameValue, nameSigOffset, segmentOriginalStartOffset + nameSigOffset);
                        Log.Cyan($"    Parsed name: '{currentName.Name}' (signature {System.BitConverter.ToString(NameSignature).Replace("-", string.Empty).ToLowerInvariant()} at seg_offset 0x{nameSigOffset:X}, file 0x{currentName.OriginalFileOffset:X})");
                        i = nameEndScan + 2;

                        int firstNonZeroAfterName = -1;
                        int scanPtrForNonZero = i;
                        while (scanPtrForNonZero < segmentData.Length) {
                            if (segmentData[scanPtrForNonZero] != 0x00) {
                                firstNonZeroAfterName = scanPtrForNonZero;
                                break;
                            }
                            scanPtrForNonZero += 1;
                        }

                        if (firstNonZeroAfterName == -1) {
                            throw new TxdExportException($"      FATAL ERROR: No non-00 byte found after name '{currentName.Name}' (File Offset: 0x{currentName.OriginalFileOffset:X}) to start metadata search.");
                        }

                        int offsetOf01Marker = -1;
                        int scannedFmtCode = -1;
                        for (int scan = firstNonZeroAfterName; scan < segmentData.Length - 1; scan++) {
                            if (segmentData[scan] == 0x01) {
                                byte potential = segmentData[scan + 1];
                                if (KnownFormatCodes.Contains(potential)) {
                                    offsetOf01Marker = scan;
                                    scannedFmtCode = potential;
                                    break;
                                }
                            }
                        }

                        if (offsetOf01Marker == -1) {
                            throw new TxdExportException($"      FATAL ERROR: Metadata signature (01 <known_fmt_code>) not found for '{currentName.Name}' (File Offset: 0x{currentName.OriginalFileOffset:X}) after first non-00 byte at seg_offset 0x{firstNonZeroAfterName:X}.");
                        }

                        int metaOffset = offsetOf01Marker - 2;
                        if (metaOffset < 0) {
                            throw new TxdExportException($"      FATAL ERROR: Calculated metadata block start (seg_offset 0x{metaOffset:X}) is negative for '{currentName.Name}' (01_marker at 0x{offsetOf01Marker:X}). Structural issue.");
                        }

                        if (metaOffset + 16 > segmentData.Length) {
                            throw new TxdExportException($"      FATAL ERROR: Not enough data for 16-byte metadata block for '{currentName.Name}' (File Offset: 0x{currentName.OriginalFileOffset:X}). Needed 16 bytes from calculated seg_offset 0x{metaOffset:X}, segment length {segmentData.Length}.");
                        }

                        System.Span<byte> metadata = segmentData.AsSpan(metaOffset, 16);
                        byte fmtCodeFromBlock = metadata[3];
                        if (fmtCodeFromBlock != scannedFmtCode) {
                            throw new TxdExportException($"      FATAL ERROR: Format code mismatch for '{currentName.Name}'. Scanned 01 {scannedFmtCode:02X} (fmt_code at seg_offset 0x{offsetOf01Marker + 1:X}), but metadata_bytes[3] (at seg_offset 0x{metaOffset + 3:X}) is {fmtCodeFromBlock:02X}. Alignment error.");
                        }

                        byte fmtCode = fmtCodeFromBlock;
                        Log.Cyan($"      Processing metadata for '{currentName.Name}' (Format Code 0x{fmtCode:02X} from metadata at seg_offset 0x{metaOffset:X})");
                        ushort width = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(metadata.Slice(4, 2));
                        ushort height = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(metadata.Slice(6, 2));
                        byte mipMapCountFromFile = metadata[9];
                        System.UInt32 totalPixelDataSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(metadata.Slice(12, 4));
                        Log.Cyan($"        Meta Details - W: {width}, H: {height}, MipsFromFile: {mipMapCountFromFile}, DataSize: {totalPixelDataSize}");

                        if (width == 0 || height == 0) {
                            if (width == 0 && height == 0) {
                                Log.Yellow($"          Skipping '{currentName.Name}' (File Offset: 0x{currentName.OriginalFileOffset:X}) due to zero dimensions (placeholder).");
                                currentName.MarkProcessed();
                                i = System.Math.Min(metaOffset + 16, segmentData.Length);
                                continue;
                            }
                            throw new TxdExportException($"          FATAL ERROR: Invalid metadata (W:{width}, H:{height}, one is zero) for '{currentName.Name}' (File Offset: 0x{currentName.OriginalFileOffset:X}).");
                        }

                        if (totalPixelDataSize == 0) {
                            throw new TxdExportException($"          FATAL ERROR: Invalid metadata (Size:{totalPixelDataSize} with W:{width}, H:{height}) for '{currentName.Name}' (File Offset: 0x{currentName.OriginalFileOffset:X}).");
                        }

                        int pixelDataStart = metaOffset + 16;
                        int actualMipDataSize = (int)totalPixelDataSize;
                        if (pixelDataStart + actualMipDataSize > segmentData.Length) {
                            throw new TxdExportException($"          FATAL ERROR: Not enough pixel data for '{currentName.Name}' (File Offset: 0x{currentName.OriginalFileOffset:X}). Expected {actualMipDataSize} from seg_offset 0x{pixelDataStart:X}, available: {segmentData.Length - pixelDataStart}.");
                        }

                        System.Span<byte> swizzledBaseMipData = segmentData.AsSpan(pixelDataStart, actualMipDataSize);
                        ConversionResult conversion = TextureFormatConverter.Convert(fmtCode, width, height, mipMapCountFromFile, swizzledBaseMipData, actualMipDataSize, segmentOriginalStartOffset, currentName);

                        if (conversion.Header == null || conversion.Pixels == null) {
                            string reason = conversion.NeedsUnswizzle && conversion.Pixels == null
                                ? $"failed to unswizzle data (format 0x{fmtCode:02X}, {conversion.BytesPerPixel}bpp)"
                                : "pixel data processing failed";
                            throw new TxdExportException($"          FATAL ERROR: Failed to generate exportable DDS data for known format 0x{fmtCode:02X} for texture '{currentName.Name}' (File 0x{currentName.OriginalFileOffset:X}). Reason: {reason}.");
                        }

                        string cleanName = SanitizeFilename(currentName.Name) ?? $"texture_at_0x{currentName.OriginalFileOffset:08X}";
                        string ext = outputExtension.StartsWith(".") ? outputExtension : "." + outputExtension;
                        string outFile = System.IO.Path.Combine(outputDir, cleanName + ext);
                        try {
                            if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase)) {
                                // 1. Combine Header and Pixels into a single in-memory DDS buffer
                                byte[] ddsData = new byte[conversion.Header.Length + conversion.Pixels.Length];
                                System.Buffer.BlockCopy(conversion.Header, 0, ddsData, 0, conversion.Header.Length);
                                System.Buffer.BlockCopy(conversion.Pixels, 0, ddsData, conversion.Header.Length, conversion.Pixels.Length);

                                // 2. Decode the DDS buffer
                                using System.IO.MemoryStream ddsStream = new(ddsData);
                                BcDecoder decoder = new BcDecoder();
                                // DecodeToImageRgba32 is provided by the BCnEncoder.Net.ImageSharp extension
                                using Image<SixLabors.ImageSharp.PixelFormats.Rgba32> image = decoder.DecodeToImageRgba32(ddsStream);

                                // 3. Save as PNG
                                image.SaveAsPng(outFile);
                            } else {
                                // Standard DDS Export
                                using System.IO.FileStream fs = System.IO.File.Create(outFile);
                                fs.Write(conversion.Header, 0, conversion.Header.Length);
                                fs.Write(conversion.Pixels, 0, conversion.Pixels.Length);
                            }
                        } catch (System.IO.IOException ex) {
                            throw new TxdExportException($"          FATAL ERROR: IOError writing {ext.ToUpper()} file {outFile} for '{currentName.Name}': {ex.Message}");
                        } catch (System.Exception ex) {
                            Shared.Diagnostics.Bug($"[TextureSegmentProcessor] convert/save catch triggered for '{currentName.Name}' to '{outFile}': {ex}");
                            throw new TxdExportException($"          FATAL ERROR: Failed to convert/save {ext.ToUpper()} for '{currentName.Name}': {ex.Message}");
                        }

                        Log.Cyan($"          Successfully exported: {outFile} (Format: {conversion.Format}, {width}x{height})");
                        texturesFound += 1;
                        currentName.MarkProcessed();
                        i = System.Math.Min(pixelDataStart + actualMipDataSize, segmentData.Length);
                        continue;
                    }

                    Log.Yellow($"    WARNING: Name signature {System.BitConverter.ToString(NameSignature).Replace("-", string.Empty).ToLowerInvariant()} at seg_offset 0x{nameSigOffset:X} (file 0x{segmentOriginalStartOffset + nameSigOffset:X}) failed full name parsing (no double null found).");
                    if (currentName is not null && !currentName.ProcessedMeta) {
                        Log.Yellow($"      WARNING: Discarding pending name '{currentName.Name}' (sig at file 0x{currentName.OriginalFileOffset:X}) due to malformed subsequent name signature.");
                    }

                    currentName = null;
                    i = nameSigOffset + 1;
                    continue;
                }

                i += 1;
            }

            return currentName is not null && !currentName.ProcessedMeta
                ? throw new TxdExportException($"  WARNING: End of segment reached. Pending name '{currentName.Name}' (sig at file 0x{currentName.OriginalFileOffset:X}) did not find or complete its metadata processing.")
                : texturesFound == 0
                ? throw new TxdExportException($"  No textures successfully exported from segment starting at file offset 0x{segmentOriginalStartOffset:X} that met all processing criteria.")
                : texturesFound;
        }
    }

}
