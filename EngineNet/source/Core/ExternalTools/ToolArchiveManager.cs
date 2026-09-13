using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;

namespace EngineNet.Core.ExternalTools;

internal sealed record ToolArchivePaths(
    string DownloadDir,
    string InstallDir
);

internal sealed class ToolArchiveManager {
    private readonly string _centralToolsRoot;

    internal ToolArchiveManager(string rootPath) {
        _centralToolsRoot = System.IO.Path.GetFullPath(path: System.IO.Path.Combine(path1: rootPath, path2: "EngineApps", path3: "Tools"));
    }

    internal ToolArchivePaths GetPaths(string toolName, string version, string platform) {
        string folderName = BuildToolInstallFolderName(toolName: toolName, version: version, platform: platform);
        return new ToolArchivePaths(
            DownloadDir: System.IO.Path.Combine(path1: _centralToolsRoot, path2: "_archives", path3: folderName),
            InstallDir: System.IO.Path.Combine(path1: _centralToolsRoot, path2: folderName)
        );
    }

    internal string? ExtractAndFindExe(string archivePath, string installDir, string toolName, string? preferredExeName) {
        if (System.IO.Directory.Exists(path: installDir)) {
            System.IO.Directory.Delete(path: installDir, recursive: true);
        }

        System.IO.Directory.CreateDirectory(path: installDir);
        IO.Info($"Unpacking to: {installDir}");

        try {
            ExtractArchiveToInstallLayout(archivePath: archivePath, installDir: installDir);
        } catch (NotSupportedException ex) {
            IO.Warn($"{ex.Message} Leaving archive as-is.");
        } catch (System.IO.IOException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolArchiveManager.cs::ExtractAndFindExe()] Failed to unpack archive '{archivePath}' to '{installDir}'.", ex: ex);
            IO.writeLine($"1 ERROR: Failed to unpack '{archivePath}': {ex.Message}", color: System.ConsoleColor.Red);
        } catch (UnauthorizedAccessException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolArchiveManager.cs::ExtractAndFindExe()] Access denied unpacking archive '{archivePath}' to '{installDir}'.", ex: ex);
            IO.writeLine($"1 ERROR: Failed to unpack '{archivePath}': {ex.Message}", color: System.ConsoleColor.Red);
        }

        string? exePath = FindExe(root: installDir, toolName: toolName, preferredExeName: preferredExeName);
        if (!string.IsNullOrWhiteSpace(exePath)) {
            IO.Info($"Detected executable: {exePath}");
        } else {
            IO.Warn("Could not detect an executable automatically.");
        }

        return exePath;
    }

    private static void ExtractArchiveToInstallLayout(string archivePath, string installDir) {
        string stagingDir = System.IO.Path.Combine(path1: installDir, path2: ".extract-staging");
        if (System.IO.Directory.Exists(path: stagingDir)) {
            System.IO.Directory.Delete(path: stagingDir, recursive: true);
        }

        System.IO.Directory.CreateDirectory(path: stagingDir);
        try {
            ExtractArchive(archivePath: archivePath, destination: stagingDir);
            PromoteExtractedContent(stagingDir: stagingDir, installDir: installDir);
        } finally {
            if (System.IO.Directory.Exists(path: stagingDir)) {
                System.IO.Directory.Delete(path: stagingDir, recursive: true);
            }
        }
    }

    private static void ExtractArchive(string archivePath, string destination) {
        string ext = System.IO.Path.GetExtension(path: archivePath).ToLowerInvariant();

        switch (ext) {
            case ".zip":
                System.IO.Compression.ZipFile.ExtractToDirectory(sourceArchiveFileName: archivePath, destinationDirectoryName: destination, overwriteFiles: true);
                return;
            case ".7z": {
                using SevenZipArchive archive = SevenZipArchive.Open(filePath: archivePath);
                ExtractionOptions options = new ExtractionOptions {
                    ExtractFullPath = true,
                    Overwrite = true
                };
                archive.WriteToDirectory(destinationDirectory: destination, options: options);
                return;
            }
            default:
                throw new NotSupportedException($"Archive format not supported for auto-unpack: {ext}");
        }
    }

    private static void PromoteExtractedContent(string stagingDir, string installDir) {
        string[] topLevelEntries = System.IO.Directory.GetFileSystemEntries(path: stagingDir);

        string sourceRoot = stagingDir;
        if (topLevelEntries.Length == 1 && System.IO.Directory.Exists(path: topLevelEntries[0])) {
            sourceRoot = topLevelEntries[0];
        }

        MoveDirectoryContents(sourceDir: sourceRoot, targetDir: installDir);

        if (!string.Equals(a: sourceRoot, b: stagingDir, comparisonType: System.StringComparison.OrdinalIgnoreCase) && System.IO.Directory.Exists(path: sourceRoot)) {
            System.IO.Directory.Delete(path: sourceRoot, recursive: true);
        }
    }

    private static void MoveDirectoryContents(string sourceDir, string targetDir) {
        System.IO.Directory.CreateDirectory(path: targetDir);

        foreach (string filePath in System.IO.Directory.GetFiles(path: sourceDir)) {
            string fileName = System.IO.Path.GetFileName(path: filePath);
            string targetPath = System.IO.Path.Combine(path1: targetDir, path2: fileName);
            System.IO.File.Move(sourceFileName: filePath, destFileName: targetPath, overwrite: true);
        }

        foreach (string subDir in System.IO.Directory.GetDirectories(path: sourceDir)) {
            string dirName = System.IO.Path.GetFileName(path: subDir);
            string targetSubDir = System.IO.Path.Join(path1: targetDir, path2: dirName);
            if (System.IO.Directory.Exists(path: targetSubDir)) {
                MoveDirectoryContents(sourceDir: subDir, targetDir: targetSubDir);
                if (System.IO.Directory.Exists(path: subDir)) {
                    System.IO.Directory.Delete(path: subDir, recursive: true);
                }
            } else {
                System.IO.Directory.Move(sourceDirName: subDir, destDirName: targetSubDir);
            }
        }
    }

    private static string? FindExe(string root, string toolName, string? preferredExeName) {
        string? foundPath = null;

        if (!string.IsNullOrWhiteSpace(preferredExeName)) {
            foundPath = SearchForFile(root: root, pattern: preferredExeName);
        }

        if (string.IsNullOrWhiteSpace(foundPath)) {
            foundPath = SearchForFile(root: root, pattern: $"{toolName}.exe")
                ?? SearchForFile(root: root, pattern: toolName)
                ?? SearchForFile(root: root, pattern: $"{toolName}*.exe")
                ?? SearchForFile(root: root, pattern: $"*{toolName}*.exe")
                ?? SearchForFile(root: root, pattern: $"*{toolName}*");
        }

        if (!string.IsNullOrWhiteSpace(foundPath)) {
            ApplyExecutablePermissions(path: foundPath);
        }

        return foundPath;
    }

    private static string? SearchForFile(string root, string pattern) {
        System.IO.EnumerationOptions options = new System.IO.EnumerationOptions {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = System.IO.MatchCasing.CaseInsensitive,
            MaxRecursionDepth = 5
        };

        try {
            foreach (string filePath in System.IO.Directory.EnumerateFiles(path: root, searchPattern: pattern, enumerationOptions: options)) {
                return filePath;
            }
        } catch (System.IO.DirectoryNotFoundException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolArchiveManager.cs::SearchForFile()] Root directory does not exist: {root}", ex: ex);
        } catch (ArgumentException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolArchiveManager.cs::SearchForFile()] Invalid path or pattern: {ex.Message}", ex: ex);
        } catch (System.IO.IOException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolArchiveManager.cs::SearchForFile()] IO error searching '{pattern}' in {root}: {ex.Message}", ex: ex);
        } catch (UnauthorizedAccessException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolArchiveManager.cs::SearchForFile()] Access denied searching '{pattern}' in {root}: {ex.Message}", ex: ex);
        }

        return null;
    }

    private static void ApplyExecutablePermissions(string path) {
        if (System.OperatingSystem.IsWindows()) {
            return;
        }

        try {
            System.IO.UnixFileMode currentMode = System.IO.File.GetUnixFileMode(path: path);
            System.IO.UnixFileMode newMode = currentMode | System.IO.UnixFileMode.UserExecute | System.IO.UnixFileMode.GroupExecute;
            System.IO.File.SetUnixFileMode(path: path, mode: newMode);
            IO.Info($"Applied executable permissions to: {path}");
        } catch (UnauthorizedAccessException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolArchiveManager.cs::ApplyExecutablePermissions()] Access denied setting permissions for '{path}'.", ex: ex);
            IO.Warn($"Insufficient permissions to set executable bit on {path}");
        } catch (System.IO.IOException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolArchiveManager.cs::ApplyExecutablePermissions()] IO error while updating permissions for '{path}'.", ex: ex);
            IO.Warn($"Could not update permissions for {path}: {ex.Message}");
        }
    }

    private static string BuildToolInstallFolderName(string toolName, string version, string platform) {
        return $"{SanitizePathSegment(toolName)}-{SanitizePathSegment(version)}-{SanitizePathSegment(platform)}";
    }

    private static string SanitizePathSegment(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return "unknown";
        }

        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        char[] chars = value.ToCharArray();

        for (int index = 0; index < chars.Length; index++) {
            if (System.Array.IndexOf(array: invalid, chars[index]) >= 0) {
                chars[index] = '_';
            }
        }

        return new string(chars).Trim();
    }
}
