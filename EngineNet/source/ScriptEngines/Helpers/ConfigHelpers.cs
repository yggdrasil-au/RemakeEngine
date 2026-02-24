
namespace EngineNet.ScriptEngines.Helpers;

/// <summary>
/// Utility helpers for common project setup and filesystem operations used by scripts.
/// </summary>
internal static class ConfigHelpers {
    private static void Write(string message, bool newline = true, string? color = null) {
        Core.UI.EngineSdk.Print(message ?? string.Empty, color, newline);
    }

    /// <summary>
    /// Validates that a source directory exists and is accessible.
    /// Throws if invalid.
    /// </summary>
    internal static void ValidateSourceDir(string dir) {
        if (string.IsNullOrWhiteSpace(dir)) {
            throw new System.ArgumentException("Source directory path is empty");
        }

        if (!System.IO.Directory.Exists(dir)) {
            throw new System.IO.DirectoryNotFoundException($"Source directory not found: {dir}");
        }
        // Basic access check: attempt to enumerate one entry (if any)
        try {
            using IEnumerator<string> _ = System.IO.Directory.EnumerateFileSystemEntries(dir).GetEnumerator();
        } catch (System.Exception ex) {
            throw new System.IO.IOException($"Cannot access source directory '{dir}': {ex.Message}", ex);
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
        foreach (string dir in System.IO.Directory.EnumerateDirectories(srcRoot, "*", System.IO.SearchOption.AllDirectories)) {
            string rel = System.IO.Path.GetRelativePath(srcRoot, dir);
            string target = System.IO.Path.Combine(dstRoot, rel);
            System.IO.Directory.CreateDirectory(target);
        }

        // Prepare files list to compute progress
        List<string> files = System.IO.Directory.EnumerateFiles(srcRoot, "*", System.IO.SearchOption.AllDirectories).ToList();
        int total = files.Count;
        int current = 0;

        using Core.UI.EngineSdk.PanelProgress? progress = total > 0
            ? new Core.UI.EngineSdk.PanelProgress(total, id: "fs_copy", label: progressLabel ?? $"Copying {total} files...")
            : null;

        // Write initial line
        if (total > 0) {
            Write($"Copying {total} files from '{srcRoot}' to '{dstRoot}'...");
        }

        // Copy files with progress
        foreach (string file in files) {
            string rel = System.IO.Path.GetRelativePath(srcRoot, file);
            string target = System.IO.Path.Combine(dstRoot, rel);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            System.IO.File.Copy(file, target, overwrite: true);

            current++;
            progress?.Update(1);
        }

        // Finish line
        if (total > 0) {
            progress?.Complete();
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
            Write($"Merging '{sourceDir}' into existing '{destDir}'...");
            CopyDirectory(sourceDir, destDir, overwrite: true, progressLabel: $"Merging {sourceDir} to {destDir}...");
            Write("Deleting source after merge...");
            System.IO.Directory.Delete(sourceDir, recursive: true);
            Write("Move complete.");
            return;
        }

        try {
            Write($"Moving directory '{sourceDir}' -> '{destDir}' (fast move) ...", newline: false);
            System.IO.Directory.Move(sourceDir, destDir);
            Write(" done.", newline: true);
        } catch {
            // Fallback to copy+delete for cross-device moves
            Write("Fast move not available; falling back to copy...", newline: true);
            CopyDirectory(sourceDir, destDir, overwrite: true, progressLabel: $"Moving {sourceDir} to {destDir}...");
            Write("Deleting source after copy...", newline: true);
            System.IO.Directory.Delete(sourceDir, recursive: true);
            Write("Move complete.");
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
            Core.Diagnostics.Bug($"[ConfigHelpers] Failed to enumerate directories under '{baseDir}'");
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
}
