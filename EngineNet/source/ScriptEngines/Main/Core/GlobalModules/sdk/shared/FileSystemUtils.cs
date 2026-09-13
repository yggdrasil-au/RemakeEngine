

namespace EngineNet.ScriptEngines.Global.SdkModule;

/// <summary>
/// File system utilities for script execution.
/// Provides safe file system operations with proper security checks.
/// </summary>
internal static class FileSystemUtils {
    private sealed class CopyProgressState {
        internal long Processed;
    }

    internal static bool PathExists(string path) => System.IO.Path.Exists(path: path);

    internal static bool PathExistsIncludingLinks(string path) {
        if (PathExists(path: path)) {
            return true;
        }

        try {
            System.IO.FileSystemInfo info = GetInfo(path: path);
            return info.Exists || info.LinkTarget != null;
        } catch (Exception ex) {
            Shared.IO.Diagnostics.Bug("[FileSystemUtils] path_exists_including_links catch triggered for path: " + path + " with exception: " + ex);
            Shared.IO.Diagnostics.LuaInternalCatch(ex: "path_exists_including_links failed for path: " + path + " with exception: " + ex);
            return false;
        }
    }

    internal static bool IsSymlink(string path) {
        try {
            System.IO.FileSystemInfo info = GetInfo(path: path);
            return info.LinkTarget != null || info.Attributes.HasFlag(flag: System.IO.FileAttributes.ReparsePoint);
        } catch (Exception ex) {
            Shared.IO.Diagnostics.LuaInternalCatch(ex: "is_symlink failed for path: " + path + " with exception: " + ex);
            return false;
        }
    }

    internal static string? RealPath(string path) {
        try {
            return System.IO.Path.GetFullPath(path: path);
        } catch (Exception ex) {
            Shared.IO.Diagnostics.LuaInternalCatch(ex: "real_path failed for path: " + path + " with exception: " + ex);
            return null;
        }
    }

