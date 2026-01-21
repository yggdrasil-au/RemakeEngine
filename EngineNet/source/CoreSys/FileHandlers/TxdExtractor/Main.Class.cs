
using System.Collections.Generic;

namespace EngineNet.Core.FileHandlers.TxdExtractor;

using System;

internal static partial class Main {

    private sealed class Options {
        internal string InputPath = string.Empty;
        internal string? OutputDirectory;
    }

    private sealed class TxdExportException:System.Exception {
        internal TxdExportException(string message) : base(message) { }
    }

    private sealed class NameInfo {
        internal NameInfo(string name, int nameSigOffsetInSegment, int originalFileOffset) {
            Name = name;
            NameSigOffsetInSegment = nameSigOffsetInSegment;
            OriginalFileOffset = originalFileOffset;
        }

        internal string Name {
            get;
        }
        internal int NameSigOffsetInSegment {
            get;
        }
        internal int OriginalFileOffset {
            get;
        }
        internal bool ProcessedMeta {
            get; private set;
        }

        internal void MarkProcessed() {
            ProcessedMeta = true;
        }
    }

    private sealed class Segment {
        internal Segment(int startOffset, byte[] data) {
            StartOffset = startOffset;
            Data = data;
        }

        internal int StartOffset {
            get;
        }
        internal byte[] Data {
            get;
        }
    }

