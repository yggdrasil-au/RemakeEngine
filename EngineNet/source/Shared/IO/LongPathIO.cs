namespace EngineNet.Shared.IO;

/// <summary>
/// Provides filesystem operations that normalize Windows long paths before passing them to .NET IO APIs.
/// </summary>
public static class LongPathIO {
    private const int MaxLegacyPathLength = 259;
    private const string ExtendedPathPrefix = @"\\?\";
    private const string ExtendedUncPathPrefix = @"\\?\UNC\";

    /// <summary>
    /// Resolves a path to an absolute path and adds the Windows extended-length prefix when required.
    /// </summary>
    /// <param name="path">The path to resolve.</param>
    /// <param name="basePath">The absolute base directory for a relative path, or the process current directory when omitted.</param>
    /// <returns>The absolute path, prefixed for extended-length Windows IO when required.</returns>
    public static string GetAbsolutePath(string path, string? basePath = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(argument: path);

        if (path.StartsWith(value: ExtendedPathPrefix, comparisonType: StringComparison.OrdinalIgnoreCase)) {
            return path;
        }

        string fullPath = basePath is null
            ? Path.GetFullPath(path: path)
            : Path.GetFullPath(path: path, basePath: basePath);

        return AddExtendedLengthPrefix(path: fullPath, isWindows: OperatingSystem.IsWindows());
    }

    /// <summary>
    /// Converts a path for use by filesystem IO while preserving process-current-directory semantics for relative paths.
    /// </summary>
    /// <param name="path">The path to prepare for IO.</param>
    /// <returns>An absolute path, prefixed for extended-length Windows IO when required.</returns>
    public static string NormalizeForIO(string path) {
        return GetAbsolutePath(path: path);
    }

    /// <summary>
    /// Calculates a relative path after removing extended-length prefixes used only for filesystem IO.
    /// </summary>
    /// <param name="basePath">The base directory.</param>
    /// <param name="targetPath">The target path.</param>
    /// <returns>The target path relative to the base directory, or an absolute path when they are on different roots.</returns>
    public static string GetRelativePath(string basePath, string targetPath) {
        string fullBasePath = RemoveExtendedLengthPrefix(path: GetAbsolutePath(path: basePath));
        string fullTargetPath = RemoveExtendedLengthPrefix(path: GetAbsolutePath(path: targetPath));
        return Path.GetRelativePath(relativeTo: fullBasePath, path: fullTargetPath);
    }

    /// <summary>
    /// Checks whether a file exists using a normalized path.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>True when the file exists.</returns>
    public static bool FileExists(string path) {
        return File.Exists(path: NormalizeForIO(path: path));
    }

    /// <summary>
    /// Checks whether a directory exists using a normalized path.
    /// </summary>
    /// <param name="path">The directory path.</param>
    /// <returns>True when the directory exists.</returns>
    public static bool DirectoryExists(string path) {
        return Directory.Exists(path: NormalizeForIO(path: path));
    }

    /// <summary>
    /// Checks whether a file or directory exists using a normalized path.
    /// </summary>
    /// <param name="path">The file or directory path.</param>
    /// <returns>True when the path exists.</returns>
    public static bool PathExists(string path) {
        return FileExists(path: path) || DirectoryExists(path: path);
    }

    /// <summary>
    /// Enumerates files under a directory using a normalized path.
    /// </summary>
    /// <param name="path">The directory path.</param>
    /// <param name="searchPattern">The file-name pattern to match.</param>
    /// <param name="searchOption">Whether to search only the directory or all subdirectories.</param>
    /// <returns>An enumerable of matching file paths.</returns>
    public static IEnumerable<string> EnumerateFiles(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly
    ) {
        return Directory.EnumerateFiles(path: NormalizeForIO(path: path), searchPattern: searchPattern, searchOption: searchOption);
    }

    /// <summary>
    /// Enumerates directories under a path using a normalized path.
    /// </summary>
    /// <param name="path">The parent directory path.</param>
    /// <param name="searchPattern">The directory-name pattern to match.</param>
    /// <param name="searchOption">Whether to search only the directory or all subdirectories.</param>
    /// <returns>An enumerable of matching directory paths.</returns>
    public static IEnumerable<string> EnumerateDirectories(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly
    ) {
        return Directory.EnumerateDirectories(path: NormalizeForIO(path: path), searchPattern: searchPattern, searchOption: searchOption);
    }

    /// <summary>
    /// Enumerates files and directories under a path using a normalized path.
    /// </summary>
    /// <param name="path">The parent directory path.</param>
    /// <returns>An enumerable of matching file and directory paths.</returns>
    public static IEnumerable<string> EnumerateFileSystemEntries(string path) {
        return Directory.EnumerateFileSystemEntries(path: NormalizeForIO(path: path));
    }

