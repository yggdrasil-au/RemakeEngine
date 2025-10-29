
using System.Linq;
using System.Collections.Generic;

namespace EngineNet.Core.ScriptEngines.Helpers;

/// <summary>
/// Utility helpers for common project setup and filesystem operations used by scripts.
/// </summary>
internal static class ConfigHelpers {
    private static void Emit(string message, bool newline = true, string? color = null) {
        try {
            Core.Utils.EngineSdk.Print(message ?? string.Empty, color, newline);
        } catch {
            if (newline) {
                System.Console.WriteLine(message);
            } else {
                System.Console.Write(message);
            }
        }
    }

    private static string BuildProgressBar(string label, int current, int total, int width = 30) {
        if (total <= 0) {
            return $"{label}: [" + new string('.', width) + "] 100%";
        }
        double ratio = current / (double)total;
        if (ratio < 0) ratio = 0; if (ratio > 1) ratio = 1;
        int filled = (int)System.Math.Round(ratio * width);
        string bar = new string('=', System.Math.Max(0, filled - 1)) + (filled > 0 ? ">" : "") + new string('.', System.Math.Max(0, width - filled));
        int percent = (int)System.Math.Round(ratio * 100);
        return $"{label}: [{bar}] {current}/{total} ({percent}%)";
    }

    /// <summary>
    /// Ensure a minimal project.json exists under <paramref name="rootDir"/>.
    /// If missing, creates a skeleton file similar to EngineNet.Program. Returns the config path.
    /// </summary>
    public static string EnsureProjectConfig(string rootDir) {
        if (string.IsNullOrWhiteSpace(rootDir)) {
            throw new System.ArgumentException("rootDir is empty");
        }

        System.IO.Directory.CreateDirectory(rootDir);
        string configPath = System.IO.Path.Combine(rootDir, "project.json");
        if (!System.IO.File.Exists(configPath)) {
            // Keep consistent with EngineNet/Program.cs default content
            string minimal = "{\n  \"RemakeEngine\": {\n    \"Config\": { \"project_path\": \"" + rootDir.Replace("\\", "\\\\") + "\" },\n    \"Directories\": {},\n    \"Tools\": {}\n  }\n}";
            System.IO.File.WriteAllText(configPath, minimal);
        }
        return configPath;
    }

    /// <summary>
    /// Validates that a source directory exists and is accessible.
    /// Throws if invalid.
    /// </summary>
    public static void ValidateSourceDir(string dir) {
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
    /// Emits progress updates to the engine System.Console.
    /// </summary>
    public static void CopyDirectory(string sourceDir, string destDir, bool overwrite = false) {
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
        System.DateTime lastUpdate = System.DateTime.UtcNow;
        int lastPercent = -1;

        // Emit initial line
        if (total > 0) {
            Emit($"Copying {total} files from '{srcRoot}' to '{dstRoot}'...");
        }

        // Copy files with progress
        foreach (string file in files) {
            string rel = System.IO.Path.GetRelativePath(srcRoot, file);
            string target = System.IO.Path.Combine(dstRoot, rel);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            System.IO.File.Copy(file, target, overwrite: true);

            current++;

            // Throttle updates: on percent change or every 100ms
            int percent = total > 0 ? (int)System.Math.Floor((current * 100.0) / total) : 100;
            System.DateTime now = System.DateTime.UtcNow;
            if (percent != lastPercent || (now - lastUpdate).TotalMilliseconds >= 100 || current == total) {
                string line = "\r" + BuildProgressBar("Copying", current, total);
                Emit(line, newline: false);
                lastPercent = percent;
                lastUpdate = now;
            }
        }

        // Finish line
        if (total > 0) {
            Emit(string.Empty, newline: true);
        }
    }

    /// <summary>
    /// Move a directory to a new location. If <paramref name="overwrite"/> is false and
    /// destination exists, throws. If moving across volumes or into an existing destination,
    /// falls back to copy+delete. Emits progress for copy operations.
    /// </summary>
    public static void MoveDirectory(string sourceDir, string destDir, bool overwrite = false) {
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
            Emit($"Merging '{sourceDir}' into existing '{destDir}'...");
            CopyDirectory(sourceDir, destDir, overwrite: true);
            Emit("Deleting source after merge...");
            System.IO.Directory.Delete(sourceDir, recursive: true);
            Emit("Move complete.");
            return;
        }

        try {
            Emit($"Moving directory '{sourceDir}' -> '{destDir}' (fast move) ...", newline: false);
            System.IO.Directory.Move(sourceDir, destDir);
            Emit(" done.", newline: true);
        } catch {
            // Fallback to copy+delete for cross-device moves
            Emit("Fast move not available; falling back to copy...", newline: true);
            CopyDirectory(sourceDir, destDir, overwrite: true);
            Emit("Deleting source after copy...", newline: true);
            System.IO.Directory.Delete(sourceDir, recursive: true);
            Emit("Move complete.");
        }
    }

    /// <summary>
    /// Returns the full path to a direct child subdirectory of <paramref name="baseDir"/>
    /// named <paramref name="name"/>. Comparison is case-insensitive on Windows.
    /// Returns null if not found.
    /// </summary>
    public static string? FindSubdir(string baseDir, string name, bool caseInsensitive = true) {
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
#if DEBUG
            System.Console.WriteLine($"Error .....'");
#endif
        }
        return null;
    }

    /// <summary>
    /// Checks whether all subdirectory names in <paramref name="names"/> exist directly under <paramref name="baseDir"/>.
    /// Comparison is case-insensitive on Windows by default.
    /// </summary>
    public static bool HasAllSubdirs(string baseDir, IEnumerable<string> names, bool caseInsensitive = true) {
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
