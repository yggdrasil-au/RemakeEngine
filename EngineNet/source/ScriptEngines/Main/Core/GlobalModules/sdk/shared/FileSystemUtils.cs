

namespace EngineNet.ScriptEngines.Global.SdkModule;

/// <summary>
/// File system utilities for script execution.
/// Provides safe file system operations with proper security checks.
/// </summary>
internal static class FileSystemUtils {
    internal static bool PathExists(string path) => System.IO.Path.Exists(path);

    internal static bool PathExistsIncludingLinks(string path) {
        if (PathExists(path)) {
            return true;
        }

        try {
            System.IO.FileSystemInfo info = GetInfo(path);
            return info.Exists || info.LinkTarget != null;
        } catch (Exception ex) {
            Shared.Diagnostics.Bug("[FileSystemUtils] path_exists_including_links catch triggered for path: " + path + " with exception: " + ex);
            Shared.Diagnostics.LuaInternalCatch("path_exists_including_links failed for path: " + path + " with exception: " + ex);
            return false;
        }
    }

    internal static bool IsSymlink(string path) {
        try {
            System.IO.FileSystemInfo info = GetInfo(path);
            return info.LinkTarget != null || info.Attributes.HasFlag(System.IO.FileAttributes.ReparsePoint);
        } catch (Exception ex) {
            Shared.Diagnostics.LuaInternalCatch("is_symlink failed for path: " + path + " with exception: " + ex);
            return false;
        }
    }

    internal static string? RealPath(string path) {
        try {
            return System.IO.Path.GetFullPath(path);
        } catch (Exception ex) {
            Shared.Diagnostics.LuaInternalCatch("real_path failed for path: " + path + " with exception: " + ex);
            return null;
        }
    }

    internal static string? ReadLink(string path) {
        try {
            System.IO.FileSystemInfo info = GetInfo(path);
            return info.LinkTarget;
        } catch (Exception ex) {
            Shared.Diagnostics.LuaInternalCatch("read_link failed for path: " + path + " with exception: " + ex);
            return null;
        }
    }

    /// <summary>
    /// Move a directory to a new location. If <paramref name="overwrite"/> is false and
    /// destination exists, throws. If moving across volumes or into an existing destination,
    /// falls back to copy+delete. Writes progress for copy operations.
    /// </summary>
    internal static void MoveDirectory(string sourceDir, string destDir, bool overwrite = false) {
        if (string.IsNullOrWhiteSpace(sourceDir)) {
            throw new System.ArgumentException("sourceDir is empty");
        }

        if (string.IsNullOrWhiteSpace(destDir)) {
            throw new System.ArgumentException("destDir is empty");
        }

        if (!System.IO.Directory.Exists(sourceDir)) {
            throw new System.IO.DirectoryNotFoundException($"Source not found: {sourceDir}");
        }

        if (System.IO.Directory.Exists(destDir)) {
            if (!overwrite) {
                throw new System.IO.IOException($"Destination already exists: {destDir}");
            }
            // We'll merge by copy then delete source
            Core.UI.EngineSdk.Print($"Merging '{sourceDir}' into existing '{destDir}'...");
            CopyDirectory(sourceDir, destDir, overwrite: true, progressLabel: $"Merging {sourceDir} to {destDir}...");
            Core.UI.EngineSdk.Print("Deleting source after merge...");
            System.IO.Directory.Delete(sourceDir, recursive: true);
            Core.UI.EngineSdk.Print("Move complete.");
            return;
        }

        try {
            Core.UI.EngineSdk.Print($"Moving directory '{sourceDir}' -> '{destDir}' (fast move) ...", newline: false);
            System.IO.Directory.Move(sourceDir, destDir);
            Core.UI.EngineSdk.Print(" done.", newline: true);
        } catch {
            // Fallback to copy+delete for cross-device moves
            Core.UI.EngineSdk.Print("Fast move not available; falling back to copy...", newline: true);
            CopyDirectory(sourceDir, destDir, overwrite: true, progressLabel: $"Moving {sourceDir} to {destDir}...");
            Core.UI.EngineSdk.Print("Deleting source after copy...", newline: true);
            System.IO.Directory.Delete(sourceDir, recursive: true);
            Core.UI.EngineSdk.Print("Move complete.");
        }
    }