    /// <summary>
    /// Creates a directory and any missing parent directories using a normalized path.
    /// </summary>
    /// <param name="path">The directory path.</param>
    /// <returns>The created directory.</returns>
    public static DirectoryInfo CreateDirectory(string path) {
        return Directory.CreateDirectory(path: NormalizeForIO(path: path));
    }

    /// <summary>
    /// Opens a file stream using a normalized path.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="mode">The file creation or opening mode.</param>
    /// <param name="access">The file access mode.</param>
    /// <param name="share">The file sharing mode.</param>
    /// <returns>The opened file stream.</returns>
    public static FileStream OpenFile(
        string path,
        FileMode mode,
        FileAccess access,
        FileShare share = FileShare.None,
        FileOptions options = FileOptions.None
    ) {
        return new FileStream(path: NormalizeForIO(path: path), mode: mode, access: access, share: share, bufferSize: 4096, options: options);
    }

    /// <summary>
    /// Reads all text from a file using a normalized path.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The file contents.</returns>
    public static string ReadAllText(string path) {
        return File.ReadAllText(path: NormalizeForIO(path: path));
    }

    /// <summary>
    /// Writes text to a file using a normalized path.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="contents">The content to write.</param>
    public static void WriteAllText(string path, string contents) {
        File.WriteAllText(path: NormalizeForIO(path: path), contents: contents);
    }

    /// <summary>
    /// Copies a file using normalized source and destination paths.
    /// </summary>
    /// <param name="sourcePath">The source file path.</param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="overwrite">Whether to overwrite an existing destination.</param>
    public static void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) {
        File.Copy(sourceFileName: NormalizeForIO(path: sourcePath), destFileName: NormalizeForIO(path: destinationPath), overwrite: overwrite);
    }

    /// <summary>
    /// Moves a file using normalized source and destination paths.
    /// </summary>
    /// <param name="sourcePath">The source file path.</param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="overwrite">Whether to overwrite an existing destination.</param>
    public static void MoveFile(string sourcePath, string destinationPath, bool overwrite = false) {
        File.Move(sourceFileName: NormalizeForIO(path: sourcePath), destFileName: NormalizeForIO(path: destinationPath), overwrite: overwrite);
    }

    /// <summary>
    /// Deletes a file using a normalized path.
    /// </summary>
    /// <param name="path">The file path.</param>
    public static void DeleteFile(string path) {
        File.Delete(path: NormalizeForIO(path: path));
    }

    /// <summary>
    /// Deletes a directory using a normalized path.
    /// </summary>
    /// <param name="path">The directory path.</param>
    /// <param name="recursive">Whether to delete contained files and directories.</param>
    public static void DeleteDirectory(string path, bool recursive = false) {
        Directory.Delete(path: NormalizeForIO(path: path), recursive: recursive);
    }

    /// <summary>
    /// Gets file or directory attributes using a normalized path.
    /// </summary>
    /// <param name="path">The file or directory path.</param>
    /// <returns>The path attributes.</returns>
    public static FileAttributes GetAttributes(string path) {
        return File.GetAttributes(path: NormalizeForIO(path: path));
    }

    /// <summary>
    /// Sets file or directory attributes using a normalized path.
    /// </summary>
    /// <param name="path">The file or directory path.</param>
    /// <param name="attributes">The attributes to set.</param>
    public static void SetAttributes(string path, FileAttributes attributes) {
        File.SetAttributes(path: NormalizeForIO(path: path), fileAttributes: attributes);
    }

    /// <summary>
    /// Moves a directory using normalized source and destination paths.
    /// </summary>
    /// <param name="sourcePath">The source directory path.</param>
    /// <param name="destinationPath">The destination directory path.</param>
    public static void MoveDirectory(string sourcePath, string destinationPath) {
        Directory.Move(sourceDirName: NormalizeForIO(path: sourcePath), destDirName: NormalizeForIO(path: destinationPath));
    }

    private static string AddExtendedLengthPrefix(string path, bool isWindows) {
        if (!isWindows || path.Length <= MaxLegacyPathLength || path.StartsWith(value: ExtendedPathPrefix, comparisonType: StringComparison.OrdinalIgnoreCase)) {
            return path;
        }

        if (path.StartsWith(value: @"\\", comparisonType: StringComparison.Ordinal)) {
            return ExtendedUncPathPrefix + path.Substring(startIndex: 2);
        }

        return ExtendedPathPrefix + path;
    }

    private static string RemoveExtendedLengthPrefix(string path) {
        if (path.StartsWith(value: ExtendedUncPathPrefix, comparisonType: StringComparison.OrdinalIgnoreCase)) {
            return @"\\" + path.Substring(startIndex: ExtendedUncPathPrefix.Length);
        }

        return path.StartsWith(value: ExtendedPathPrefix, comparisonType: StringComparison.OrdinalIgnoreCase)
            ? path.Substring(startIndex: ExtendedPathPrefix.Length)
            : path;
    }
}