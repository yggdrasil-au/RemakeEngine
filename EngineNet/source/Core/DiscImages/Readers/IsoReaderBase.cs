namespace EngineNet.Core.DiscImages;

/// <summary>
/// Base implementation for optical disc image readers providing case-insensitive entry lookup,
/// virtual stream slicing via <see cref="SubStream"/>, in-memory byte reads, and long-path-safe extraction.
/// </summary>
public abstract class IsoReaderBase : IIsoReader {
    /* :: :: Fields :: START :: */

    private const int CopyBufferSize = 131072;

    protected readonly System.IO.Stream BaseStream;
    protected readonly object StreamLock = new();
    protected readonly System.Collections.Generic.List<IsoEntry> AllEntries = new();
    protected readonly System.Collections.Generic.Dictionary<string, IsoEntry> EntryLookup = new(comparer: System.StringComparer.OrdinalIgnoreCase);

    private readonly bool _leaveOpen;
    private bool _disposed;

    /* :: :: Fields :: END :: */
    // //
    /* :: :: Properties :: START :: */

    /// <inheritdoc />
    public string Format { get; protected set; }

    /// <inheritdoc />
    public string VolumeLabel { get; protected set; }

    /// <inheritdoc />
    public long TotalSize { get; }

    /// <inheritdoc />
    public long PartitionOffset { get; protected set; }

    /* :: :: Properties :: END :: */
    // //
    /* :: :: Constructors :: START :: */