    /// <summary>
    /// Recursively copy a directory to destination. Creates destination if needed.
    /// If <paramref name="overwrite"/> is false and destination exists, throws.
    /// Writes progress updates to the engine System.Console.
    /// </summary>
    internal static void CopyDirectory(string sourceDir, string destDir, bool overwrite = false, string? progressLabel = null) {
        if (string.IsNullOrWhiteSpace(sourceDir)) {
            throw new System.ArgumentException("sourceDir is empty");
        }

        if (string.IsNullOrWhiteSpace(destDir)) {
            throw new System.ArgumentException("destDir is empty");
        }

        if (!System.IO.Directory.Exists(sourceDir)) {
            throw new System.IO.DirectoryNotFoundException($"Source not found: {sourceDir}");
        }

        if (System.IO.Directory.Exists(destDir)) {
            if (!overwrite) {
                throw new System.IO.IOException($"Destination already exists: {destDir}");
            }
        } else {
            System.IO.Directory.CreateDirectory(destDir);
        }

        string srcRoot = System.IO.Path.GetFullPath(sourceDir);
        string dstRoot = System.IO.Path.GetFullPath(destDir);

        // Create all directories first
        foreach (string target in System.IO.Directory.EnumerateDirectories(srcRoot, "*", System.IO.SearchOption.AllDirectories).Select(dir => System.IO.Path.Combine(dstRoot, System.IO.Path.GetRelativePath(srcRoot, dir)))) {
            System.IO.Directory.CreateDirectory(target);
        }

        // Prepare files list to compute progress
        List<string> files = System.IO.Directory.EnumerateFiles(srcRoot, "*", System.IO.SearchOption.AllDirectories).ToList();
        int total = files.Count;
        //int current = 0;

        using Core.UI.EngineSdk.PanelProgress? progress = total > 0
            ? new Core.UI.EngineSdk.PanelProgress(total, id: "fs_copy", label: progressLabel ?? $"Copying {total} files...")
            : null;

        // Write initial line
        if (total > 0) {
            Core.UI.EngineSdk.Print($"Copying {total} files from '{srcRoot}' to '{dstRoot}'...");
        }

        // Copy files with progress
        foreach (var item in files.Select(file => (File: file, Target: System.IO.Path.Combine(dstRoot, System.IO.Path.GetRelativePath(srcRoot, file))))) {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(item.Target)!);
            System.IO.File.Copy(item.File, item.Target, overwrite: true);

            //current++;
            progress?.Update(1);
        }

        // Finish line
        if (total > 0) {
            progress?.Complete();
        }
    }


    /// <summary>
    /// Returns the full path to a direct child subdirectory of <paramref name="baseDir"/>
    /// named <paramref name="name"/>. Comparison is case-insensitive on Windows.
    /// Returns null if not found.
    /// </summary>
    internal static string? FindSubdir(string baseDir, string name, bool caseInsensitive = true) {
        if (!System.IO.Directory.Exists(baseDir)) {
            return null;
        }

        System.StringComparer cmp = caseInsensitive ? System.StringComparer.OrdinalIgnoreCase : System.StringComparer.Ordinal;
        try {
            foreach (string d in System.IO.Directory.EnumerateDirectories(baseDir)) {
                string dn = new System.IO.DirectoryInfo(d).Name;
                if (cmp.Equals(dn, name)) {
                    return d;
                }
            }
        } catch {
            Shared.Diagnostics.Bug($"[ConfigHelpers] Failed to enumerate directories under '{baseDir}'");
        }
        return null;
    }

    /// <summary>
    /// Checks whether all subdirectory names in <paramref name="names"/> exist directly under <paramref name="baseDir"/>.
    /// Comparison is case-insensitive on Windows by default.
    /// </summary>
    internal static bool HasAllSubdirs(string baseDir, IEnumerable<string> names, bool caseInsensitive = true) {
        if (!System.IO.Directory.Exists(baseDir)) {
            return false;
        }

        System.StringComparer cmp = caseInsensitive ? System.StringComparer.OrdinalIgnoreCase : System.StringComparer.Ordinal;
        HashSet<string> existing;
        try {
            existing = System.IO.Directory.EnumerateDirectories(baseDir)
                .Select(d => new System.IO.DirectoryInfo(d).Name)
                .ToHashSet(cmp);
        } catch {
            return false;
        }
        foreach (string n in names) {
            if (string.IsNullOrWhiteSpace(n)) {
                continue;
            }

            if (!existing.Contains(n)) {
                return false;
            }
        }
        return true;
    }


    private static System.IO.FileSystemInfo GetInfo(string path) {
        string full = System.IO.Path.GetFullPath(path);
        System.IO.DirectoryInfo dirInfo = new System.IO.DirectoryInfo(full);
        if (dirInfo.Exists) {
            return dirInfo;
        }

        System.IO.FileInfo fileInfo = new System.IO.FileInfo(full);
        if (fileInfo.Exists) {
            return fileInfo;
        }
        // Determine based on trailing separator
        return full.EndsWith(System.IO.Path.DirectorySeparatorChar) || full.EndsWith(System.IO.Path.AltDirectorySeparatorChar)
            ? new System.IO.DirectoryInfo(full.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))
            : fileInfo;
    }
}
