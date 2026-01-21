
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections;

namespace EngineNet.Core.FileHandlers.TxdExtractor;

internal static partial class Main {

    private static void DebugLog(string message) {
                Core.Diagnostics.Log(message);
    }

    private static List<string> EnumerateTxdFiles(string inputPathAbs) {
        if (!System.IO.File.Exists(inputPathAbs) && !System.IO.Directory.Exists(inputPathAbs)) {
            throw new TxdExportException($"Error: Input path '{inputPathAbs}' does not exist.");
        }

        List<string> txdFilesToProcess = new List<string>();
        if (System.IO.File.Exists(inputPathAbs)) {
            if (!inputPathAbs.EndsWith(".txd", System.StringComparison.OrdinalIgnoreCase)) {
                throw new TxdExportException($"Error: Input file '{inputPathAbs}' is not a .txd file.");
            }
            txdFilesToProcess.Add(inputPathAbs);
        } else {
            foreach (string file in System.IO.Directory.EnumerateFiles(inputPathAbs, "*.txd", System.IO.SearchOption.AllDirectories)) {
                txdFilesToProcess.Add(file);
            }
            if (txdFilesToProcess.Count == 0) {
                throw new TxdExportException($"No .txd files found in directory '{inputPathAbs}'.");
            }
        }

        return txdFilesToProcess;
    }

    private static Options Parse(IList<string> args) {
        if (args is null || args.Count == 0) {
            throw new TxdExportException("Missing input path argument for TXD extraction.");
        }

        Options options = new();
        for (int i = 0; i < args.Count; i++) {
            string current = args[i];
            if (string.IsNullOrWhiteSpace(current)) {
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

                    options.InputPath = string.IsNullOrEmpty(options.InputPath)
                        ? current
                        : throw new TxdExportException($"Unexpected extra positional argument '{current}'.");
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(options.InputPath)) {
            throw new TxdExportException("Missing input path argument for TXD extraction.");
        }

        options.InputPath = System.IO.Path.GetFullPath(options.InputPath);
        if (!string.IsNullOrWhiteSpace(options.OutputDirectory)) {
            options.OutputDirectory = System.IO.Path.GetFullPath(options.OutputDirectory!);
        }

        return options;
    }

    private static int MortonEncode2D(int x, int y) {
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

    private static byte[]? UnswizzleData(System.ReadOnlySpan<byte> swizzledData, int width, int height, int bytesPerPixel) {
        int linearSize = width * height * bytesPerPixel;
        if (swizzledData.IsEmpty || swizzledData.Length < linearSize) {
            Log.Yellow($"      Warning: Swizzled data length ({swizzledData.Length}) is less than expected ({linearSize}) for {width}x{height}@{bytesPerPixel}bpp. Skipping unswizzle.");
            return null;
        }

        byte[] linear = new byte[linearSize];
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                int mortonIdx = MortonEncode2D(x, y);
                int pixelStart = mortonIdx * bytesPerPixel;
                if (pixelStart + bytesPerPixel > swizzledData.Length) {
                    continue;
                }

                int linearStart = ((y * width) + x) * bytesPerPixel;
                swizzledData.Slice(pixelStart, bytesPerPixel).CopyTo(System.MemoryExtensions.AsSpan(linear, linearStart, bytesPerPixel));

            }
        }

        return linear;
    }

    private static string? SanitizeFilename(string name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        System.Text.StringBuilder builder = new(name.Length);
        foreach (char ch in name) {
            _ = ch is < (char)32 or (char)127
                ? builder.Append('_')
                : ch is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' ? builder.Append('_') : builder.Append(ch);
        }

        string cleaned = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static int CalculateDxtLevelSize(int width, int height, string fourcc) {
        if (width <= 0 || height <= 0) {
            return 0;
        }

        int blocksWide = System.Math.Max(1, (width + 3) / 4);
        int blocksHigh = System.Math.Max(1, (height + 3) / 4);
        int bytesPerBlock = fourcc switch {
            "DXT1" => 8,
            "DXT3" => 16,
            "DXT5" => 16,
            _ => 0
        };
        return blocksWide * blocksHigh * bytesPerBlock;
    }

    private static int CountOccurrences(System.ReadOnlySpan<byte> data, System.ReadOnlySpan<byte> pattern) {
        if (pattern.IsEmpty || data.IsEmpty || pattern.Length > data.Length) {
            return 0;
        }
        int count = 0;
        int index = 0;

        while (index <= data.Length - pattern.Length) {
            int found = System.MemoryExtensions.IndexOf(data.Slice(index), pattern);
            if (found == -1)
                break;

            count += 1;
            index += found + 1; // advance at least 1 to allow overlapping matches
        }

        return count;
    }

    private static int IndexOf(System.ReadOnlySpan<byte> data, System.ReadOnlySpan<byte> pattern, int start) {
        if (pattern.IsEmpty)
            return start <= data.Length ? start : -1;

        if (start < 0 || start > data.Length - pattern.Length)
            return -1;

        int pos = System.MemoryExtensions.IndexOf(data.Slice(start), pattern); // fully qualified
        return pos == -1 ? -1 : start + pos;
    }

    private static bool StartsWith(System.ReadOnlySpan<byte> data, int offset, System.ReadOnlySpan<byte> pattern) {
        return pattern.IsEmpty || (offset >= 0 && offset + pattern.Length <= data.Length && System.MemoryExtensions.SequenceEqual(data.Slice(offset, pattern.Length), pattern));
    }

}
