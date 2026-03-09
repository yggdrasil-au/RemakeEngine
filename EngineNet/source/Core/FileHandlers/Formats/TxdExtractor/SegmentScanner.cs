
using System.Collections.Generic;

namespace EngineNet.Core.FileHandlers.Formats;

using System;

public static partial class TxdExtractor {

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

        public SegmentScanner(byte[] data, string txdFilePath) {
            _data = data;
            _txdFilePath = txdFilePath;
            _lenEofSignature = EofPrefix.Length + LenEofVariablePart + EofSuffix.Length;
        }

        public (List<Segment> segments, int totalTextures) CollectSegments() {
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

}
