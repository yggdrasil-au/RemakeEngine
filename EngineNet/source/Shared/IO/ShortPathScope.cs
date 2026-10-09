namespace EngineNet.Shared.IO;

/// <summary>
/// Creates short-lived directory aliases under the configured engine root for processes that cannot consume long paths.
/// </summary>
public sealed class ShortPathScope : IDisposable {
    private readonly Dictionary<string, string> _directoryAliases = new(comparer: OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly string _scopeDirectory;
    private int _nextAlias;
    private bool _disposed;

    /// <summary>
    /// Gets the engine-rooted scratch directory owned by this scope.
    /// </summary>
    public string ScratchDirectory => _scopeDirectory;

    /// <summary>
    /// Creates a short-path scope under &lt;engine-root&gt;/TMP/LongPaths.
    /// </summary>
    /// <param name="engineRootPath">The engine root, or the configured Shared.State root when omitted.</param>
    public ShortPathScope(string? engineRootPath = null) {
        string rootPath = string.IsNullOrWhiteSpace(engineRootPath) ? State.RootPath : engineRootPath;
        if (string.IsNullOrWhiteSpace(rootPath)) {
            throw new InvalidOperationException("Cannot create an engine scratch path because the engine root is not configured.");
        }

        string scratchRoot = Path.Combine(path1: Path.GetFullPath(path: rootPath), path2: "TMP", path3: "LongPaths");
        _scopeDirectory = Path.Combine(path1: scratchRoot, path2: Guid.NewGuid().ToString(format: "N"));

        if (OperatingSystem.IsWindows() && _scopeDirectory.Length > 180) {
            throw new PathTooLongException($"The engine-rooted scratch directory is too long for a legacy process: '{_scopeDirectory}'. Move the engine root closer to the drive root.");
        }

        LongPathIO.CreateDirectory(path: _scopeDirectory);
    }

    /// <summary>
    /// Gets or creates a short alias for a directory.
    /// </summary>
    /// <param name="directoryPath">The existing directory to alias.</param>
    /// <returns>The alias path on Windows, or the absolute directory path on other platforms.</returns>
    public string GetDirectoryPath(string directoryPath) {
        ThrowIfDisposed();
        string fullPath = Path.GetFullPath(path: directoryPath);

        if (!OperatingSystem.IsWindows()) {
            return fullPath;
        }

        if (_directoryAliases.TryGetValue(key: fullPath, value: out string? existingAlias)) {
            return existingAlias;
        }

        if (!LongPathIO.DirectoryExists(path: fullPath)) {
            throw new DirectoryNotFoundException($"Cannot create a short-path alias for missing directory '{fullPath}'.");
        }

        string aliasPath = Path.Combine(path1: _scopeDirectory, path2: $"d{_nextAlias++}");
        try {
            Directory.CreateSymbolicLink(path: aliasPath, pathToTarget: LongPathIO.GetAbsolutePath(path: fullPath));
        } catch (IOException ex) {
            throw new InvalidOperationException(
                $"Could not create a short-path alias for '{fullPath}'. Enable Windows Developer Mode or grant symbolic-link privileges. Scratch path: '{_scopeDirectory}'.",
                innerException: ex
            );
        } catch (UnauthorizedAccessException ex) {
            throw new InvalidOperationException(
                $"Access was denied while creating a short-path alias for '{fullPath}'. Enable Windows Developer Mode or grant symbolic-link privileges. Scratch path: '{_scopeDirectory}'.",
                innerException: ex
            );
        } catch (PlatformNotSupportedException ex) {
            throw new InvalidOperationException($"Short-path aliases are not supported for '{fullPath}'.", innerException: ex);
        }

        _directoryAliases[key: fullPath] = aliasPath;
        return aliasPath;
    }

    /// <summary>
    /// Gets a short path for an existing or future file by aliasing its parent directory.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>The file path through a directory alias on Windows, or an absolute path on other platforms.</returns>
    public string GetFilePath(string filePath) {
        ThrowIfDisposed();
        string fullPath = Path.GetFullPath(path: filePath);
        string? parentPath = Path.GetDirectoryName(path: fullPath);
        if (string.IsNullOrWhiteSpace(parentPath)) {
            return EnsureProcessPathLength(path: LongPathIO.GetAbsolutePath(path: fullPath));
        }

        if (string.Equals(a: parentPath, b: _scopeDirectory, comparisonType: OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) {
            return EnsureProcessPathLength(path: LongPathIO.GetAbsolutePath(path: fullPath));
        }

        string aliasPath = Path.Combine(path1: GetDirectoryPath(directoryPath: parentPath), path2: Path.GetFileName(path: fullPath));
        return EnsureProcessPathLength(path: aliasPath);
    }

    /// <summary>
    /// Removes this scope's aliases and scratch directory without deleting their targets.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        foreach (string aliasPath in _directoryAliases.Values) {
            try {
                Directory.Delete(path: aliasPath);
            } catch (IOException ex) {
                Shared.IO.Diagnostics.Bug($"Failed to remove short-path alias '{aliasPath}': {ex}");
            } catch (UnauthorizedAccessException ex) {
                Shared.IO.Diagnostics.Bug($"Access denied while removing short-path alias '{aliasPath}': {ex}");
            }
        }

        try {
            if (Directory.Exists(path: _scopeDirectory)) {
                Directory.Delete(path: _scopeDirectory, recursive: true);
            }
        } catch (IOException ex) {
            Shared.IO.Diagnostics.Bug($"Failed to remove short-path scratch directory '{_scopeDirectory}': {ex}");
        } catch (UnauthorizedAccessException ex) {
            Shared.IO.Diagnostics.Bug($"Access denied while removing short-path scratch directory '{_scopeDirectory}': {ex}");
        }
    }

    private void ThrowIfDisposed() {
        ObjectDisposedException.ThrowIf(condition: _disposed, instance: this);
    }

    private static string EnsureProcessPathLength(string path) {
        if (OperatingSystem.IsWindows() && path.Length > 259) {
            throw new PathTooLongException($"The aliased path still exceeds the legacy process path limit: '{path}'. Shorten the final file name or use a shallower engine root.");
        }

        return path;
    }
}