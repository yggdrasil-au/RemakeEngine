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
        _centralToolsRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(rootPath, "EngineApps", "Tools"));
    }

    internal ToolArchivePaths GetPaths(string toolName, string version, string platform) {
        string folderName = BuildToolInstallFolderName(toolName, version, platform);
        return new ToolArchivePaths(
            DownloadDir: System.IO.Path.Combine(_centralToolsRoot, "_archives", folderName),
            InstallDir: System.IO.Path.Combine(_centralToolsRoot, folderName)
        );
    }

    internal string? ExtractAndFindExe(string archivePath, string installDir, string toolName, string? preferredExeName) {
        if (System.IO.Directory.Exists(installDir)) {
            System.IO.Directory.Delete(installDir, recursive: true);
        }

        System.IO.Directory.CreateDirectory(installDir);
        IO.Info($"Unpacking to: {installDir}");

        try {
            ExtractArchiveToInstallLayout(archivePath, installDir);
        } catch (NotSupportedException ex) {
            IO.Warn($"{ex.Message} Leaving archive as-is.");
        } catch (System.IO.IOException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolArchiveManager.cs::ExtractAndFindExe()] Failed to unpack archive '{archivePath}' to '{installDir}'.", ex);
            IO.writeLine($"1 ERROR: Failed to unpack '{archivePath}': {ex.Message}", System.ConsoleColor.Red);
        } catch (UnauthorizedAccessException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolArchiveManager.cs::ExtractAndFindExe()] Access denied unpacking archive '{archivePath}' to '{installDir}'.", ex);
            IO.writeLine($"1 ERROR: Failed to unpack '{archivePath}': {ex.Message}", System.ConsoleColor.Red);
        }

        string? exePath = FindExe(installDir, toolName, preferredExeName);
        if (!string.IsNullOrWhiteSpace(exePath)) {
            IO.Info($"Detected executable: {exePath}");
        } else {
            IO.Warn("Could not detect an executable automatically.");
        }

        return exePath;
    }

    private static void ExtractArchiveToInstallLayout(string archivePath, string installDir) {
        string stagingDir = System.IO.Path.Combine(installDir, ".extract-staging");
        if (System.IO.Directory.Exists(stagingDir)) {
            System.IO.Directory.Delete(stagingDir, recursive: true);
        }

        System.IO.Directory.CreateDirectory(stagingDir);
        try {
            ExtractArchive(archivePath, stagingDir);
            PromoteExtractedContent(stagingDir, installDir);
        } finally {
            if (System.IO.Directory.Exists(stagingDir)) {
                System.IO.Directory.Delete(stagingDir, recursive: true);
            }
        }
    }

    private static void ExtractArchive(string archivePath, string destination) {
        string ext = System.IO.Path.GetExtension(archivePath).ToLowerInvariant();

        switch (ext) {
            case ".zip":
                System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, destination, overwriteFiles: true);
                return;
            case ".7z": {
                using SevenZipArchive archive = SevenZipArchive.Open(archivePath);
                ExtractionOptions options = new ExtractionOptions {
                    ExtractFullPath = true,
                    Overwrite = true
                };
                archive.WriteToDirectory(destination, options);
                return;
            }
            default:
                throw new NotSupportedException($"Archive format not supported for auto-unpack: {ext}");
        }
    }

    private static void PromoteExtractedContent(string stagingDir, string installDir) {
        string[] topLevelEntries = System.IO.Directory.GetFileSystemEntries(stagingDir);

        string sourceRoot = stagingDir;
        if (topLevelEntries.Length == 1 && System.IO.Directory.Exists(topLevelEntries[0])) {
            sourceRoot = topLevelEntries[0];
        }

        MoveDirectoryContents(sourceRoot, installDir);

        if (!string.Equals(sourceRoot, stagingDir, System.StringComparison.OrdinalIgnoreCase) && System.IO.Directory.Exists(sourceRoot)) {
            System.IO.Directory.Delete(sourceRoot, recursive: true);
        }
    }

    private static void MoveDirectoryContents(string sourceDir, string targetDir) {
        System.IO.Directory.CreateDirectory(targetDir);

        foreach (string filePath in System.IO.Directory.GetFiles(sourceDir)) {
            string fileName = System.IO.Path.GetFileName(filePath);
            string targetPath = System.IO.Path.Combine(targetDir, fileName);
            System.IO.File.Move(filePath, targetPath, overwrite: true);
        }

        foreach (string subDir in System.IO.Directory.GetDirectories(sourceDir)) {
            string dirName = System.IO.Path.GetFileName(subDir);
            string targetSubDir = System.IO.Path.Join(targetDir, dirName);
            if (System.IO.Directory.Exists(targetSubDir)) {
                MoveDirectoryContents(subDir, targetSubDir);
                if (System.IO.Directory.Exists(subDir)) {
                    System.IO.Directory.Delete(subDir, recursive: true);
                }
            } else {
                System.IO.Directory.Move(subDir, targetSubDir);
            }
        }
    }

    private static string? FindExe(string root, string toolName, string? preferredExeName) {
        string? foundPath = null;

        if (!string.IsNullOrWhiteSpace(preferredExeName)) {
            foundPath = SearchForFile(root, preferredExeName);
        }

        if (string.IsNullOrWhiteSpace(foundPath)) {
            foundPath = SearchForFile(root, $"{toolName}.exe")
                ?? SearchForFile(root, toolName)
                ?? SearchForFile(root, $"{toolName}*.exe")
                ?? SearchForFile(root, $"*{toolName}*.exe")
                ?? SearchForFile(root, $"*{toolName}*");
        }

        if (!string.IsNullOrWhiteSpace(foundPath)) {
            ApplyExecutablePermissions(foundPath);
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
            foreach (string filePath in System.IO.Directory.EnumerateFiles(root, pattern, options)) {
                return filePath;
            }
        } catch (System.IO.DirectoryNotFoundException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolArchiveManager.cs::SearchForFile()] Root directory does not exist: {root}", ex);
        } catch (ArgumentException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolArchiveManager.cs::SearchForFile()] Invalid path or pattern: {ex.Message}", ex);
        } catch (System.IO.IOException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolArchiveManager.cs::SearchForFile()] IO error searching '{pattern}' in {root}: {ex.Message}", ex);
        } catch (UnauthorizedAccessException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolArchiveManager.cs::SearchForFile()] Access denied searching '{pattern}' in {root}: {ex.Message}", ex);
        }

        return null;
    }

    private static void ApplyExecutablePermissions(string path) {
        if (System.OperatingSystem.IsWindows()) {
            return;
        }

        try {
            System.IO.UnixFileMode currentMode = System.IO.File.GetUnixFileMode(path);
            System.IO.UnixFileMode newMode = currentMode | System.IO.UnixFileMode.UserExecute | System.IO.UnixFileMode.GroupExecute;
            System.IO.File.SetUnixFileMode(path, newMode);
            IO.Info($"Applied executable permissions to: {path}");
        } catch (UnauthorizedAccessException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolArchiveManager.cs::ApplyExecutablePermissions()] Access denied setting permissions for '{path}'.", ex);
            IO.Warn($"Insufficient permissions to set executable bit on {path}");
        } catch (System.IO.IOException ex) {
            Shared.IO.Diagnostics.Bug($"[ToolArchiveManager.cs::ApplyExecutablePermissions()] IO error while updating permissions for '{path}'.", ex);
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
            if (System.Array.IndexOf(invalid, chars[index]) >= 0) {
                chars[index] = '_';
            }
        }

        return new string(chars).Trim();
    }
}