    internal static string? ReadLink(string path) {
        try {
            System.IO.FileSystemInfo info = GetInfo(path: path);
            return info.LinkTarget;
        } catch (Exception ex) {
            Shared.IO.Diagnostics.LuaInternalCatch(ex: "read_link failed for path: " + path + " with exception: " + ex);
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

        if (!System.IO.Directory.Exists(path: sourceDir)) {
            throw new System.IO.DirectoryNotFoundException($"Source not found: {sourceDir}");
        }

        if (System.IO.Directory.Exists(path: destDir)) {
            if (!overwrite) {
                throw new System.IO.IOException($"Destination already exists: {destDir}");
            }
            // We'll merge by copy then delete source
            Shared.IO.UI.EngineSdk.Print($"Merging '{sourceDir}' into existing '{destDir}'...");
            CopyDirectory(sourceDir: sourceDir, destDir: destDir, overwrite: true, progressLabel: $"Merging {sourceDir} to {destDir}...");
            Shared.IO.UI.EngineSdk.Print("Deleting source after merge...");
            System.IO.Directory.Delete(path: sourceDir, recursive: true);
            Shared.IO.UI.EngineSdk.Print("Move complete.");
            return;
        }

        try {
            Shared.IO.UI.EngineSdk.Print($"Moving directory '{sourceDir}' -> '{destDir}' (fast move) ...", newline: false);
            System.IO.Directory.Move(sourceDirName: sourceDir, destDirName: destDir);
            Shared.IO.UI.EngineSdk.Print(" done.", newline: true);
        } catch {
            // Fallback to copy+delete for cross-device moves
            Shared.IO.UI.EngineSdk.Print("Fast move not available; falling back to copy...", newline: true);
            CopyDirectory(sourceDir: sourceDir, destDir: destDir, overwrite: true, progressLabel: $"Moving {sourceDir} to {destDir}...");
            Shared.IO.UI.EngineSdk.Print("Deleting source after copy...", newline: true);
            System.IO.Directory.Delete(path: sourceDir, recursive: true);
            Shared.IO.UI.EngineSdk.Print("Move complete.");
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

        if (!System.IO.Directory.Exists(path: sourceDir)) {
            throw new System.IO.DirectoryNotFoundException($"Source not found: {sourceDir}");
        }

        if (System.IO.Directory.Exists(path: destDir)) {
            if (!overwrite) {
                throw new System.IO.IOException($"Destination already exists: {destDir}");
            }
        } else {
            System.IO.Directory.CreateDirectory(path: destDir);
        }

        string srcRoot = System.IO.Path.GetFullPath(path: sourceDir);
        string dstRoot = System.IO.Path.GetFullPath(path: destDir);

        // Create all directories first
        foreach (string target in System.IO.Directory.EnumerateDirectories(path: srcRoot, searchPattern: "*", searchOption: System.IO.SearchOption.AllDirectories).Select(selector: dir => System.IO.Path.Combine(path1: dstRoot, path2: System.IO.Path.GetRelativePath(relativeTo: srcRoot, path: dir)))) {
            System.IO.Directory.CreateDirectory(path: target);
        }

        // Prepare files list to compute progress
        List<string> files = System.IO.Directory.EnumerateFiles(path: srcRoot, searchPattern: "*", searchOption: System.IO.SearchOption.AllDirectories).ToList();
        long total = files.Count;
        //int current = 0;

        CopyProgressState? progressState = total > 0 ? new CopyProgressState() : null;
        System.Threading.CancellationTokenSource? progressCts = null;
        System.Threading.Tasks.Task? progressTask = null;

        // Write initial line
        if (total > 0) {
            Shared.IO.UI.EngineSdk.Print($"Copying {total} files from '{srcRoot}' to '{dstRoot}'...");
            progressCts = new System.Threading.CancellationTokenSource();
            progressTask = Shared.IO.UI.EngineSdk.SdkConsoleProgress.StartPanel(
                total: () => total,
                snapshot: () => {
                    long processed = System.Threading.Volatile.Read(location: ref progressState!.Processed);
                    int ok = processed > int.MaxValue ? int.MaxValue : (int)processed;
                    return (processed, ok, 0, 0);
                },
                activeSnapshot: () => new List<Shared.IO.UI.EngineSdk.SdkConsoleProgress.ActiveProcess>(),
                label: () => progressLabel ?? $"Copying {total} files...",
                token: progressCts.Token,
                id: "fs_copy"
            );
        }

        try {
            // Copy files with progress
            foreach (var item in files.Select(selector: file => (File: file, Target: System.IO.Path.Combine(path1: dstRoot, path2: System.IO.Path.GetRelativePath(relativeTo: srcRoot, path: file))))) {
                System.IO.Directory.CreateDirectory(path: System.IO.Path.GetDirectoryName(path: item.Target)!);
                System.IO.File.Copy(sourceFileName: item.File, destFileName: item.Target, overwrite: true);

                //current++;
                if (progressState != null) {
                    System.Threading.Interlocked.Increment(location: ref progressState.Processed);
                }
            }
        } finally {
            if (progressCts != null && progressTask != null) {
                try {
                    if (!progressCts.IsCancellationRequested) {
                        progressCts.Cancel();
                    }

                    try {
                        progressTask.Wait();
                    } catch (System.AggregateException ex) {
                        Shared.IO.Diagnostics.Bug("[FileSystemUtils::CopyDirectory()] Progress task wait failed.", ex: ex);
                        /* ignore */
                    } catch (System.ObjectDisposedException ex) {
                        Shared.IO.Diagnostics.Bug("[FileSystemUtils::CopyDirectory()] Progress task disposed while waiting.", ex: ex);
                        /* ignore */
                    } catch (System.InvalidOperationException ex) {
                        Shared.IO.Diagnostics.Bug("[FileSystemUtils::CopyDirectory()] Progress task wait failed with invalid state.", ex: ex);
                        /* ignore */
                    }
                } finally {
                    progressCts.Dispose();
                }
            }
        }
    }


    /// <summary>
    /// Returns the full path to a direct child subdirectory of <paramref name="baseDir"/>
    /// named <paramref name="name"/>. Comparison is case-insensitive on Windows.
    /// Returns null if not found.
    /// </summary>
    internal static string? FindSubdir(string baseDir, string name, bool caseInsensitive = true) {
        if (!System.IO.Directory.Exists(path: baseDir)) {
            return null;
        }

        System.StringComparer cmp = caseInsensitive ? System.StringComparer.OrdinalIgnoreCase : System.StringComparer.Ordinal;
        try {
            foreach (string d in System.IO.Directory.EnumerateDirectories(path: baseDir)) {
                string dn = new System.IO.DirectoryInfo(path: d).Name;
                if (cmp.Equals(x: dn, y: name)) {
                    return d;
                }
            }
        } catch {
            Shared.IO.Diagnostics.Bug($"[ConfigHelpers] Failed to enumerate directories under '{baseDir}'");
        }
        return null;
    }

    /// <summary>
    /// Checks whether all subdirectory names in <paramref name="names"/> exist directly under <paramref name="baseDir"/>.
    /// Comparison is case-insensitive on Windows by default.
    /// </summary>
    internal static bool HasAllSubdirs(string baseDir, IEnumerable<string> names, bool caseInsensitive = true) {
        if (!System.IO.Directory.Exists(path: baseDir)) {
            return false;
        }

        System.StringComparer cmp = caseInsensitive ? System.StringComparer.OrdinalIgnoreCase : System.StringComparer.Ordinal;
        HashSet<string> existing;
        try {
            existing = System.IO.Directory.EnumerateDirectories(path: baseDir)
                .Select(selector: d => new System.IO.DirectoryInfo(path: d).Name)
                .ToHashSet(comparer: cmp);
        } catch {
            return false;
        }
        foreach (string n in names) {
            if (string.IsNullOrWhiteSpace(n)) {
                continue;
            }

            if (!existing.Contains(item: n)) {
                return false;
            }
        }
        return true;
    }


    private static System.IO.FileSystemInfo GetInfo(string path) {
        string full = System.IO.Path.GetFullPath(path: path);
        System.IO.DirectoryInfo dirInfo = new System.IO.DirectoryInfo(path: full);
        if (dirInfo.Exists) {
            return dirInfo;
        }

        System.IO.FileInfo fileInfo = new System.IO.FileInfo(fileName: full);
        if (fileInfo.Exists) {
            return fileInfo;
        }
        // Determine based on trailing separator
        return full.EndsWith(System.IO.Path.DirectorySeparatorChar) || full.EndsWith(System.IO.Path.AltDirectorySeparatorChar)
            ? new System.IO.DirectoryInfo(path: full.TrimEnd(trimChars: [System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar]))
            : fileInfo;
    }
}
