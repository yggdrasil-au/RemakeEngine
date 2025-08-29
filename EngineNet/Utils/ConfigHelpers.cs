using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RemakeEngine.Utils;

/// <summary>
/// Utility helpers for common project setup and filesystem operations used by scripts.
/// </summary>
public static class ConfigHelpers {
    /// <summary>
    /// Ensure a minimal project.json exists under <paramref name="rootDir"/>.
    /// If missing, creates a skeleton file similar to EngineNet.Program. Returns the config path.
    /// </summary>
    public static string EnsureProjectConfig(string rootDir) {
        if (string.IsNullOrWhiteSpace(rootDir)) throw new ArgumentException("rootDir is empty");
        Directory.CreateDirectory(rootDir);
        var configPath = Path.Combine(rootDir, "project.json");
        if (!File.Exists(configPath)) {
            // Keep consistent with EngineNet/Program.cs default content
            var minimal = "{\n  \"RemakeEngine\": {\n    \"Config\": { \"project_path\": \"" + rootDir.Replace("\\", "\\\\") + "\" },\n    \"Directories\": {},\n    \"Tools\": {}\n  }\n}";
            File.WriteAllText(configPath, minimal);
        }
        return configPath;
    }

    /// <summary>
    /// Validates that a source directory exists and is accessible.
    /// Throws if invalid.
    /// </summary>
    public static void ValidateSourceDir(string dir) {
        if (string.IsNullOrWhiteSpace(dir))
            throw new ArgumentException("Source directory path is empty");
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"Source directory not found: {dir}");
        // Basic access check: attempt to enumerate one entry (if any)
        try {
            using var _ = Directory.EnumerateFileSystemEntries(dir).GetEnumerator();
        } catch (Exception ex) {
            throw new IOException($"Cannot access source directory '{dir}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Recursively copy a directory to destination. Creates destination if needed.
    /// If <paramref name="overwrite"/> is false and destination exists, throws.
    /// </summary>
    public static void CopyDirectory(string sourceDir, string destDir, bool overwrite = false) {
        if (string.IsNullOrWhiteSpace(sourceDir)) throw new ArgumentException("sourceDir is empty");
        if (string.IsNullOrWhiteSpace(destDir)) throw new ArgumentException("destDir is empty");
        if (!Directory.Exists(sourceDir)) throw new DirectoryNotFoundException($"Source not found: {sourceDir}");

        if (Directory.Exists(destDir)) {
            if (!overwrite)
                throw new IOException($"Destination already exists: {destDir}");
        } else {
            Directory.CreateDirectory(destDir);
        }

        var srcRoot = Path.GetFullPath(sourceDir);
        var dstRoot = Path.GetFullPath(destDir);

        foreach (var dir in Directory.EnumerateDirectories(srcRoot, "*", SearchOption.AllDirectories)) {
            var rel = Path.GetRelativePath(srcRoot, dir);
            var target = Path.Combine(dstRoot, rel);
            Directory.CreateDirectory(target);
        }
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*", SearchOption.AllDirectories)) {
            var rel = Path.GetRelativePath(srcRoot, file);
            var target = Path.Combine(dstRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>
    /// Move a directory to a new location. If <paramref name="overwrite"/> is false and
    /// destination exists, throws. If moving across volumes or into an existing destination,
    /// falls back to copy+delete.
    /// </summary>
    public static void MoveDirectory(string sourceDir, string destDir, bool overwrite = false) {
        if (string.IsNullOrWhiteSpace(sourceDir)) throw new ArgumentException("sourceDir is empty");
        if (string.IsNullOrWhiteSpace(destDir)) throw new ArgumentException("destDir is empty");
        if (!Directory.Exists(sourceDir)) throw new DirectoryNotFoundException($"Source not found: {sourceDir}");

        if (Directory.Exists(destDir)) {
            if (!overwrite)
                throw new IOException($"Destination already exists: {destDir}");
            // We'll merge by copy then delete source
            CopyDirectory(sourceDir, destDir, overwrite: true);
            Directory.Delete(sourceDir, recursive: true);
            return;
        }

        try {
            Directory.Move(sourceDir, destDir);
        } catch {
            // Fallback to copy+delete for cross-device moves
            CopyDirectory(sourceDir, destDir, overwrite: true);
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    /// <summary>
    /// Returns the full path to a direct child subdirectory of <paramref name="baseDir"/>
    /// named <paramref name="name"/>. Comparison is case-insensitive on Windows.
    /// Returns null if not found.
    /// </summary>
    public static string? FindSubdir(string baseDir, string name, bool caseInsensitive = true) {
        if (!Directory.Exists(baseDir)) return null;
        var cmp = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        try {
            foreach (var d in Directory.EnumerateDirectories(baseDir)) {
                var dn = new DirectoryInfo(d).Name;
                if (cmp.Equals(dn, name)) return d;
            }
        } catch { }
        return null;
    }

    /// <summary>
    /// Checks whether all subdirectory names in <paramref name="names"/> exist directly under <paramref name="baseDir"/>.
    /// Comparison is case-insensitive on Windows by default.
    /// </summary>
    public static bool HasAllSubdirs(string baseDir, IEnumerable<string> names, bool caseInsensitive = true) {
        if (!Directory.Exists(baseDir)) return false;
        var cmp = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        HashSet<string> existing;
        try {
            existing = Directory.EnumerateDirectories(baseDir)
                .Select(d => new DirectoryInfo(d).Name)
                .ToHashSet(cmp);
        } catch {
            return false;
        }
        foreach (var n in names) {
            if (string.IsNullOrWhiteSpace(n)) continue;
            if (!existing.Contains(n)) return false;
        }
        return true;
    }
}
