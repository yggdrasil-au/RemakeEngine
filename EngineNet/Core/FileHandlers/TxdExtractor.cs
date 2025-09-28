//
using System.Buffers.Binary;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EngineNet.Core.FileHandlers;

public static class TxdExtractor {
    private sealed class Options {
        public String InputPath = String.Empty;
        public String? OutputDirectory;
    }

    private sealed class TxdExportException:Exception {
        public TxdExportException(String message) : base(message) { }
    }

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false, false);

    /// <summary>
    /// Extracts textures and metadata from TXD inputs. Supports a single positional input path and optional --output_dir.
    /// </summary>
    /// <param name="args">CLI-style args: [input_path] [--output_dir DIR]</param>
    /// <returns>True if extraction completed successfully.</returns>
    public static Boolean Run(IList<String> args) {
        try {
            Options options = Parse(args);
            TxdExporter exporter = new();
            _ = exporter.ExportPath(options.InputPath, options.OutputDirectory);
            return true;
        } catch (TxdExportException ex) {
            Log.Red(ex.Message);
            return false;
        } catch (Exception ex) {
            Log.Red($"Unhandled TXD extraction error: {ex.Message}");
            if (!String.IsNullOrWhiteSpace(ex.StackTrace)) {
                Log.Gray(ex.StackTrace!);
            }

            return false;
        }
    }

    private static Options Parse(IList<String> args) {
        if (args is null || args.Count == 0) {
            throw new TxdExportException("Missing input path argument for TXD extraction.");
        }

        Options options = new();
        for (Int32 i = 0; i < args.Count; i++) {
            String current = args[i];
            if (String.IsNullOrWhiteSpace(current)) {
                continue;
            }

            switch (current) {
                case "-o":
                case "--output_dir": {
                    if (i + 1 >= args.Count) {
                        throw new TxdExportException($"Option '{current}' expects a value.");
                    }

                    options.OutputDirectory = args[++i];
                    break;
                }
                default: {
                    if (current.StartsWith('-')) {
                        throw new TxdExportException($"Unknown argument '{current}'.");
                    }

                    options.InputPath = String.IsNullOrEmpty(options.InputPath)
                        ? current
                        : throw new TxdExportException($"Unexpected extra positional argument '{current}'.");
                    break;
                }
            }
        }

        if (String.IsNullOrWhiteSpace(options.InputPath)) {
            throw new TxdExportException("Missing input path argument for TXD extraction.");
        }

        options.InputPath = Path.GetFullPath(options.InputPath);
        if (!String.IsNullOrWhiteSpace(options.OutputDirectory)) {
            options.OutputDirectory = Path.GetFullPath(options.OutputDirectory!);
        }

        return options;
    }
    private static Int32 MortonEncode2D(Int32 x, Int32 y) {
        x = (x | (x << 8)) & 0x00FF00FF;
        x = (x | (x << 4)) & 0x0F0F0F0F;
        x = (x | (x << 2)) & 0x33333333;
        x = (x | (x << 1)) & 0x55555555;

        y = (y | (y << 8)) & 0x00FF00FF;
        y = (y | (y << 4)) & 0x0F0F0F0F;
        y = (y | (y << 2)) & 0x33333333;
        y = (y | (y << 1)) & 0x55555555;

        return x | (y << 1);
    }

    private static Byte[]? UnswizzleData(ReadOnlySpan<Byte> swizzledData, Int32 width, Int32 height, Int32 bytesPerPixel) {
        Int32 linearSize = width * height * bytesPerPixel;
        if (swizzledData.IsEmpty || swizzledData.Length < linearSize) {
            Log.Yellow($"      Warning: Swizzled data length ({swizzledData.Length}) is less than expected ({linearSize}) for {width}x{height}@{bytesPerPixel}bpp. Skipping unswizzle.");
            return null;
        }

        Byte[] linear = new Byte[linearSize];
        for (Int32 y = 0; y < height; y++) {
            for (Int32 x = 0; x < width; x++) {
                Int32 mortonIdx = MortonEncode2D(x, y);
                Int32 pixelStart = mortonIdx * bytesPerPixel;
                if (pixelStart + bytesPerPixel > swizzledData.Length) {
                    continue;
                }

                Int32 linearStart = ((y * width) + x) * bytesPerPixel;
                swizzledData.Slice(pixelStart, bytesPerPixel).CopyTo(linear.AsSpan(linearStart, bytesPerPixel));
            }
        }

        return linear;
    }

    private static String? SanitizeFilename(String name) {
        if (String.IsNullOrWhiteSpace(name)) {
            return null;
        }

        StringBuilder builder = new(name.Length);
        foreach (Char ch in name) {
            _ = ch is < (Char)32 or (Char)127
                ? builder.Append('_')
                : ch is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' ? builder.Append('_') : builder.Append(ch);
        }

        String cleaned = builder.ToString().Trim();
        return String.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static Int32 CalculateDxtLevelSize(Int32 width, Int32 height, String fourcc) {
        if (width <= 0 || height <= 0) {
            return 0;
        }

        Int32 blocksWide = Math.Max(1, (width + 3) / 4);
        Int32 blocksHigh = Math.Max(1, (height + 3) / 4);
        Int32 bytesPerBlock = fourcc switch {
            "DXT1" => 8,
            "DXT3" => 16,
            "DXT5" => 16,
            _ => 0
        };
        return blocksWide * blocksHigh * bytesPerBlock;
    }

    private static Byte[] CreateDdsHeaderDxt(Int32 width, Int32 height, Int32 mipMapCountFromFile, String fourcc) {
        Byte[] buffer = new Byte[128];
        using MemoryStream ms = new(buffer);
        using BinaryWriter writer = new(ms, Encoding.ASCII, leaveOpen: true);

        const Int32 DDSD_CAPS = 0x1;
        const Int32 DDSD_HEIGHT = 0x2;
        const Int32 DDSD_WIDTH = 0x4;
        const Int32 DDSD_PIXELFORMAT = 0x1000;
        const Int32 DDSD_MIPMAPCOUNT = 0x20000;
        const Int32 DDSD_LINEARSIZE = 0x80000;

        Int32 flags = DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT | DDSD_LINEARSIZE;
        if (mipMapCountFromFile > 0) {
            flags |= DDSD_MIPMAPCOUNT;
        }

        Int32 dwMipMapCount = mipMapCountFromFile > 0 ? mipMapCountFromFile : 1;
        Int32 linearSize = CalculateDxtLevelSize(width, height, fourcc);

        writer.Write(Encoding.ASCII.GetBytes("DDS "));
        writer.Write(124);
        writer.Write(flags);
        writer.Write(height);
        writer.Write(width);
        writer.Write(linearSize);
        writer.Write(0);
        writer.Write(dwMipMapCount);

        for (Int32 i = 0; i < 11; i++) {
            writer.Write(0);
        }

        const Int32 pfSize = 32;
        const Int32 DDPF_FOURCC = 0x4;
        writer.Write(pfSize);
        writer.Write(DDPF_FOURCC);
        Byte[] fourccBytes = new Byte[4];
        Byte[] srcFourcc = Encoding.ASCII.GetBytes(fourcc);
        Array.Copy(srcFourcc, fourccBytes, Math.Min(srcFourcc.Length, 4));
        writer.Write(fourccBytes);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        const Int32 DDSCAPS_TEXTURE = 0x1000;
        const Int32 DDSCAPS_MIPMAP = 0x400000;
        const Int32 DDSCAPS_COMPLEX = 0x8;
        Int32 caps = DDSCAPS_TEXTURE;
        if (dwMipMapCount > 1) {
            caps |= DDSCAPS_MIPMAP | DDSCAPS_COMPLEX;
        }

        writer.Write(caps);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        return buffer;
    }

    private static Byte[] CreateDdsHeaderRgba(Int32 width, Int32 height, Int32 mipMapCount) {
        Byte[] buffer = new Byte[128];
        using MemoryStream ms = new(buffer);
        using BinaryWriter writer = new(ms, Encoding.ASCII, leaveOpen: true);

        const Int32 DDSD_CAPS = 0x1;
        const Int32 DDSD_HEIGHT = 0x2;
        const Int32 DDSD_WIDTH = 0x4;
        const Int32 DDSD_PIXELFORMAT = 0x1000;
        const Int32 DDSD_PITCH = 0x8;

        writer.Write(Encoding.ASCII.GetBytes("DDS "));
        writer.Write(124);
        writer.Write(DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT | DDSD_PITCH);
        writer.Write(height);
        writer.Write(width);
        writer.Write(width * 4);
        writer.Write(0);
        writer.Write(mipMapCount > 0 ? mipMapCount : 1);

        for (Int32 i = 0; i < 11; i++) {
            writer.Write(0);
        }

        const Int32 pfSize = 32;
        const Int32 DDPF_RGB = 0x40;
        const Int32 DDPF_ALPHAPIXELS = 0x1;
        writer.Write(pfSize);
        writer.Write(DDPF_RGB | DDPF_ALPHAPIXELS);
        writer.Write(0);
        writer.Write(32);
        writer.Write(unchecked(0x000000FF));
        writer.Write(unchecked(0x0000FF00));
        writer.Write(unchecked(0x00FF0000));
        writer.Write(unchecked((Int32)0xFF000000));

        const Int32 DDSCAPS_TEXTURE = 0x1000;
        writer.Write(DDSCAPS_TEXTURE);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        return buffer;
    }
    private sealed class NameInfo {
        public NameInfo(String name, Int32 nameSigOffsetInSegment, Int32 originalFileOffset) {
            Name = name;
            NameSigOffsetInSegment = nameSigOffsetInSegment;
            OriginalFileOffset = originalFileOffset;
        }

        public String Name {
            get;
        }
        public Int32 NameSigOffsetInSegment {
            get;
        }
        public Int32 OriginalFileOffset {
            get;
        }
        public Boolean ProcessedMeta {
            get; private set;
        }

        public void MarkProcessed() {
            ProcessedMeta = true;
        }
    }

    private sealed class Segment {
        public Segment(Int32 startOffset, Byte[] data) {
            StartOffset = startOffset;
            Data = data;
        }

        public Int32 StartOffset {
            get;
        }
        public Byte[] Data {
            get;
        }
    }

    private readonly struct ConversionResult {
        public ConversionResult(Byte[]? header, Byte[]? pixels, String? format, Boolean needsUnswizzle, Int32 bytesPerPixel) {
            Header = header;
            Pixels = pixels;
            Format = format;
            NeedsUnswizzle = needsUnswizzle;
            BytesPerPixel = bytesPerPixel;
        }

        public Byte[]? Header {
            get;
        }
        public Byte[]? Pixels {
            get;
        }
        public String? Format {
            get;
        }
        public Boolean NeedsUnswizzle {
            get;
        }
        public Int32 BytesPerPixel {
            get;
        }
    }
    private sealed class TextureFormatConverter {
        public ConversionResult Convert(
            Int32 fmtCode,
            Int32 width,
            Int32 height,
            Int32 mipMapCountFromFile,
            ReadOnlySpan<Byte> swizzledBaseMipData,
            Int32 actualMipDataSize,
            Int32 segmentOriginalStartOffset,
            NameInfo nameInfo) {

            Boolean needsUnswizzle = false;
            Int32 bytesPerPixelForUns = 0;
            Byte[]? ddsHeader = null;
            Byte[]? outputPixels = null;
            String? exportFormat;
            if (fmtCode == 0x52) {
                ddsHeader = CreateDdsHeaderDxt(width, height, mipMapCountFromFile, "DXT1");
                outputPixels = swizzledBaseMipData.ToArray();
                exportFormat = "DXT1";
                Log.Cyan($"        (Debug) DXT1 format detected. Size: {actualMipDataSize} bytes.");
            } else if (fmtCode == 0x53) {
                ddsHeader = CreateDdsHeaderDxt(width, height, mipMapCountFromFile, "DXT3");
                outputPixels = swizzledBaseMipData.ToArray();
                exportFormat = "DXT3";
                Log.Cyan($"        (Debug) DXT3 format detected. Size: {actualMipDataSize} bytes.");
            } else if (fmtCode == 0x54) {
                ddsHeader = CreateDdsHeaderDxt(width, height, mipMapCountFromFile, "DXT5");
                outputPixels = swizzledBaseMipData.ToArray();
                exportFormat = "DXT5";
                Log.Cyan($"        (Debug) DXT5 format detected. Size: {actualMipDataSize} bytes.");
            } else if (fmtCode == 0x86) {
                exportFormat = "RGBA8888 (from Swizzled BGRA)";
                Int32 expectedSize = width * height * 4;
                bytesPerPixelForUns = 4;
                needsUnswizzle = true;
                Log.Cyan($"        (Debug) Swizzled BGRA format detected. Size: {actualMipDataSize} bytes.");
                if (actualMipDataSize != expectedSize) {
                    throw new TxdExportException($"          FATAL ERROR: Data size mismatch for BGRA '{nameInfo.Name}' (File 0x{nameInfo.OriginalFileOffset:X}): expected {expectedSize}, got {actualMipDataSize}.");
                }

                Byte[]? linear = UnswizzleData(swizzledBaseMipData, width, height, bytesPerPixelForUns);
                if (linear != null) {
                    outputPixels = new Byte[linear.Length];
                    for (Int32 pix = 0; pix < linear.Length; pix += 4) {
                        outputPixels[pix + 0] = linear[pix + 2];
                        outputPixels[pix + 1] = linear[pix + 1];
                        outputPixels[pix + 2] = linear[pix + 0];
                        outputPixels[pix + 3] = linear[pix + 3];
                    }
                    ddsHeader = CreateDdsHeaderRgba(width, height, 1);
                }
            } else if (fmtCode == 0x02) {
                exportFormat = "RGBA8888 (from Swizzled A8 or P8A8)";
                needsUnswizzle = true;
                Log.Cyan($"        (Debug) Swizzled A8 or P8A8 format detected. Size: {actualMipDataSize} bytes.");
                if (actualMipDataSize == width * height) {
                    Log.Cyan($"        (Debug) A8 format detected. Size: {actualMipDataSize} bytes.");
                    bytesPerPixelForUns = 1;
                    Byte[]? linear = UnswizzleData(swizzledBaseMipData, width, height, bytesPerPixelForUns);
                    if (linear != null) {
                        outputPixels = new Byte[width * height * 4];
                        for (Int32 pix = 0; pix < width * height; pix++) {
                            Byte alpha = linear[pix];
                            Int32 idx = pix * 4;
                            outputPixels[idx + 0] = 0;
                            outputPixels[idx + 1] = 0;
                            outputPixels[idx + 2] = 0;
                            outputPixels[idx + 3] = alpha;
                        }
                        ddsHeader = CreateDdsHeaderRgba(width, height, 1);
                    }
                } else if (actualMipDataSize == width * height * 2) {
                    bytesPerPixelForUns = 2;
                    Byte[]? linear = UnswizzleData(swizzledBaseMipData, width, height, bytesPerPixelForUns);
                    if (linear != null) {
                        outputPixels = new Byte[width * height * 4];
                        Log.Cyan($"        (Debug) P8A8/L8A8 format detected. Size: {actualMipDataSize} bytes.");
                        for (Int32 pix = 0; pix < width * height; pix++) {
                            Int32 idx = pix * 2;
                            Byte p8 = linear[idx + 0];
                            Byte a8 = linear[idx + 1];
                            Int32 outIdx = pix * 4;
                            outputPixels[outIdx + 0] = p8;
                            outputPixels[outIdx + 1] = p8;
                            outputPixels[outIdx + 2] = p8;
                            outputPixels[outIdx + 3] = a8;
                        }
                        ddsHeader = CreateDdsHeaderRgba(width, height, 1);
                    }
                } else {
                    throw new TxdExportException($"          FATAL ERROR: Data size mismatch for Format 0x02 '{nameInfo.Name}' (File 0x{nameInfo.OriginalFileOffset:X}): expected {width * height} or {width * height * 2}, got {actualMipDataSize}.");
                }
            } else {
                throw new TxdExportException($"          FATAL ERROR: Unknown or unsupported format code 0x{fmtCode:X2} for texture '{nameInfo.Name}' (File 0x{nameInfo.OriginalFileOffset:X}).");
            }

            return new ConversionResult(ddsHeader, outputPixels, exportFormat, needsUnswizzle, bytesPerPixelForUns);
        }
    }
    private sealed class SegmentScanner {
        private static readonly Byte[] SigFileStart = { 0x16, 0x00, 0x00, 0x00 };
        private static readonly Byte[] SigBlockStart = { 0x03, 0x00, 0x00, 0x00, 0x14, 0x00, 0x00, 0x00 };
        private static readonly Byte[] TextureNameSignature = { 0x2D, 0x00, 0x02, 0x1C, 0x00, 0x00, 0x00, 0x0A };
        private static readonly Byte[] EofPrefix = { 0x03, 0x00, 0x00, 0x00, 0x14, 0x00, 0x00, 0x00, 0x2D, 0x00, 0x02, 0x1C, 0x2F, 0xEA, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x2D, 0x00, 0x02, 0x1C };
        private static readonly Byte[] EofSuffix = { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x2D, 0x00, 0x02, 0x1C };
        private const Int32 LenEofVariablePart = 8;

        private readonly Byte[] _data;
        private readonly String _txdFilePath;
        private readonly Int32 _lenEofSignature;

        public SegmentScanner(Byte[] data, String txdFilePath) {
            _data = data;
            _txdFilePath = txdFilePath;
            _lenEofSignature = EofPrefix.Length + LenEofVariablePart + EofSuffix.Length;
        }

        public (List<Segment> segments, Int32 totalTextures) CollectSegments() {
            Byte[] data = _data;
            List<Segment> segments = [];

            List<Int32> eofOccurrences = [];
            Int32 searchIdx = 0;
            while (true) {
                Int32 pos = FindEofPattern(searchIdx);
                if (pos != -1) {
                    eofOccurrences.Add(pos);
                    searchIdx = pos + 1;
                } else {
                    break;
                }
            }

            Int32 totalEofPatterns = eofOccurrences.Count;
            Log.Blue($"  Found {totalEofPatterns} occurrences of EOF_SIGNATURE pattern in the entire file.");
            if (totalEofPatterns != 1) {
                if (totalEofPatterns == 0) {
                    throw new TxdExportException("  ERROR: EOF pattern not found in the file. This may indicate a corrupted or incomplete TXD file.");
                }

                throw new TxdExportException($"  ERROR: Expected 1 EOF pattern, found {totalEofPatterns}. This may indicate a corrupted or incomplete TXD file.");
            }

            Int32 totalSigFileStart = CountOccurrences(data, SigFileStart);
            Log.Blue($"  Found {totalSigFileStart} occurrences of sig_file_start in the entire file.");

            Int32 totalSigBlockStart = CountOccurrences(data, SigBlockStart);
            Log.Blue($"  Found {totalSigBlockStart} occurrences of sig_block_start in the entire file.");

            Int32 totalSigCompoundEndMarker = totalSigBlockStart;
            Log.Blue($"  Found {totalSigCompoundEndMarker} occurrences of sig_compound_end_marker in the entire file.");

            Int32 totalTextureNameSignature = CountOccurrences(data, TextureNameSignature);
            Log.Blue($"  Found {totalTextureNameSignature} occurrences of texture_name_signature in the entire file.");

            Int32 totalTextures = 0;
            if (totalSigBlockStart == totalSigCompoundEndMarker && totalSigBlockStart == totalTextureNameSignature) {
                totalTextures = totalTextureNameSignature;
            }

            Int32 searchPtr;
            if (StartsWith(data, 0, SigFileStart)) {
                Log.Cyan("  File starts with sig_file_start (0x16). Processing initial segment.");
                Int32 startAfter16 = SigFileStart.Length;
                Int32 posMarker = IndexOf(data, SigBlockStart, startAfter16);
                if (posMarker != -1) {
                    if (IsEofPatternAt(posMarker)) {
                        Log.Cyan($"      0x16 segment data (offset 0x{startAfter16:X}) ends before EOF_SIGNATURE pattern found at 0x{posMarker:X}.");
                        Byte[] segmentData = data.AsSpan(startAfter16, posMarker - startAfter16).ToArray();
                        if (segmentData.Length > 0) {
                            segments.Add(new Segment(startAfter16, segmentData));
                        }

                        searchPtr = data.Length;
                    } else {
                        Log.Cyan($"      0x16 segment data (offset 0x{startAfter16:X}) ends before sig_compound_end_marker at 0x{posMarker:X}.");
                        Byte[] segmentData = data.AsSpan(startAfter16, posMarker - startAfter16).ToArray();
                        if (segmentData.Length > 0) {
                            segments.Add(new Segment(startAfter16, segmentData));
                        }

                        searchPtr = posMarker;
                    }
                } else {
                    Int32 posEof = FindEofPattern(startAfter16);
                    if (posEof != -1) {
                        Log.Cyan($"      0x16 segment data (offset 0x{startAfter16:X}) ends before EOF_SIGNATURE pattern (direct find) at 0x{posEof:X}.");
                        Byte[] segmentData = data.AsSpan(startAfter16, posEof - startAfter16).ToArray();
                        if (segmentData.Length > 0) {
                            segments.Add(new Segment(startAfter16, segmentData));
                        }

                        searchPtr = data.Length;
                    } else {
                        Log.Yellow("      Warning: No sig_compound_end_marker or EOF_SIGNATURE pattern found after 0x16 segment start. Assuming 0x16 data to end of file.");
                        Byte[] segmentData = data.AsSpan(startAfter16).ToArray();
                        if (segmentData.Length > 0) {
                            segments.Add(new Segment(startAfter16, segmentData));
                        }

                        searchPtr = data.Length;
                    }
                }
            } else {
                Log.Yellow("  File does not start with sig_file_start (0x16). Will scan for 0x14 blocks from beginning.");
                searchPtr = 0;
            }

            Int32 currentScanPos = searchPtr;
            while (currentScanPos < data.Length) {
                Int32 foundBlockStart = IndexOf(data, SigBlockStart, currentScanPos);
                if (foundBlockStart == -1) {
                    Log.Blue($"  No more sig_block_start (or EOF pattern prefix) found after offset 0x{currentScanPos:X}. Ending 0x14 block scan.");
                    break;
                }

                if (IsEofPatternAt(foundBlockStart)) {
                    Log.Cyan($"  Encountered EOF_SIGNATURE pattern at 0x{foundBlockStart:X} while searching for a 0x14 block start. Ending block scan.");
                    break;
                }

                Log.Cyan($"  Found 0x14 block start signature at file offset 0x{foundBlockStart:X}.");
                Int32 startAfter14 = foundBlockStart + SigBlockStart.Length;
                Int32 posNextMarker = IndexOf(data, SigBlockStart, startAfter14);
                if (posNextMarker != -1) {
                    if (IsEofPatternAt(posNextMarker)) {
                        Log.Cyan($"      0x14 block (data from 0x{startAfter14:X}) ends before EOF_SIGNATURE pattern (found as next marker) at 0x{posNextMarker:X}.");
                        Byte[] segmentData = data.AsSpan(startAfter14, posNextMarker - startAfter14).ToArray();
                        if (segmentData.Length > 0) {
                            segments.Add(new Segment(startAfter14, segmentData));
                        }

                        currentScanPos = data.Length;
                    } else {
                        Log.Cyan($"      0x14 block (data from 0x{startAfter14:X}) ends before next sig_compound_end_marker at 0x{posNextMarker:X}.");
                        Byte[] segmentData = data.AsSpan(startAfter14, posNextMarker - startAfter14).ToArray();
                        if (segmentData.Length > 0) {
                            segments.Add(new Segment(startAfter14, segmentData));
                        }

                        currentScanPos = posNextMarker;
                    }
                } else {
                    Int32 posEof = FindEofPattern(startAfter14);
                    if (posEof != -1) {
                        Log.Cyan($"      0x14 block (data from 0x{startAfter14:X}) ends before EOF_SIGNATURE pattern (direct find) at 0x{posEof:X}.");
                        Byte[] segmentData = data.AsSpan(startAfter14, posEof - startAfter14).ToArray();
                        if (segmentData.Length > 0) {
                            segments.Add(new Segment(startAfter14, segmentData));
                        }

                        currentScanPos = data.Length;
                    } else {
                        Log.Yellow($"      Warning: For 0x14 block (data from 0x{startAfter14:X}), no subsequent marker or EOF pattern found. Assuming data to end of file.");
                        Byte[] segmentData = data.AsSpan(startAfter14).ToArray();
                        if (segmentData.Length > 0) {
                            segments.Add(new Segment(startAfter14, segmentData));
                        }

                        currentScanPos = data.Length;
                    }
                }
            }

            if (segments.Count == 0 && StartsWith(data, 0, SigFileStart) && data.Length > 0x28) {
                Log.Yellow("  No segments found by primary rules, but file starts with 0x16. Defaulting to process from offset 0x28 (Noesis-style).");
                Int32 eofFallback = FindEofPattern(0x28);
                if (eofFallback != -1) {
                    segments.Add(new Segment(0x28, data.AsSpan(0x28, eofFallback - 0x28).ToArray()));
                } else {
                    segments.Add(new Segment(0x28, data.AsSpan(0x28).ToArray()));
                }
            } else if (segments.Count == 0) {
                throw new TxdExportException($"  No processable data segments ultimately found in '{_txdFilePath}'.");
            }

            Log.Blue($"\n  Found {segments.Count} segment(s) to process in '{_txdFilePath}'.");
            return (segments, totalTextures);
        }
        private Int32 FindEofPattern(Int32 searchStartOffset) {
            Byte[] data = _data;
            while (searchStartOffset <= data.Length - _lenEofSignature) {
                Int32 prefixPos = IndexOf(data, EofPrefix, searchStartOffset);
                if (prefixPos == -1) {
                    return -1;
                }

                if (prefixPos + _lenEofSignature > data.Length) {
                    return -1;
                }

                Int32 expectedSuffixPos = prefixPos + EofPrefix.Length + LenEofVariablePart;
                if (StartsWith(data, expectedSuffixPos, EofSuffix)) {
                    return prefixPos;
                }

                searchStartOffset = prefixPos + 1;
            }
            return -1;
        }

        private Boolean IsEofPatternAt(Int32 position) {
            if (position < 0 || position + _lenEofSignature > _data.Length) {
                return false;
            }

            if (!StartsWith(_data, position, EofPrefix)) {
                return false;
            }

            Int32 expectedSuffixStart = position + EofPrefix.Length + LenEofVariablePart;
            return StartsWith(_data, expectedSuffixStart, EofSuffix);
        }
    }
    private sealed class TextureSegmentProcessor {
        private readonly TextureFormatConverter _converter = new();
        private static readonly Byte[] NameSignature = { 0x2D, 0x00, 0x02, 0x1C, 0x00, 0x00, 0x00, 0x0A };
        private static readonly HashSet<Byte> KnownFormatCodes = [0x52, 0x53, 0x54, 0x86, 0x02];
        private static readonly Int32 NameSignatureLength = NameSignature.Length;

        public Int32 ProcessSegment(Segment segment, String outputDir) {
            Byte[] segmentData = segment.Data;
            Int32 segmentOriginalStartOffset = segment.StartOffset;
            Int32 texturesFound = 0;
            Int32 i = 0;
            NameInfo? currentName = null;

            Log.Cyan($"  Scanning data segment (len {segmentData.Length}) for textures using signature {BitConverter.ToString(NameSignature).Replace("-", String.Empty).ToLowerInvariant()}...");

            while (i < segmentData.Length) {
                if (currentName?.ProcessedMeta == true) {
                    currentName = null;
                }

                if (i + NameSignatureLength <= segmentData.Length && StartsWith(segmentData, i, NameSignature)) {
                    Int32 nameSigOffset = i;
                    Int32 nameStringStart = nameSigOffset + 12;
                    Int32 nameEndScan = nameStringStart;
                    Log.Green($"    name_sig_offset_in_segment = 0x{nameSigOffset:X} (file offset 0x{segmentOriginalStartOffset + nameSigOffset:X})");
                    Log.Green($"    name_string_start_offset_in_segment = 0x{nameStringStart:X} (file offset 0x{segmentOriginalStartOffset + nameStringStart:X})");
                    Log.Green($"    name_end_scan_in_segment = 0x{nameEndScan:X} (file offset 0x{segmentOriginalStartOffset + nameEndScan:X})");
                    Log.Green($"    Found name signature {BitConverter.ToString(NameSignature).Replace("-", String.Empty).ToLowerInvariant()} at seg_offset 0x{nameSigOffset:X} (file offset 0x{segmentOriginalStartOffset + nameSigOffset:X})");

                    if (nameStringStart + 2 > segmentData.Length) {
                        Log.Yellow($"    WARNING: Found name signature {BitConverter.ToString(NameSignature).Replace("-", String.Empty).ToLowerInvariant()} at seg_offset 0x{nameSigOffset:X}, but not enough data for name string (expected at 0x{nameStringStart:X}).");
                        i = nameSigOffset + 1;
                        continue;
                    }

                    while (nameEndScan < segmentData.Length - 1 && !(segmentData[nameEndScan] == 0x00 && segmentData[nameEndScan + 1] == 0x00)) {
                        nameEndScan += 1;
                    }

                    if (nameEndScan < segmentData.Length - 1 && segmentData[nameEndScan] == 0x00 && segmentData[nameEndScan + 1] == 0x00) {
                        Span<Byte> nameBytes = segmentData.AsSpan(nameStringStart, nameEndScan - nameStringStart);
                        String? nameValue;
                        try {
                            nameValue = Utf8NoBom.GetString(nameBytes).Trim();
                        } catch {
                            nameValue = BitConverter.ToString(nameBytes.ToArray()).Replace("-", String.Empty);
                        }

                        if (String.IsNullOrWhiteSpace(nameValue)) {
                            nameValue = $"unnamed_texture_at_0x{segmentOriginalStartOffset + nameSigOffset:08X}";
                            Log.Red($"    WARNING: Name string parsing failed for signature {BitConverter.ToString(NameSignature).Replace("-", String.Empty).ToLowerInvariant()} at seg_offset 0x{nameSigOffset:X}. Using fallback name '{nameValue}' (sig at file 0x{segmentOriginalStartOffset + nameSigOffset:X}).");
                        }

                        if (currentName is not null && !currentName.ProcessedMeta) {
                            Log.Yellow($"    WARNING: Previous name '{currentName.Name}' (sig at file 0x{currentName.OriginalFileOffset:X}) was pending metadata but new name '{nameValue}' was found.");
                        }

                        currentName = new NameInfo(nameValue, nameSigOffset, segmentOriginalStartOffset + nameSigOffset);
                        Log.Cyan($"    Parsed name: '{currentName.Name}' (signature {BitConverter.ToString(NameSignature).Replace("-", String.Empty).ToLowerInvariant()} at seg_offset 0x{nameSigOffset:X}, file 0x{currentName.OriginalFileOffset:X})");
                        i = nameEndScan + 2;

                        Int32 firstNonZeroAfterName = -1;
                        Int32 scanPtrForNonZero = i;
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

                        Int32 offsetOf01Marker = -1;
                        Int32 scannedFmtCode = -1;
                        for (Int32 scan = firstNonZeroAfterName; scan < segmentData.Length - 1; scan++) {
                            if (segmentData[scan] == 0x01) {
                                Byte potential = segmentData[scan + 1];
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

                        Int32 metaOffset = offsetOf01Marker - 2;
                        if (metaOffset < 0) {
                            throw new TxdExportException($"      FATAL ERROR: Calculated metadata block start (seg_offset 0x{metaOffset:X}) is negative for '{currentName.Name}' (01_marker at 0x{offsetOf01Marker:X}). Structural issue.");
                        }

                        if (metaOffset + 16 > segmentData.Length) {
                            throw new TxdExportException($"      FATAL ERROR: Not enough data for 16-byte metadata block for '{currentName.Name}' (File Offset: 0x{currentName.OriginalFileOffset:X}). Needed 16 bytes from calculated seg_offset 0x{metaOffset:X}, segment length {segmentData.Length}.");
                        }

                        Span<Byte> metadata = segmentData.AsSpan(metaOffset, 16);
                        Byte fmtCodeFromBlock = metadata[3];
                        if (fmtCodeFromBlock != scannedFmtCode) {
                            throw new TxdExportException($"      FATAL ERROR: Format code mismatch for '{currentName.Name}'. Scanned 01 {scannedFmtCode:02X} (fmt_code at seg_offset 0x{offsetOf01Marker + 1:X}), but metadata_bytes[3] (at seg_offset 0x{metaOffset + 3:X}) is {fmtCodeFromBlock:02X}. Alignment error.");
                        }

                        Byte fmtCode = fmtCodeFromBlock;
                        Log.Cyan($"      Processing metadata for '{currentName.Name}' (Format Code 0x{fmtCode:02X} from metadata at seg_offset 0x{metaOffset:X})");
                        UInt16 width = BinaryPrimitives.ReadUInt16BigEndian(metadata.Slice(4, 2));
                        UInt16 height = BinaryPrimitives.ReadUInt16BigEndian(metadata.Slice(6, 2));
                        Byte mipMapCountFromFile = metadata[9];
                        UInt32 totalPixelDataSize = BinaryPrimitives.ReadUInt32LittleEndian(metadata.Slice(12, 4));
                        Log.Cyan($"        Meta Details - W: {width}, H: {height}, MipsFromFile: {mipMapCountFromFile}, DataSize: {totalPixelDataSize}");

                        if (width == 0 || height == 0) {
                            if (width == 0 && height == 0) {
                                Log.Yellow($"          Skipping '{currentName.Name}' (File Offset: 0x{currentName.OriginalFileOffset:X}) due to zero dimensions (placeholder).");
                                currentName.MarkProcessed();
                                i = Math.Min(metaOffset + 16, segmentData.Length);
                                continue;
                            }
                            throw new TxdExportException($"          FATAL ERROR: Invalid metadata (W:{width}, H:{height}, one is zero) for '{currentName.Name}' (File Offset: 0x{currentName.OriginalFileOffset:X}).");
                        }

                        if (totalPixelDataSize == 0) {
                            throw new TxdExportException($"          FATAL ERROR: Invalid metadata (Size:{totalPixelDataSize} with W:{width}, H:{height}) for '{currentName.Name}' (File Offset: 0x{currentName.OriginalFileOffset:X}).");
                        }

                        Int32 pixelDataStart = metaOffset + 16;
                        Int32 actualMipDataSize = (Int32)totalPixelDataSize;
                        if (pixelDataStart + actualMipDataSize > segmentData.Length) {
                            throw new TxdExportException($"          FATAL ERROR: Not enough pixel data for '{currentName.Name}' (File Offset: 0x{currentName.OriginalFileOffset:X}). Expected {actualMipDataSize} from seg_offset 0x{pixelDataStart:X}, available: {segmentData.Length - pixelDataStart}.");
                        }

                        Span<Byte> swizzledBaseMipData = segmentData.AsSpan(pixelDataStart, actualMipDataSize);
                        ConversionResult conversion = _converter.Convert(fmtCode, width, height, mipMapCountFromFile, swizzledBaseMipData, actualMipDataSize, segmentOriginalStartOffset, currentName);

                        if (conversion.Header == null || conversion.Pixels == null) {
                            String reason = conversion.NeedsUnswizzle && conversion.Pixels == null
                                ? $"failed to unswizzle data (format 0x{fmtCode:02X}, {conversion.BytesPerPixel}bpp)"
                                : "pixel data processing failed";
                            throw new TxdExportException($"          FATAL ERROR: Failed to generate exportable DDS data for known format 0x{fmtCode:02X} for texture '{currentName.Name}' (File 0x{currentName.OriginalFileOffset:X}). Reason: {reason}.");
                        }

                        String cleanName = SanitizeFilename(currentName.Name) ?? $"texture_at_0x{currentName.OriginalFileOffset:08X}";
                        String ddsFile = Path.Combine(outputDir, cleanName + ".dds");
                        try {
                            using FileStream fs = File.Create(ddsFile);
                            fs.Write(conversion.Header, 0, conversion.Header.Length);
                            fs.Write(conversion.Pixels, 0, conversion.Pixels.Length);
                        } catch (IOException ex) {
                            throw new TxdExportException($"          FATAL ERROR: IOError writing DDS file {ddsFile} for '{currentName.Name}': {ex.Message}");
                        }

                        Log.Cyan($"          Successfully exported: {ddsFile} (Format: {conversion.Format}, {width}x{height})");
                        texturesFound += 1;
                        currentName.MarkProcessed();
                        i = Math.Min(pixelDataStart + actualMipDataSize, segmentData.Length);
                        continue;
                    }

                    Log.Yellow($"    WARNING: Name signature {BitConverter.ToString(NameSignature).Replace("-", String.Empty).ToLowerInvariant()} at seg_offset 0x{nameSigOffset:X} (file 0x{segmentOriginalStartOffset + nameSigOffset:X}) failed full name parsing (no double null found).");
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
    private sealed class TxdExporter {
        public (Int32 totalTexturesExported, Int32 filesProcessed, Int32 filesWithExports) ExportPath(String inputPathAbs, String? outputDirBaseArg) {
            Int32 overallTexturesExported = 0;
            Int32 filesProcessedCount = 0;
            Int32 filesWithExports = 0;

            if (!File.Exists(inputPathAbs) && !Directory.Exists(inputPathAbs)) {
                throw new TxdExportException($"Error: Input path '{inputPathAbs}' does not exist.");
            }

            List<String> txdFilesToProcess = [];
            if (File.Exists(inputPathAbs)) {
                if (!inputPathAbs.EndsWith(".txd", StringComparison.OrdinalIgnoreCase)) {
                    throw new TxdExportException($"Error: Input file '{inputPathAbs}' is not a .txd file.");
                }

                txdFilesToProcess.Add(inputPathAbs);
            } else {
                Log.Cyan($"Scanning directory: {inputPathAbs}");
                foreach (String file in Directory.EnumerateFiles(inputPathAbs, "*.txd", SearchOption.AllDirectories)) {
                    txdFilesToProcess.Add(file);
                }

                if (txdFilesToProcess.Count == 0) {
                    throw new TxdExportException($"No .txd files found in directory '{inputPathAbs}'.");
                }
            }

            if (txdFilesToProcess.Count == 0) {
                Log.Red("No .txd files to process.");
                return (overallTexturesExported, filesProcessedCount, filesWithExports);
            }

            Log.Cyan($"Found {txdFilesToProcess.Count} .txd file(s) to process.");

            String lastUsedOutputBaseForSummary = String.Empty;
            foreach (String txdFile in txdFilesToProcess) {
                String? currentOutputDirBase = outputDirBaseArg;
                if (String.IsNullOrEmpty(currentOutputDirBase)) {
                    String baseDir = Path.GetDirectoryName(txdFile) ?? Directory.GetCurrentDirectory();
                    String baseName = Path.GetFileNameWithoutExtension(txdFile);
                    currentOutputDirBase = Path.Combine(baseDir, baseName + "_txd");
                }

                lastUsedOutputBaseForSummary = currentOutputDirBase!;
                Log.Cyan($"\n--- Processing file: {txdFile} ---");
                Int32 texturesInFile = ExportTexturesFromTxd(txdFile, currentOutputDirBase!);
                overallTexturesExported += texturesInFile;
                filesProcessedCount += 1;
                if (texturesInFile > 0) {
                    filesWithExports += 1;
                }
            }

            Log.Cyan("\n--- Summary ---");
            Log.Cyan($"Attempted to process {txdFilesToProcess.Count} .txd file(s).");
            if (filesProcessedCount > 0) {
                Log.Cyan($"Files fully processed: {filesProcessedCount}.");
                Log.Cyan($"Files with at least one texture exported: {filesWithExports}.");
                Log.Cyan($"Total textures exported across all files: {overallTexturesExported}.");
                if (overallTexturesExported > 0) {
                    if (!String.IsNullOrEmpty(outputDirBaseArg)) {
                        Log.Cyan($"Base output directory specified: '{outputDirBaseArg}' (TXD-specific subfolders created within).");
                    } else {
                        Log.Cyan($"Output subdirectories created relative to each input TXD file's location (e.g., '{Path.Combine(lastUsedOutputBaseForSummary, "examplename_txd")}').");
                    }
                }
                if (filesProcessedCount == 858 && overallTexturesExported != 7318) {
                    Log.Yellow($"WARNING: Only {overallTexturesExported} textures were exported. This may indicate that some textures were not processed or exported due to errors.");
                }
            } else {
                Log.Yellow("No .txd files ended up being processed.");
            }

            return (overallTexturesExported, filesProcessedCount, filesWithExports);
        }

        private Int32 ExportTexturesFromTxd(String txdFilePath, String outputDirBase) {
            Log.Cyan($"Processing TXD file: {txdFilePath}");
            Byte[] data;
            try {
                data = File.ReadAllBytes(txdFilePath);
            } catch (FileNotFoundException) {
                Log.Red($"Error: File not found: {txdFilePath}");
                return 0;
            } catch (Exception ex) {
                Log.Red($"Error reading file {txdFilePath}: {ex.Message}");
                return 0;
            }

            if (!Directory.Exists(outputDirBase)) {
                try {
                    _ = Directory.CreateDirectory(outputDirBase);
                    Log.Cyan($"  Created output directory: {outputDirBase}");
                } catch (Exception ex) {
                    throw new TxdExportException($"  Error: Could not create output directory {outputDirBase}: {ex.Message}. Textures from this TXD cannot be saved.");
                }
            }

            SegmentScanner scanner = new(data, txdFilePath);
            (List<Segment> segments, Int32 totalTextures) = scanner.CollectSegments();
            if (segments.Count == 0) {
                return 0;
            }

            Int32 totalTexturesExportedFromFile = 0;
            for (Int32 index = 0; index < segments.Count; index++) {
                Segment segment = segments[index];
                if (segment.Data.Length == 0) {
                    Log.Yellow($"\n  Skipping zero-length segment #{index + 1} (intended to start at file offset 0x{segment.StartOffset:X}).");
                    continue;
                }

                Log.Cyan($"\n  Processing segment #{index + 1}: data starts at file offset 0x{segment.StartOffset:X}, segment length {segment.Data.Length} bytes.");
                Int32 texturesInSegment = new TextureSegmentProcessor().ProcessSegment(segment, outputDirBase);
                totalTexturesExportedFromFile += texturesInSegment;
            }

            if (totalTexturesExportedFromFile > 0) {
                Log.Cyan($"\nFinished processing for '{txdFilePath}'. Exported {totalTexturesExportedFromFile} textures to '{outputDirBase}'.");
            } else {
                Log.Red($"\nNo textures were successfully exported from any identified segments in '{txdFilePath}'.");
            }

            if (totalTexturesExportedFromFile != totalTextures) {
                Log.Yellow($"  WARNING: Number of raw name signatures found ({totalTextures}) does not match number of textures reported as exported ({totalTexturesExportedFromFile}). This could be due to segmentation logic, invalid texture data, or duplicate/unused name entries.");
            }

            return totalTexturesExportedFromFile;
        }
    }
    private static class Log {
        private static readonly Object Sync = new();

        public static void Cyan(String message) {
            Write(ConsoleColor.Cyan, message);
        }

        public static void Blue(String message) {
            Write(ConsoleColor.Blue, message);
        }

        public static void Green(String message) {
            Write(ConsoleColor.Green, message);
        }

        public static void Yellow(String message) {
            Write(ConsoleColor.Yellow, message);
        }

        public static void Red(String message) {
            Write(ConsoleColor.Red, message, true);
        }

        public static void Gray(String message) {
            Write(ConsoleColor.DarkGray, message);
        }

        private static void Write(ConsoleColor colour, String message, Boolean isError = false) {
            lock (Sync) {
                ConsoleColor previous = Console.ForegroundColor;
                Console.ForegroundColor = colour;
                if (isError) {
                    Console.Error.WriteLine(message);
                } else {
                    Console.WriteLine(message);
                }

                Console.ForegroundColor = previous;
            }
        }
    }

    private static Int32 CountOccurrences(ReadOnlySpan<Byte> data, ReadOnlySpan<Byte> pattern) {
        if (pattern.IsEmpty || data.IsEmpty || pattern.Length > data.Length) {
            return 0;
        }

        Int32 count = 0;
        Int32 index = 0;
        while (index <= data.Length - pattern.Length) {
            Int32 found = data[index..].IndexOf(pattern);
            if (found == -1) {
                break;
            }

            count += 1;
            index += found + 1;
        }
        return count;
    }

    private static Int32 IndexOf(ReadOnlySpan<Byte> data, ReadOnlySpan<Byte> pattern, Int32 start) {
        if (pattern.IsEmpty) {
            return start <= data.Length ? start : -1;
        }

        if (start < 0 || start > data.Length - pattern.Length) {
            return -1;
        }

        Int32 pos = data[start..].IndexOf(pattern);
        return pos == -1 ? -1 : start + pos;
    }

    private static Boolean StartsWith(ReadOnlySpan<Byte> data, Int32 offset, ReadOnlySpan<Byte> pattern) {
        return pattern.IsEmpty || (offset >= 0 && offset + pattern.Length <= data.Length && data.Slice(offset, pattern.Length).SequenceEqual(pattern));
    }
}
