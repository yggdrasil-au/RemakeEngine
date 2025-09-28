using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EngineNet.Core.Sys;

namespace EngineNet.Core.ScriptEngines.Helpers;

/// <summary>
/// Utility helpers for common project setup and filesystem operations used by scripts.
/// </summary>
public static class ConfigHelpers {
    private static void Emit(String message, Boolean newline = true, String? color = null) {
        try {
            EngineSdk.Print(message ?? String.Empty, color, newline);
        } catch {
            if (newline) {
                Console.WriteLine(message);
            } else {
                Console.Write(message);
            }
        }
    }

    private static String BuildProgressBar(String label, Int32 current, Int32 total, Int32 width = 30) {
        if (total <= 0) {
            return $"{label}: [" + new String('.', width) + "] 100%";
        }
        Double ratio = current / (Double)total;
        if (ratio < 0) ratio = 0; if (ratio > 1) ratio = 1;
        Int32 filled = (Int32)Math.Round(ratio * width);
        String bar = new String('=', Math.Max(0, filled - 1)) + (filled > 0 ? ">" : "") + new String('.', Math.Max(0, width - filled));
        Int32 percent = (Int32)Math.Round(ratio * 100);
        return $"{label}: [{bar}] {current}/{total} ({percent}%)";
    }

    /// <summary>
    /// Ensure a minimal project.json exists under <paramref name="rootDir"/>.
    /// If missing, creates a skeleton file similar to EngineNet.Program. Returns the config path.
    /// </summary>
    public static String EnsureProjectConfig(String rootDir) {
        if (String.IsNullOrWhiteSpace(rootDir)) {
            throw new ArgumentException("rootDir is empty");
        }

        Directory.CreateDirectory(rootDir);
        String configPath = Path.Combine(rootDir, "project.json");
        if (!File.Exists(configPath)) {
            // Keep consistent with EngineNet/Program.cs default content
            String minimal = "{\n  \"RemakeEngine\": {\n    \"Config\": { \"project_path\": \"" + rootDir.Replace("\\", "\\\\") + "\" },\n    \"Directories\": {},\n    \"Tools\": {}\n  }\n}";
            File.WriteAllText(configPath, minimal);
        }
        return configPath;
    }

