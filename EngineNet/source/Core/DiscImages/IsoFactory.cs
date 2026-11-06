namespace EngineNet.Core.DiscImages;

/// <summary>
/// Factory and CLI runner for probing, inspecting, and extracting console optical disc images (.iso).
/// Supports Xbox Original (XISO, XGD1), Xbox 360 (XGD2, XGD3), PlayStation 3 (ISO9660/Joliet/UDF hybrid),
/// PlayStation 2 (ISO9660), and standard ECMA-119 ISO9660 disc images.
/// </summary>
public static class IsoFactory {
    /* :: :: Constants :: START :: */

    private static readonly byte[] XdvdfsMagic = "MICROSOFT*XBOX*MEDIA"u8.ToArray();
    private static readonly byte[] Iso9660Magic = "CD001"u8.ToArray();
    private static readonly byte[] UdfNsr02Magic = "NSR02"u8.ToArray();
    private static readonly byte[] UdfNsr03Magic = "NSR03"u8.ToArray();

    /* :: :: Constants :: END :: */
    // //
    /* :: :: Methods :: START :: */

    /// <summary>
    /// Opens an optical disc image file at <paramref name="filePath"/>, automatically detecting its console partition and filesystem format.
    /// </summary>
    /// <param name="filePath">Path to the .iso file on disk.</param>
    /// <returns>An initialized <see cref="IIsoReader"/> instance.</returns>
    public static IIsoReader Open(string filePath) {
        if (string.IsNullOrWhiteSpace(filePath)) {
            throw new System.ArgumentException("Disc image path cannot be empty.", paramName: nameof(filePath));
        }

        string fullPath = System.IO.Path.GetFullPath(path: filePath);
        if (!Shared.IO.LongPathIO.FileExists(path: fullPath)) {
            throw new System.IO.FileNotFoundException($"Disc image file not found: '{fullPath}'.", fileName: fullPath);
        }

        System.IO.FileStream fs = Shared.IO.LongPathIO.OpenFile(
            path: fullPath,
            mode: System.IO.FileMode.Open,
            access: System.IO.FileAccess.Read,
            share: System.IO.FileShare.Read
        );

        try {
            return OpenStream(stream: fs, leaveOpen: false);
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[IsoFactory.cs::Open()]:: catch: {ex.Message}");
            fs.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens an optical disc image from an existing seekable <see cref="System.IO.Stream"/>.
    /// </summary>
    /// <param name="stream">Seekable stream containing the disc image.</param>
    /// <param name="leaveOpen">Whether to keep <paramref name="stream"/> open when the returned reader is disposed.</param>
    /// <returns>An initialized <see cref="IIsoReader"/> instance.</returns>
    public static IIsoReader OpenStream(System.IO.Stream stream, bool leaveOpen = false) {
        if (stream is null) {
            throw new System.ArgumentNullException(paramName: nameof(stream));
        }
        if (!stream.CanRead || !stream.CanSeek) {
            throw new System.ArgumentException("Disc image stream must be readable and seekable.", paramName: nameof(stream));
        }

        // 1. Probe Xbox 360 XGD3 (Partition Base 0x02080000 + Header 0x10000 = 0x02090000)
        if (CheckMagic(stream: stream, offset: XdvdfsReader.Xgd3PartitionOffset + XdvdfsReader.VolumeDescriptorOffset, magic: XdvdfsMagic)) {
            return new XdvdfsReader(stream: stream, partitionOffset: XdvdfsReader.Xgd3PartitionOffset, formatName: "XGD3", leaveOpen: leaveOpen);
        }

        // 2. Probe Xbox 360 XGD2 (Partition Base 0x0FD90000 + Header 0x10000 = 0x0FDA0000)
        if (CheckMagic(stream: stream, offset: XdvdfsReader.Xgd2PartitionOffset + XdvdfsReader.VolumeDescriptorOffset, magic: XdvdfsMagic)) {
            return new XdvdfsReader(stream: stream, partitionOffset: XdvdfsReader.Xgd2PartitionOffset, formatName: "XGD2", leaveOpen: leaveOpen);
        }

        // 3. Probe Xbox Original XGD1 (Partition Base 0x18300000 + Header 0x10000 = 0x18310000)
        if (CheckMagic(stream: stream, offset: XdvdfsReader.Xgd1PartitionOffset + XdvdfsReader.VolumeDescriptorOffset, magic: XdvdfsMagic)) {
            return new XdvdfsReader(stream: stream, partitionOffset: XdvdfsReader.Xgd1PartitionOffset, formatName: "XGD1", leaveOpen: leaveOpen);
        }

        // 4. Probe Trimmed XISO (Partition Base 0x00000000 + Header 0x10000 = 0x00010000)
        if (CheckMagic(stream: stream, offset: XdvdfsReader.VolumeDescriptorOffset, magic: XdvdfsMagic)) {
            return new XdvdfsReader(stream: stream, partitionOffset: 0L, formatName: "XISO", leaveOpen: leaveOpen);
        }

        // 5. Probe ISO9660 / PS3 / PS2 at Sector 16 (0x8000)
        if (CheckMagic(stream: stream, offset: 0x8001L, magic: Iso9660Magic)) {
            System.Span<byte> sysIdBuf = stackalloc byte[32];
            System.Span<byte> volIdBuf = stackalloc byte[32];

            stream.Seek(offset: 0x8008L, origin: System.IO.SeekOrigin.Begin);
            stream.ReadExactly(buffer: sysIdBuf);
            string sysId = System.Text.Encoding.ASCII.GetString(bytes: sysIdBuf).TrimEnd(' ', '\0');

            stream.Seek(offset: 0x8028L, origin: System.IO.SeekOrigin.Begin);
            stream.ReadExactly(buffer: volIdBuf);
            string volId = System.Text.Encoding.ASCII.GetString(bytes: volIdBuf).TrimEnd(' ', '\0');

            bool hasUdfBridge = CheckMagic(stream: stream, offset: 0xA001L, magic: UdfNsr03Magic)
                || CheckMagic(stream: stream, offset: 0xA001L, magic: UdfNsr02Magic)
                || CheckMagic(stream: stream, offset: 0x9801L, magic: UdfNsr03Magic);

            if (volId.StartsWith("PS3VOLUME", comparisonType: System.StringComparison.OrdinalIgnoreCase)
                || sysId.StartsWith("PLAYSTATION", comparisonType: System.StringComparison.OrdinalIgnoreCase) && hasUdfBridge) {
                return new Iso9660Reader(stream: stream, formatName: "PS3_HYBRID", volumeLabel: volId, leaveOpen: leaveOpen);
            }

            if (sysId.StartsWith("PLAYSTATION", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                return new Iso9660Reader(stream: stream, formatName: "PS2_ISO9660", volumeLabel: volId, leaveOpen: leaveOpen);
            }

            string initialFormat = hasUdfBridge ? "PS3_HYBRID" : "STANDARD_ISO9660";
            return new Iso9660Reader(stream: stream, formatName: initialFormat, volumeLabel: volId, leaveOpen: leaveOpen);
        }

        throw new System.IO.InvalidDataException("Unrecognized optical disc format or encrypted BD-ROM image (no XDVDFS or ISO9660 volume descriptors found).");
    }

    /// <summary>
    /// Fast non-throwing probe to check whether a file is a supported optical disc image and return its detected format name.
    /// </summary>
    /// <param name="filePath">Path to the file to probe.</param>
    /// <param name="detectedFormat">Detected format string if true; otherwise null.</param>
    /// <returns>True if the file contains a recognized optical disc signature.</returns>
    public static bool TryProbeFormat(string filePath, out string? detectedFormat) {
        detectedFormat = null;
        if (string.IsNullOrWhiteSpace(filePath)) {
            return false;
        }

        try {
            string fullPath = System.IO.Path.GetFullPath(path: filePath);
            if (!Shared.IO.LongPathIO.FileExists(path: fullPath)) {
                return false;
            }

            using System.IO.FileStream fs = Shared.IO.LongPathIO.OpenFile(
                path: fullPath,
                mode: System.IO.FileMode.Open,
                access: System.IO.FileAccess.Read,
                share: System.IO.FileShare.Read
            );

            if (CheckMagic(stream: fs, offset: XdvdfsReader.Xgd3PartitionOffset + XdvdfsReader.VolumeDescriptorOffset, magic: XdvdfsMagic)) {
                detectedFormat = "XGD3";
                return true;
            }
            if (CheckMagic(stream: fs, offset: XdvdfsReader.Xgd2PartitionOffset + XdvdfsReader.VolumeDescriptorOffset, magic: XdvdfsMagic)) {
                detectedFormat = "XGD2";
                return true;
            }
            if (CheckMagic(stream: fs, offset: XdvdfsReader.Xgd1PartitionOffset + XdvdfsReader.VolumeDescriptorOffset, magic: XdvdfsMagic)) {
                detectedFormat = "XGD1";
                return true;
            }
            if (CheckMagic(stream: fs, offset: XdvdfsReader.VolumeDescriptorOffset, magic: XdvdfsMagic)) {
                detectedFormat = "XISO";
                return true;
            }
            if (CheckMagic(stream: fs, offset: 0x8001L, magic: Iso9660Magic)) {
                System.Span<byte> sysIdBuf = stackalloc byte[32];
                System.Span<byte> volIdBuf = stackalloc byte[32];
                fs.Seek(offset: 0x8008L, origin: System.IO.SeekOrigin.Begin);
                fs.ReadExactly(buffer: sysIdBuf);
                string sysId = System.Text.Encoding.ASCII.GetString(bytes: sysIdBuf).TrimEnd(' ', '\0');

                fs.Seek(offset: 0x8028L, origin: System.IO.SeekOrigin.Begin);
                fs.ReadExactly(buffer: volIdBuf);
                string volId = System.Text.Encoding.ASCII.GetString(bytes: volIdBuf).TrimEnd(' ', '\0');

                bool hasUdfBridge = CheckMagic(stream: fs, offset: 0xA001L, magic: UdfNsr03Magic)
                    || CheckMagic(stream: fs, offset: 0xA001L, magic: UdfNsr02Magic);

                if (volId.StartsWith("PS3VOLUME", comparisonType: System.StringComparison.OrdinalIgnoreCase) || hasUdfBridge) {
                    detectedFormat = "PS3_HYBRID";
                } else if (sysId.StartsWith("PLAYSTATION", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                    detectedFormat = "PS2_ISO9660";
                } else {
                    detectedFormat = "STANDARD_ISO9660";
                }
                return true;
            }

            return false;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[IsoFactory.cs::TryProbeFormat()]:: catch: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Executes CLI / built-in engine operation for ISO inspection, listing, or extraction.
    /// Supported arguments:
    /// --input / -i / --source &lt;iso-or-dir&gt;
    /// --output / -o / --out &lt;dest-dir&gt;
    /// --filter / --pattern &lt;glob&gt;
    /// --overwrite [true|false]
    /// --list / --inspect
    /// </summary>
    public static bool RunCli(System.Collections.Generic.IReadOnlyList<string> args, System.Threading.CancellationToken cancellationToken = default) {
        try {
            if (args.Count == 0) {
                Shared.IO.UI.EngineSdk.PrintLine("Usage: format-extract (format=iso) --input <iso-path> [--output <dest-dir>] [--filter <glob>] [--overwrite] [--list|--inspect]");
                return false;
            }

            string? inputPath = null;
            string? outputDir = null;
            string? filter = null;
            bool overwrite = false;
            bool listOnly = false;

            for (int i = 0; i < args.Count; i++) {
                string arg = args[index: i];
                if (string.IsNullOrWhiteSpace(arg)) {
                    continue;
                }

                switch (arg.ToLowerInvariant()) {
                    case "-i":
                    case "--in":
                    case "--input":
                    case "--source":
                        if (i + 1 < args.Count) {
                            inputPath = args[index: ++i];
                        }
                        break;
                    case "-o":
                    case "--out":
                    case "--output":
                    case "--output_dir":
                    case "--destination":
                        if (i + 1 < args.Count) {
                            outputDir = args[index: ++i];
                        }
                        break;
                    case "-f":
                    case "--filter":
                    case "--pattern":
                        if (i + 1 < args.Count) {
                            filter = args[index: ++i];
                        }
                        break;
                    case "--overwrite":
                        if (i + 1 < args.Count && bool.TryParse(value: args[index: i + 1], result: out bool parsedOverwrite)) {
                            overwrite = parsedOverwrite;
                            i++;
                        } else {
                            overwrite = true;
                        }
                        break;
                    case "--list":
                    case "--inspect":
                        listOnly = true;
                        break;
                    default:
                        if (!arg.StartsWith("-", comparisonType: System.StringComparison.Ordinal)) {
                            if (string.IsNullOrEmpty(inputPath)) {
                                inputPath = arg;
                            } else if (string.IsNullOrEmpty(outputDir)) {
                                outputDir = arg;
                            }
                        }
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(inputPath)) {
                IO.Error("ISO operation requires an --input <path> argument.");
                return false;
            }

            string fullInput = System.IO.Path.GetFullPath(path: inputPath);
            System.Collections.Generic.List<string> isoFiles = new();
            if (Shared.IO.LongPathIO.FileExists(path: fullInput)) {
                isoFiles.Add(item: fullInput);
            } else if (Shared.IO.LongPathIO.DirectoryExists(path: fullInput)) {
                foreach (string file in Shared.IO.LongPathIO.EnumerateFiles(path: fullInput, searchPattern: "*.iso", searchOption: System.IO.SearchOption.AllDirectories)) {
                    isoFiles.Add(item: file);
                }
            } else {
                IO.Error($"ISO input path does not exist: '{fullInput}'.");
                return false;
            }

            if (isoFiles.Count == 0) {
                IO.Error($"No .iso disc images found at '{fullInput}'.");
                return false;
            }

            bool allOk = true;
            foreach (string isoFile in isoFiles) {
                cancellationToken.ThrowIfCancellationRequested();
                using IIsoReader reader = Open(filePath: isoFile);
                System.Collections.Generic.IReadOnlyList<IsoEntry> entries = reader.ListEntries(subPath: "/", recursive: true);

                IO.writeLine($"[ISO] Opened '{System.IO.Path.GetFileName(path: isoFile)}' | Format={reader.Format} | Volume='{reader.VolumeLabel}' | PartitionOffset=0x{reader.PartitionOffset:X} | Entries={entries.Count}", color: System.ConsoleColor.Cyan);

                if (listOnly || string.IsNullOrWhiteSpace(outputDir)) {
                    foreach (IsoEntry entry in entries) {
                        if (!IsoPathUtilities.MatchesFilter(entry: entry, filterPattern: filter)) {
                            continue;
                        }
                        string kind = entry.IsDirectory ? "DIR " : "FILE";
                        IO.writeLine($"  [{kind}] {entry.Path} ({entry.Size} bytes @ sector {entry.StartSector})");
                    }
                    continue;
                }

                string targetOut = isoFiles.Count > 1
                    ? System.IO.Path.Combine(path1: outputDir, path2: System.IO.Path.GetFileNameWithoutExtension(path: isoFile))
                    : outputDir;

                using Shared.IO.UI.EngineSdk.PanelProgress panel = new(total: System.Math.Max(val1: 1, val2: entries.Count), id: "iso-extract", label: $"Extracting {System.IO.Path.GetFileName(path: isoFile)}");
                int lastReported = 0;
                System.Progress<IsoProgressInfo> progressReporter = new(handler: info => {
                    int delta = info.ProcessedFiles - lastReported;
                    if (delta > 0) {
                        lastReported = info.ProcessedFiles;
                        panel.SetTotal(total: System.Math.Max(val1: 1, val2: info.TotalFiles));
                        panel.SetLabel(label: $"Extracting {info.CurrentFile}");
                        panel.Update(inc: delta);
                    }
                });

                IsoExtractResult result = reader.ExtractAll(
                    options: new IsoExtractOptions {
                        Destination = targetOut,
                        Filter = filter,
                        Overwrite = overwrite
                    },
                    progress: progressReporter,
                    ct: cancellationToken
                );
                panel.Complete();

                if (!result.Success) {
                    IO.Error($"[ISO] Failed to extract '{isoFile}': {result.Error}");
                    allOk = false;
                } else {
                    IO.writeLine($"[ISO] Extracted {result.FilesExtracted} file(s) ({result.BytesExtracted:N0} bytes, {result.FilesSkipped} skipped) to '{targetOut}'.", color: System.ConsoleColor.Green);
                }
            }

            return allOk;
        } catch (System.OperationCanceledException ex) {
            Shared.IO.Diagnostics.Bug($"[IsoFactory.cs::RunCli()]:: catch: {ex.Message}");
            throw;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[IsoFactory.cs::RunCli()]:: catch: {ex.Message}");
            IO.Error($"[ISO] Error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks whether <paramref name="stream"/> contains the exact byte sequence <paramref name="magic"/> at <paramref name="offset"/>.
    /// </summary>
    private static bool CheckMagic(System.IO.Stream stream, long offset, byte[] magic) {
        if (offset < 0 || stream.Length < offset + magic.Length) {
            return false;
        }

        stream.Seek(offset: offset, origin: System.IO.SeekOrigin.Begin);
        System.Span<byte> buf = stackalloc byte[magic.Length];
        int totalRead = 0;
        while (totalRead < magic.Length) {
            int read = stream.Read(buffer: buf.Slice(start: totalRead));
            if (read <= 0) {
                return false;
            }
            totalRead += read;
        }

        return System.MemoryExtensions.SequenceEqual(span: (System.ReadOnlySpan<byte>)buf, other: magic);
    }

    /* :: :: Methods :: END :: */
}