    private sealed class TextureFormatConverter {
        internal ConversionResult Convert(
            int fmtCode,
            int width,
            int height,
            int mipMapCountFromFile,
            System.ReadOnlySpan<byte> swizzledBaseMipData,
            int actualMipDataSize,
            int segmentOriginalStartOffset,
            NameInfo nameInfo) {

            bool needsUnswizzle = false;
            int bytesPerPixelForUns = 0;
            byte[]? ddsHeader = null;
            byte[]? outputPixels = null;
            string? exportFormat;
            if (fmtCode == 0x52)
            {
                ddsHeader = CreateDdsHeaderDxt(width, height, mipMapCountFromFile, "DXT1");
                outputPixels = swizzledBaseMipData.ToArray();
                exportFormat = "DXT1";
                DebugLog($"        DXT1 format detected. Size: {actualMipDataSize} bytes.");
            }
            else if (fmtCode == 0x53)
            {
                ddsHeader = CreateDdsHeaderDxt(width, height, mipMapCountFromFile, "DXT3");
                outputPixels = swizzledBaseMipData.ToArray();
                exportFormat = "DXT3";
                DebugLog($"        DXT3 format detected. Size: {actualMipDataSize} bytes.");
            }
            else if (fmtCode == 0x54)
            {
                ddsHeader = CreateDdsHeaderDxt(width, height, mipMapCountFromFile, "DXT5");
                outputPixels = swizzledBaseMipData.ToArray();
                exportFormat = "DXT5";
                DebugLog($"        DXT5 format detected. Size: {actualMipDataSize} bytes.");
            }
            else if (fmtCode == 0x86)
            {
                exportFormat = "RGBA8888 (from Swizzled BGRA)";
                int expectedSize = width * height * 4;
                bytesPerPixelForUns = 4;
                needsUnswizzle = true;
                DebugLog($"        Swizzled BGRA format detected. Size: {actualMipDataSize} bytes.");
                if (actualMipDataSize != expectedSize)
                {
                    throw new TxdExportException($"          FATAL ERROR: Data size mismatch for BGRA '{nameInfo.Name}' (File 0x{nameInfo.OriginalFileOffset:X}): expected {expectedSize}, got {actualMipDataSize}.");
                }

                byte[]? linear = UnswizzleData(swizzledBaseMipData, width, height, bytesPerPixelForUns);
                if (linear != null)
                {
                    outputPixels = new byte[linear.Length];
                    for (int pix = 0; pix < linear.Length; pix += 4)
                    {
                        outputPixels[pix + 0] = linear[pix + 2];
                        outputPixels[pix + 1] = linear[pix + 1];
                        outputPixels[pix + 2] = linear[pix + 0];
                        outputPixels[pix + 3] = linear[pix + 3];
                    }
                    ddsHeader = CreateDdsHeaderRgba(width, height, 1);
                }
            }
            else if (fmtCode == 0x02)
            {
                exportFormat = "RGBA8888 (from Swizzled A8 or P8A8)";
                needsUnswizzle = true;
                DebugLog($"        Swizzled A8 or P8A8 format detected. Size: {actualMipDataSize} bytes.");
                if (actualMipDataSize == width * height)
                {
                    DebugLog($"        A8 format detected. Size: {actualMipDataSize} bytes.");
                    bytesPerPixelForUns = 1;
                    byte[]? linear = UnswizzleData(swizzledBaseMipData, width, height, bytesPerPixelForUns);
                    if (linear != null)
                    {
                        outputPixels = new byte[width * height * 4];
                        for (int pix = 0; pix < width * height; pix++)
                        {
                            byte alpha = linear[pix];
                            int idx = pix * 4;
                            outputPixels[idx + 0] = 0;
                            outputPixels[idx + 1] = 0;
                            outputPixels[idx + 2] = 0;
                            outputPixels[idx + 3] = alpha;
                        }
                        ddsHeader = CreateDdsHeaderRgba(width, height, 1);
                    }
                }
                else if (actualMipDataSize == width * height * 2)
                {
                    bytesPerPixelForUns = 2;
                    byte[]? linear = UnswizzleData(swizzledBaseMipData, width, height, bytesPerPixelForUns);
                    if (linear != null)
                    {
                        outputPixels = new byte[width * height * 4];
                        DebugLog($"        P8A8/L8A8 format detected. Size: {actualMipDataSize} bytes.");
                        for (int pix = 0; pix < width * height; pix++)
                        {
                            int idx = pix * 2;
                            byte p8 = linear[idx + 0];
                            byte a8 = linear[idx + 1];
                            int outIdx = pix * 4;
                            outputPixels[outIdx + 0] = p8;
                            outputPixels[outIdx + 1] = p8;
                            outputPixels[outIdx + 2] = p8;
                            outputPixels[outIdx + 3] = a8;
                        }
                        ddsHeader = CreateDdsHeaderRgba(width, height, 1);
                    }
                }
                else
                {
                    throw new TxdExportException($"          FATAL ERROR: Data size mismatch for Format 0x02 '{nameInfo.Name}' (File 0x{nameInfo.OriginalFileOffset:X}): expected {width * height} or {width * height * 2}, got {actualMipDataSize}.");
                }
            }
            else
            {
                throw new TxdExportException($"          FATAL ERROR: Unknown or unsupported format code 0x{fmtCode:X2} for texture '{nameInfo.Name}' (File 0x{nameInfo.OriginalFileOffset:X}).");
            }

            return new ConversionResult(ddsHeader, outputPixels, exportFormat, needsUnswizzle, bytesPerPixelForUns);
        }

    }

    internal readonly struct ConversionResult {
        internal ConversionResult(byte[]? header, byte[]? pixels, string? format, bool needsUnswizzle, int bytesPerPixel) {
            Header = header;
            Pixels = pixels;
            Format = format;
            NeedsUnswizzle = needsUnswizzle;
            BytesPerPixel = bytesPerPixel;
        }

        internal byte[]? Header {
            get;
        }
        internal byte[]? Pixels {
            get;
        }
        internal string? Format {
            get;
        }
        internal bool NeedsUnswizzle {
            get;
        }
        internal int BytesPerPixel {
            get;
        }
    }

    private sealed class SegmentScanner {
        private static readonly byte[] SigFileStart = { 0x16, 0x00, 0x00, 0x00 };
        private static readonly byte[] SigBlockStart = { 0x03, 0x00, 0x00, 0x00, 0x14, 0x00, 0x00, 0x00 };
        private static readonly byte[] TextureNameSignature = { 0x2D, 0x00, 0x02, 0x1C, 0x00, 0x00, 0x00, 0x0A };
        private static readonly byte[] EofPrefix = { 0x03, 0x00, 0x00, 0x00, 0x14, 0x00, 0x00, 0x00, 0x2D, 0x00, 0x02, 0x1C, 0x2F, 0xEA, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x2D, 0x00, 0x02, 0x1C };
        private static readonly byte[] EofSuffix = { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x2D, 0x00, 0x02, 0x1C };
        private const int LenEofVariablePart = 8;