    /// <summary>
    /// Validates that a source directory exists and is accessible.
    /// Throws if invalid.
    /// </summary>
    public static void ValidateSourceDir(String dir) {
        if (String.IsNullOrWhiteSpace(dir)) {
            throw new ArgumentException("Source directory path is empty");
        }

        if (!Directory.Exists(dir)) {
            throw new DirectoryNotFoundException($"Source directory not found: {dir}");
        }
        // Basic access check: attempt to enumerate one entry (if any)
        try {
            using IEnumerator<String> _ = Directory.EnumerateFileSystemEntries(dir).GetEnumerator();
        } catch (Exception ex) {
            throw new IOException($"Cannot access source directory '{dir}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Recursively copy a directory to destination. Creates destination if needed.
    /// If <paramref name="overwrite"/> is false and destination exists, throws.
    /// Emits progress updates to the engine console.
    /// </summary>
    public static void CopyDirectory(String sourceDir, String destDir, Boolean overwrite = false) {
        if (String.IsNullOrWhiteSpace(sourceDir)) {
            throw new ArgumentException("sourceDir is empty");
        }

        if (String.IsNullOrWhiteSpace(destDir)) {
            throw new ArgumentException("destDir is empty");
        }

        if (!Directory.Exists(sourceDir)) {
            throw new DirectoryNotFoundException($"Source not found: {sourceDir}");
        }

        if (Directory.Exists(destDir)) {
            if (!overwrite) {
                throw new IOException($"Destination already exists: {destDir}");
            }
        } else {
            Directory.CreateDirectory(destDir);
        }

        String srcRoot = Path.GetFullPath(sourceDir);
        String dstRoot = Path.GetFullPath(destDir);

        // Create all directories first
        foreach (String dir in Directory.EnumerateDirectories(srcRoot, "*", SearchOption.AllDirectories)) {
            String rel = Path.GetRelativePath(srcRoot, dir);
            String target = Path.Combine(dstRoot, rel);
            Directory.CreateDirectory(target);
        }

        // Prepare files list to compute progress
        List<String> files = Directory.EnumerateFiles(srcRoot, "*", SearchOption.AllDirectories).ToList();
        Int32 total = files.Count;
        Int32 current = 0;
        DateTime lastUpdate = DateTime.UtcNow;
        Int32 lastPercent = -1;

        // Emit initial line
        if (total > 0) {
            Emit($"Copying {total} files from '{srcRoot}' to '{dstRoot}'...");
        }

        // Copy files with progress
        foreach (String file in files) {
            String rel = Path.GetRelativePath(srcRoot, file);
            String target = Path.Combine(dstRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);

            current++;

            // Throttle updates: on percent change or every 100ms
            Int32 percent = total > 0 ? (Int32)Math.Floor((current * 100.0) / total) : 100;
            DateTime now = DateTime.UtcNow;
            if (percent != lastPercent || (now - lastUpdate).TotalMilliseconds >= 100 || current == total) {
                String line = "\r" + BuildProgressBar("Copying", current, total);
                Emit(line, newline: false);
                lastPercent = percent;
                lastUpdate = now;
            }
        }

        // Finish line
        if (total > 0) {
            Emit(String.Empty, newline: true);
        }
    }

    /// <summary>
    /// Move a directory to a new location. If <paramref name="overwrite"/> is false and
    /// destination exists, throws. If moving across volumes or into an existing destination,
    /// falls back to copy+delete. Emits progress for copy operations.
    /// </summary>
    public static void MoveDirectory(String sourceDir, String destDir, Boolean overwrite = false) {
        if (String.IsNullOrWhiteSpace(sourceDir)) {
            throw new ArgumentException("sourceDir is empty");
        }

        if (String.IsNullOrWhiteSpace(destDir)) {
            throw new ArgumentException("destDir is empty");
        }

        if (!Directory.Exists(sourceDir)) {
            throw new DirectoryNotFoundException($"Source not found: {sourceDir}");
        }

        if (Directory.Exists(destDir)) {
            if (!overwrite) {
                throw new IOException($"Destination already exists: {destDir}");
            }
            // We'll merge by copy then delete source
            Emit($"Merging '{sourceDir}' into existing '{destDir}'...");
            CopyDirectory(sourceDir, destDir, overwrite: true);
            Emit("Deleting source after merge...");
            Directory.Delete(sourceDir, recursive: true);
            Emit("Move complete.");
            return;
        }

        try {
            Emit($"Moving directory '{sourceDir}' -> '{destDir}' (fast move) ...", newline: false);
            Directory.Move(sourceDir, destDir);
            Emit(" done.", newline: true);
        } catch {
            // Fallback to copy+delete for cross-device moves
            Emit("Fast move not available; falling back to copy...", newline: true);
            CopyDirectory(sourceDir, destDir, overwrite: true);
            Emit("Deleting source after copy...", newline: true);
            Directory.Delete(sourceDir, recursive: true);
            Emit("Move complete.");
        }
    }

    /// <summary>
    /// Returns the full path to a direct child subdirectory of <paramref name="baseDir"/>
    /// named <paramref name="name"/>. Comparison is case-insensitive on Windows.
    /// Returns null if not found.
    /// </summary>
    public static String? FindSubdir(String baseDir, String name, Boolean caseInsensitive = true) {
        if (!Directory.Exists(baseDir)) {
            return null;
        }

        StringComparer cmp = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        try {
            foreach (String d in Directory.EnumerateDirectories(baseDir)) {
                String dn = new DirectoryInfo(d).Name;
                if (cmp.Equals(dn, name)) {
                    return d;
                }
            }
        } catch { }
        return null;
    }

    /// <summary>
    /// Checks whether all subdirectory names in <paramref name="names"/> exist directly under <paramref name="baseDir"/>.
    /// Comparison is case-insensitive on Windows by default.
    /// </summary>
    public static Boolean HasAllSubdirs(String baseDir, IEnumerable<String> names, Boolean caseInsensitive = true) {
        if (!Directory.Exists(baseDir)) {
            return false;
        }

        StringComparer cmp = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        HashSet<String> existing;
        try {
            existing = Directory.EnumerateDirectories(baseDir)
                .Select(d => new DirectoryInfo(d).Name)
                .ToHashSet(cmp);
        } catch {
            return false;
        }
        foreach (String n in names) {
            if (String.IsNullOrWhiteSpace(n)) {
                continue;
            }

            if (!existing.Contains(n)) {
                return false;
            }
        }
        return true;
    }
}