    /// <summary>
    /// Initializes a new optical disc image reader over <paramref name="stream"/>.
    /// </summary>
    protected IsoReaderBase(System.IO.Stream stream, long partitionOffset, string format, string volumeLabel, bool leaveOpen = false) {
        if (stream is null) {
            throw new System.ArgumentNullException(paramName: nameof(stream));
        }

        BaseStream = stream;
        TotalSize = stream.CanSeek ? stream.Length : 0;
        PartitionOffset = partitionOffset;
        Format = format;
        VolumeLabel = volumeLabel;
        _leaveOpen = leaveOpen;
    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Methods :: START :: */

    /// <summary>
    /// Registers an entry in the reader's internal directory table and synthesizes missing parent directory entries.
    /// </summary>
    protected void RegisterEntry(IsoEntry entry) {
        string normalized = IsoPathUtilities.NormalizeInternalPath(internalPath: entry.Path);
        if (string.IsNullOrEmpty(normalized)) {
            return;
        }

        // Synthesize any missing parent directory entries if a flat path contains slashes
        int slashIdx = normalized.IndexOf('/', comparisonType: System.StringComparison.Ordinal);
        while (slashIdx > 0) {
            string parentDir = normalized.Substring(startIndex: 0, length: slashIdx);
            if (!EntryLookup.ContainsKey(key: parentDir)) {
                int lastSlash = parentDir.LastIndexOf('/');
                string dirName = lastSlash >= 0 ? parentDir.Substring(startIndex: lastSlash + 1) : parentDir;
                IsoEntry dirEntry = new() {
                    Path = parentDir,
                    Name = dirName,
                    Size = 0,
                    IsDirectory = true,
                    StartSector = 0,
                    AbsoluteOffset = PartitionOffset
                };
                EntryLookup[key: parentDir] = dirEntry;
                AllEntries.Add(item: dirEntry);
            }
            slashIdx = normalized.IndexOf('/', startIndex: slashIdx + 1);
        }

        if (!EntryLookup.ContainsKey(key: normalized)) {
            EntryLookup[key: normalized] = entry;
            AllEntries.Add(item: entry);
        }
    }

    /// <inheritdoc />
    public bool FileExists(string internalPath) {
        EnsureNotDisposed();
        string normalized = IsoPathUtilities.NormalizeInternalPath(internalPath: internalPath);
        if (string.IsNullOrEmpty(normalized)) {
            return true;
        }
        return EntryLookup.ContainsKey(key: normalized);
    }

    /// <inheritdoc />
    public IsoEntry? GetEntry(string internalPath) {
        EnsureNotDisposed();
        string normalized = IsoPathUtilities.NormalizeInternalPath(internalPath: internalPath);
        if (string.IsNullOrEmpty(normalized)) {
            return null;
        }
        return EntryLookup.TryGetValue(key: normalized, out IsoEntry? entry) ? entry : null;
    }

    /// <inheritdoc />
    public System.Collections.Generic.IReadOnlyList<IsoEntry> ListEntries(string subPath = "/", bool recursive = true) {
        EnsureNotDisposed();
        string normalizedScope = IsoPathUtilities.NormalizeInternalPath(internalPath: subPath);
        if (string.IsNullOrEmpty(normalizedScope) && recursive) {
            return AllEntries;
        }

        System.Collections.Generic.List<IsoEntry> filtered = new();
        foreach (IsoEntry entry in AllEntries) {
            if (IsoPathUtilities.IsWithinScope(entryPath: entry.Path, subPath: normalizedScope, recursive: recursive)) {
                filtered.Add(item: entry);
            }
        }
        return filtered;
    }

    /// <inheritdoc />
    public System.IO.Stream OpenFileStream(string internalPath) {
        EnsureNotDisposed();
        IsoEntry? entry = GetEntry(internalPath: internalPath);
        if (entry is null) {
            throw new System.IO.FileNotFoundException($"File '{internalPath}' was not found inside the disc image.", fileName: internalPath);
        }
        if (entry.IsDirectory) {
            throw new System.InvalidOperationException($"Cannot open a byte stream for directory entry '{internalPath}'.");
        }
        if (entry.AbsoluteOffset < 0 || entry.AbsoluteOffset + entry.Size > TotalSize) {
            throw new System.IO.InvalidDataException($"Entry '{internalPath}' (offset=0x{entry.AbsoluteOffset:X}, size={entry.Size}) exceeds disc image bounds ({TotalSize} bytes).");
        }

        return new SubStream(baseStream: BaseStream, baseOffset: entry.AbsoluteOffset, length: entry.Size, syncLock: StreamLock);
    }

    /// <inheritdoc />
    public byte[] ReadFileBytes(string internalPath, int maxBytes = -1) {
        EnsureNotDisposed();
        using System.IO.Stream stream = OpenFileStream(internalPath: internalPath);
        long targetLen = maxBytes >= 0 ? System.Math.Min(val1: stream.Length, val2: maxBytes) : stream.Length;
        if (targetLen > int.MaxValue) {
            throw new System.InvalidOperationException($"File '{internalPath}' ({targetLen} bytes) is too large to read into a single byte array. Use OpenFileStream or ExtractFile instead.");
        }

        byte[] buffer = new byte[(int)targetLen];
        int totalRead = 0;
        while (totalRead < buffer.Length) {
            int read = stream.Read(buffer: buffer, offset: totalRead, count: buffer.Length - totalRead);
            if (read <= 0) {
                break;
            }
            totalRead += read;
        }
        return buffer;
    }

    /// <inheritdoc />
    public void ExtractFile(string internalPath, string destinationPath, bool overwrite = false) {
        EnsureNotDisposed();
        IsoEntry? entry = GetEntry(internalPath: internalPath);
        if (entry is null) {
            throw new System.IO.FileNotFoundException($"File '{internalPath}' was not found inside the disc image.", fileName: internalPath);
        }
        if (entry.IsDirectory) {
            Shared.IO.LongPathIO.CreateDirectory(path: destinationPath);
            return;
        }

        string fullDest = System.IO.Path.GetFullPath(path: destinationPath);
        if (Shared.IO.LongPathIO.FileExists(path: fullDest) && !overwrite) {
            return;
        }

        string? parentDir = System.IO.Path.GetDirectoryName(path: fullDest);
        if (!string.IsNullOrEmpty(parentDir)) {
            Shared.IO.LongPathIO.CreateDirectory(path: parentDir);
        }

        using System.IO.Stream src = OpenFileStream(internalPath: entry.Path);
        using System.IO.FileStream dst = Shared.IO.LongPathIO.OpenFile(
            path: fullDest,
            mode: System.IO.FileMode.Create,
            access: System.IO.FileAccess.Write,
            share: System.IO.FileShare.None
        );

        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(minimumLength: CopyBufferSize);
        try {
            int read;
            while ((read = src.Read(buffer: buffer, offset: 0, count: CopyBufferSize)) > 0) {
                dst.Write(buffer: buffer, offset: 0, count: read);
            }
        } finally {
            System.Buffers.ArrayPool<byte>.Shared.Return(array: buffer);
        }
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task ExtractFileAsync(string internalPath, string destinationPath, bool overwrite = false, System.Threading.CancellationToken ct = default) {
        EnsureNotDisposed();
        ct.ThrowIfCancellationRequested();
        await System.Threading.Tasks.Task.Run(action: () => ExtractFile(internalPath: internalPath, destinationPath: destinationPath, overwrite: overwrite), cancellationToken: ct).ConfigureAwait(continueOnCapturedContext: false);
    }

    /// <inheritdoc />
    public IsoExtractResult ExtractAll(IsoExtractOptions options, System.IProgress<IsoProgressInfo>? progress = null, System.Threading.CancellationToken ct = default) {
        EnsureNotDisposed();
        if (options is null || string.IsNullOrWhiteSpace(options.Destination)) {
            return new IsoExtractResult {
                Success = false,
                Error = "Destination directory is required for ISO extraction."
            };
        }

        try {
            string destRoot = System.IO.Path.GetFullPath(path: options.Destination);
            Shared.IO.LongPathIO.CreateDirectory(path: destRoot);

            System.Collections.Generic.List<IsoEntry> matchingFiles = new();
            foreach (IsoEntry entry in AllEntries) {
                if (!IsoPathUtilities.MatchesFilter(entry: entry, filterPattern: options.Filter)) {
                    continue;
                }
                if (entry.IsDirectory) {
                    string dirOut = ResolveSafeDestinationPath(destRoot: destRoot, internalPath: entry.Path);
                    Shared.IO.LongPathIO.CreateDirectory(path: dirOut);
                } else {
                    matchingFiles.Add(item: entry);
                }
            }

            int extractedCount = 0;
            int skippedCount = 0;
            long bytesExtracted = 0;
            int totalFiles = matchingFiles.Count;

            for (int i = 0; i < matchingFiles.Count; i++) {
                ct.ThrowIfCancellationRequested();
                IsoEntry fileEntry = matchingFiles[index: i];
                string fileOut = ResolveSafeDestinationPath(destRoot: destRoot, internalPath: fileEntry.Path);

                if (Shared.IO.LongPathIO.FileExists(path: fileOut) && !options.Overwrite) {
                    skippedCount++;
                } else {
                    ExtractFile(internalPath: fileEntry.Path, destinationPath: fileOut, overwrite: true);
                    extractedCount++;
                    bytesExtracted += fileEntry.Size;
                }

                progress?.Report(value: new IsoProgressInfo {
                    CurrentFile = fileEntry.Path,
                    ProcessedFiles = i + 1,
                    TotalFiles = totalFiles,
                    BytesExtracted = bytesExtracted
                });
            }

            return new IsoExtractResult {
                Success = true,
                FilesExtracted = extractedCount,
                FilesSkipped = skippedCount,
                BytesExtracted = bytesExtracted
            };
        } catch (System.OperationCanceledException ex) {
            Shared.IO.Diagnostics.Bug($"[IsoReaderBase.cs::ExtractAll()]:: catch: {ex.Message}");
            throw;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[IsoReaderBase.cs::ExtractAll()]:: catch: {ex.Message}");
            return new IsoExtractResult {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<IsoExtractResult> ExtractAllAsync(IsoExtractOptions options, System.IProgress<IsoProgressInfo>? progress = null, System.Threading.CancellationToken ct = default) {
        EnsureNotDisposed();
        return await System.Threading.Tasks.Task.Run(function: () => ExtractAll(options: options, progress: progress, ct: ct), cancellationToken: ct).ConfigureAwait(continueOnCapturedContext: false);
    }

    /// <summary>
    /// Resolves an internal disc entry path against <paramref name="destRoot"/> and verifies no path traversal escapes the destination directory.
    /// </summary>
    private static string ResolveSafeDestinationPath(string destRoot, string internalPath) {
        string normalized = IsoPathUtilities.NormalizeInternalPath(internalPath: internalPath);
        string[] segments = normalized.Split(separator: '/', options: System.StringSplitOptions.RemoveEmptyEntries);
        foreach (string seg in segments) {
            if (seg == "." || seg == "..") {
                throw new System.IO.InvalidDataException($"Unsafe relative path segment in disc entry '{internalPath}'.");
            }
        }

        string relativeOsPath = normalized.Replace(oldChar: '/', newChar: System.IO.Path.DirectorySeparatorChar);
        string fullTarget = System.IO.Path.GetFullPath(path: System.IO.Path.Combine(path1: destRoot, path2: relativeOsPath));
        string relCheck = System.IO.Path.GetRelativePath(relativeTo: destRoot, path: fullTarget);
        if (relCheck.StartsWith("..", comparisonType: System.StringComparison.Ordinal) || System.IO.Path.IsPathRooted(path: relCheck)) {
            throw new System.IO.InvalidDataException($"Disc entry '{internalPath}' attempted to escape destination root '{destRoot}'.");
        }

        return fullTarget;
    }

    /// <summary>
    /// Throws <see cref="System.ObjectDisposedException"/> if this reader has been disposed.
    /// </summary>
    protected void EnsureNotDisposed() {
        if (_disposed) {
            throw new System.ObjectDisposedException(objectName: GetType().Name);
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        if (!_leaveOpen) {
            BaseStream.Dispose();
        }
        System.GC.SuppressFinalize(obj: this);
    }

    /* :: :: Methods :: END :: */
}