        private readonly byte[] _data;
        private readonly string _txdFilePath;
        private readonly int _lenEofSignature;

        internal SegmentScanner(byte[] data, string txdFilePath) {
            _data = data;
            _txdFilePath = txdFilePath;
            _lenEofSignature = EofPrefix.Length + LenEofVariablePart + EofSuffix.Length;
        }

        internal (List<Segment> segments, int totalTextures) CollectSegments() {
            byte[] data = _data;
            List<Segment> segments = [];

            List<int> eofOccurrences = [];
            int searchIdx = 0;
            while (true) {
                int pos = FindEofPattern(searchIdx);
                if (pos != -1) {
                    eofOccurrences.Add(pos);
                    searchIdx = pos + 1;
                } else {
                    break;
                }
            }

            int totalEofPatterns = eofOccurrences.Count;
            Log.Blue($"  Found {totalEofPatterns} occurrences of EOF_SIGNATURE pattern in the entire file.");
            if (totalEofPatterns != 1) {
                if (totalEofPatterns == 0) {
                    throw new TxdExportException("  ERROR: EOF pattern not found in the file. This may indicate a corrupted or incomplete TXD file.");
                }

                throw new TxdExportException($"  ERROR: Expected 1 EOF pattern, found {totalEofPatterns}. This may indicate a corrupted or incomplete TXD file.");
            }

            int totalSigFileStart = CountOccurrences(data, SigFileStart);
            Log.Blue($"  Found {totalSigFileStart} occurrences of sig_file_start in the entire file.");

            int totalSigBlockStart = CountOccurrences(data, SigBlockStart);
            Log.Blue($"  Found {totalSigBlockStart} occurrences of sig_block_start in the entire file.");

            int totalSigCompoundEndMarker = totalSigBlockStart;
            Log.Blue($"  Found {totalSigCompoundEndMarker} occurrences of sig_compound_end_marker in the entire file.");

            int totalTextureNameSignature = CountOccurrences(data, TextureNameSignature);
            Log.Blue($"  Found {totalTextureNameSignature} occurrences of texture_name_signature in the entire file.");

            int totalTextures = 0;
            if (totalSigBlockStart == totalSigCompoundEndMarker && totalSigBlockStart == totalTextureNameSignature) {
                totalTextures = totalTextureNameSignature;
            }

            int searchPtr;
            if (StartsWith(data, 0, SigFileStart)) {
                Log.Cyan("  File starts with sig_file_start (0x16). Processing initial segment.");
                int startAfter16 = SigFileStart.Length;
                int posMarker = IndexOf(data, SigBlockStart, startAfter16);
                if (posMarker != -1) {
                    if (IsEofPatternAt(posMarker)) {
                        Log.Cyan($"      0x16 segment data (offset 0x{startAfter16:X}) ends before EOF_SIGNATURE pattern found at 0x{posMarker:X}.");
                        byte[] segmentData = data.AsSpan(startAfter16, posMarker - startAfter16).ToArray();
                        if (segmentData.Length > 0) {
                            segments.Add(new Segment(startAfter16, segmentData));
                        }

                        searchPtr = data.Length;
                    } else {
                        Log.Cyan($"      0x16 segment data (offset 0x{startAfter16:X}) ends before sig_compound_end_marker at 0x{posMarker:X}.");
                        byte[] segmentData = data.AsSpan(startAfter16, posMarker - startAfter16).ToArray();
                        if (segmentData.Length > 0) {
                            segments.Add(new Segment(startAfter16, segmentData));
                        }

                        searchPtr = posMarker;
                    }
                } else {
                    int posEof = FindEofPattern(startAfter16);
                    if (posEof != -1) {
                        Log.Cyan($"      0x16 segment data (offset 0x{startAfter16:X}) ends before EOF_SIGNATURE pattern (direct find) at 0x{posEof:X}.");
                        byte[] segmentData = data.AsSpan(startAfter16, posEof - startAfter16).ToArray();
                        if (segmentData.Length > 0) {
                            segments.Add(new Segment(startAfter16, segmentData));
                        }

                        searchPtr = data.Length;
                    } else {
                        Log.Yellow("      Warning: No sig_compound_end_marker or EOF_SIGNATURE pattern found after 0x16 segment start. Assuming 0x16 data to end of file.");
                        byte[] segmentData = data.AsSpan(startAfter16).ToArray();
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

            int currentScanPos = searchPtr;
            while (currentScanPos < data.Length) {
                int foundBlockStart = IndexOf(data, SigBlockStart, currentScanPos);
                if (foundBlockStart == -1) {
                    Log.Blue($"  No more sig_block_start (or EOF pattern prefix) found after offset 0x{currentScanPos:X}. Ending 0x14 block scan.");
                    break;
                }

                if (IsEofPatternAt(foundBlockStart)) {
                    Log.Cyan($"  Encountered EOF_SIGNATURE pattern at 0x{foundBlockStart:X} while searching for a 0x14 block start. Ending block scan.");
                    break;
                }

                Log.Cyan($"  Found 0x14 block start signature at file offset 0x{foundBlockStart:X}.");
                int startAfter14 = foundBlockStart + SigBlockStart.Length;
                int posNextMarker = IndexOf(data, SigBlockStart, startAfter14);
                if (posNextMarker != -1) {
                    if (IsEofPatternAt(posNextMarker)) {
                        Log.Cyan($"      0x14 block (data from 0x{startAfter14:X}) ends before EOF_SIGNATURE pattern (found as next marker) at 0x{posNextMarker:X}.");
                        byte[] segmentData = data.AsSpan(startAfter14, posNextMarker - startAfter14).ToArray();
                        if (segmentData.Length > 0) {
                            segments.Add(new Segment(startAfter14, segmentData));
                        }

                        currentScanPos = data.Length;
                    } else {
                        Log.Cyan($"      0x14 block (data from 0x{startAfter14:X}) ends before next sig_compound_end_marker at 0x{posNextMarker:X}.");
                        byte[] segmentData = data.AsSpan(startAfter14, posNextMarker - startAfter14).ToArray();
                        if (segmentData.Length > 0) {
                            segments.Add(new Segment(startAfter14, segmentData));
                        }

                        currentScanPos = posNextMarker;
                    }
                } else {
                    int posEof = FindEofPattern(startAfter14);
                    if (posEof != -1) {
                        Log.Cyan($"      0x14 block (data from 0x{startAfter14:X}) ends before EOF_SIGNATURE pattern (direct find) at 0x{posEof:X}.");
                        byte[] segmentData = data.AsSpan(startAfter14, posEof - startAfter14).ToArray();
                        if (segmentData.Length > 0) {
                            segments.Add(new Segment(startAfter14, segmentData));
                        }

                        currentScanPos = data.Length;
                    } else {
                        Log.Yellow($"      Warning: For 0x14 block (data from 0x{startAfter14:X}), no subsequent marker or EOF pattern found. Assuming data to end of file.");
                        byte[] segmentData = data.AsSpan(startAfter14).ToArray();
                        if (segmentData.Length > 0) {
                            segments.Add(new Segment(startAfter14, segmentData));
                        }

                        currentScanPos = data.Length;
                    }
                }
            }

            if (segments.Count == 0 && StartsWith(data, 0, SigFileStart) && data.Length > 0x28) {
                Log.Yellow("  No segments found by primary rules, but file starts with 0x16. Defaulting to process from offset 0x28 (Noesis-style).");
                int eofFallback = FindEofPattern(0x28);
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
        private int FindEofPattern(int searchStartOffset) {
            byte[] data = _data;
            while (searchStartOffset <= data.Length - _lenEofSignature) {
                int prefixPos = IndexOf(data, EofPrefix, searchStartOffset);
                if (prefixPos == -1) {
                    return -1;
                }

                if (prefixPos + _lenEofSignature > data.Length) {
                    return -1;
                }

                int expectedSuffixPos = prefixPos + EofPrefix.Length + LenEofVariablePart;
                if (StartsWith(data, expectedSuffixPos, EofSuffix)) {
                    return prefixPos;
                }

                searchStartOffset = prefixPos + 1;
            }
            return -1;
        }

        private bool IsEofPatternAt(int position) {
            if (position < 0 || position + _lenEofSignature > _data.Length) {
                return false;
            }

            if (!StartsWith(_data, position, EofPrefix)) {
                return false;
            }

            int expectedSuffixStart = position + EofPrefix.Length + LenEofVariablePart;
            return StartsWith(_data, expectedSuffixStart, EofSuffix);
        }
    }

    private sealed class TextureSegmentProcessor {
        private readonly TextureFormatConverter _converter = new();
        private static readonly byte[] NameSignature = { 0x2D, 0x00, 0x02, 0x1C, 0x00, 0x00, 0x00, 0x0A };
        private static readonly HashSet<byte> KnownFormatCodes = [0x52, 0x53, 0x54, 0x86, 0x02];
        private static readonly int NameSignatureLength = NameSignature.Length;

        internal int ProcessSegment(Segment segment, string outputDir) {
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
                        } catch {
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
                        ConversionResult conversion = _converter.Convert(fmtCode, width, height, mipMapCountFromFile, swizzledBaseMipData, actualMipDataSize, segmentOriginalStartOffset, currentName);

                        if (conversion.Header == null || conversion.Pixels == null) {
                            string reason = conversion.NeedsUnswizzle && conversion.Pixels == null
                                ? $"failed to unswizzle data (format 0x{fmtCode:02X}, {conversion.BytesPerPixel}bpp)"
                                : "pixel data processing failed";
                            throw new TxdExportException($"          FATAL ERROR: Failed to generate exportable DDS data for known format 0x{fmtCode:02X} for texture '{currentName.Name}' (File 0x{currentName.OriginalFileOffset:X}). Reason: {reason}.");
                        }

                        string cleanName = SanitizeFilename(currentName.Name) ?? $"texture_at_0x{currentName.OriginalFileOffset:08X}";
                        string ddsFile = System.IO.Path.Combine(outputDir, cleanName + ".dds");
                        try {
                            using System.IO.FileStream fs = System.IO.File.Create(ddsFile);
                            fs.Write(conversion.Header, 0, conversion.Header.Length);
                            fs.Write(conversion.Pixels, 0, conversion.Pixels.Length);
                        } catch (System.IO.IOException ex) {
                            throw new TxdExportException($"          FATAL ERROR: IOError writing DDS file {ddsFile} for '{currentName.Name}': {ex.Message}");
                        }

                        Log.Cyan($"          Successfully exported: {ddsFile} (Format: {conversion.Format}, {width}x{height})");
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

    private sealed class TxdExporter {
        internal (int totalTexturesExported, int filesProcessed, int filesWithExports) ExportPath(string inputPathAbs, string? outputDirBaseArg) {
            int overallTexturesExported = 0;
            int filesProcessedCount = 0;
            int filesWithExports = 0;

            if (!System.IO.File.Exists(inputPathAbs) && !System.IO.Directory.Exists(inputPathAbs)) {
                throw new TxdExportException($"Error: Input path '{inputPathAbs}' does not exist.");
            }

            List<string> txdFilesToProcess = [];
            if (System.IO.File.Exists(inputPathAbs)) {
                if (!inputPathAbs.EndsWith(".txd", System.StringComparison.OrdinalIgnoreCase)) {
                    throw new TxdExportException($"Error: Input file '{inputPathAbs}' is not a .txd file.");
                }

                txdFilesToProcess.Add(inputPathAbs);
            } else {
                Log.Cyan($"Scanning directory: {inputPathAbs}");
                foreach (string file in System.IO.Directory.EnumerateFiles(inputPathAbs, "*.txd", System.IO.SearchOption.AllDirectories)) {
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

            string lastUsedOutputBaseForSummary = string.Empty;
            foreach (string txdFile in txdFilesToProcess) {
                string? currentOutputDirBase = outputDirBaseArg;
                if (string.IsNullOrEmpty(currentOutputDirBase)) {
                    string baseDir = System.IO.Path.GetDirectoryName(txdFile) ?? System.IO.Directory.GetCurrentDirectory();
                    string baseName = System.IO.Path.GetFileNameWithoutExtension(txdFile);
                    currentOutputDirBase = System.IO.Path.Combine(baseDir, baseName + "_txd");
                }

                lastUsedOutputBaseForSummary = currentOutputDirBase!;
                Log.Cyan($"\n--- Processing file: {txdFile} ---");
                int texturesInFile = ExportTexturesFromTxd(txdFile, currentOutputDirBase!);
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
                    if (!string.IsNullOrEmpty(outputDirBaseArg)) {
                        Log.Cyan($"Base output directory specified: '{outputDirBaseArg}' (TXD-specific subfolders created within).");
                    } else {
                        Log.Cyan($"Output subdirectories created relative to each input TXD file's location (e.g., '{System.IO.Path.Combine(lastUsedOutputBaseForSummary, "examplename_txd")}').");
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

    internal int ExportTexturesFromTxd(string txdFilePath, string outputDirBase) {
            Log.Cyan($"Processing TXD file: {txdFilePath}");
            byte[] data;
            try {
                data = System.IO.File.ReadAllBytes(txdFilePath);
            } catch (System.IO.FileNotFoundException) {
                Log.Red($"Error: File not found: {txdFilePath}");
                return 0;
            } catch (System.Exception ex) {
                Log.Red($"Error reading file {txdFilePath}: {ex.Message}");
                return 0;
            }

            if (!System.IO.Directory.Exists(outputDirBase)) {
                try {
                    _ = System.IO.Directory.CreateDirectory(outputDirBase);
                    Log.Cyan($"  Created output directory: {outputDirBase}");
                } catch (System.Exception ex) {
                    throw new TxdExportException($"  Error: Could not create output directory {outputDirBase}: {ex.Message}. Textures from this TXD cannot be saved.");
                }
            }

            SegmentScanner scanner = new(data, txdFilePath);
            (List<Segment> segments, int totalTextures) = scanner.CollectSegments();
            if (segments.Count == 0) {
                return 0;
            }

            int totalTexturesExportedFromFile = 0;
            for (int index = 0; index < segments.Count; index++) {
                Segment segment = segments[index];
                if (segment.Data.Length == 0) {
                    Log.Yellow($"\n  Skipping zero-length segment #{index + 1} (intended to start at file offset 0x{segment.StartOffset:X}).");
                    continue;
                }

                Log.Cyan($"\n  Processing segment #{index + 1}: data starts at file offset 0x{segment.StartOffset:X}, segment length {segment.Data.Length} bytes.");
                int texturesInSegment = new TextureSegmentProcessor().ProcessSegment(segment, outputDirBase);
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
        private static readonly object Sync = new();

        internal static void Cyan(string message) {
            Write(System.ConsoleColor.Cyan, message);
        }

        internal static void Blue(string message) {
            Write(System.ConsoleColor.Blue, message);
        }

        internal static void Green(string message) {
            Write(System.ConsoleColor.Green, message);
        }

        internal static void Yellow(string message) {
            Write(System.ConsoleColor.Yellow, message);
        }

        internal static void Red(string message) {
            Write(System.ConsoleColor.Red, message, true);
        }

        internal static void Gray(string message) {
            Write(System.ConsoleColor.DarkGray, message);
        }

        private static void Write(System.ConsoleColor colour, string message, bool isError = false) {
            lock (Sync) {
                                Core.Diagnostics.Log(message);
            }
            return;
        }
    }

}
