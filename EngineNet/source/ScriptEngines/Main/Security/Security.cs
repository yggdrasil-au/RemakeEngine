namespace EngineNet.ScriptEngines;

/// <summary>
/// Security validation methods for script execution, for Lua, JS, and Python scripts from external Game modules.
/// Ensures that scripts from external modules adhere to security boundaries and user-approved paths.
/// This includes verifying that paths are within allowed boundaries and resolving symbolic links securely.
/// </summary>
internal static class Security {
    // Use a ConcurrentDictionary to act as a thread-safe HashSet
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> UserApprovedRoots = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the canonical full path for the specified path, resolving any symbolic links and cleaning path prefixes.
    /// </summary>
    /// <param name="path">The input file or directory path.</param>
    /// <returns>The canonical full path with symbolic links resolved and path prefixes cleaned.</returns>
    private static string GetCanonicalFullPath(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return string.Empty;
        }

        try {
            string fullPath = System.IO.Path.GetFullPath(path: path);
            return CleanPathPrefix(path: fullPath);
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug("catch triggered for path: " + path + " with exception: " + ex);
            return CleanPathPrefix(path: path).Replace(oldChar: '/', newChar: System.IO.Path.DirectorySeparatorChar);
        }
    }

    /// <summary>
    /// Resolves the canonical path for the specified file or directory, including resolving symbolic links.
    /// </summary>
    /// <param name="path">The input file or directory path.</param>
    /// <returns>The canonical full path with symbolic links resolved, or the original canonical path if resolution fails.</returns>
    private static string ResolveCanonicalPathForIo(string path) {
        string canonicalPath = GetCanonicalFullPath(path: path);
        if (string.IsNullOrWhiteSpace(canonicalPath)) {
            return canonicalPath;
        }

        try {
            if (System.IO.File.Exists(path: canonicalPath)) {
                System.IO.FileInfo fileInfo = new(fileName: canonicalPath);
                System.IO.FileSystemInfo? fileTarget = fileInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (fileTarget != null) {
                    return GetCanonicalFullPath(path: fileTarget.FullName);
                }
            }

            string? check = canonicalPath;
            string? root = System.IO.Path.GetPathRoot(path: check);

            while (!string.IsNullOrEmpty(check) && !string.Equals(a: check, b: root, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                if (System.IO.Directory.Exists(path: check)) {
                    System.IO.DirectoryInfo info = new(path: check);
                    System.IO.FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: true);
                    if (target != null) {
                        string targetPath = GetCanonicalFullPath(path: target.FullName);
                        string suffix = canonicalPath.Length > check.Length
                            ? canonicalPath.Substring(startIndex: check.Length)
                            : string.Empty;
                        return GetCanonicalFullPath(path: targetPath + suffix);
                    }
                }

                check = System.IO.Path.GetDirectoryName(path: check);
            }
        } catch {
            // Keep the non-resolved canonical path if link resolution fails.
        }

        return canonicalPath;
    }

    /// <summary>
    /// Cleans the path prefix for the specified path, removing Win32 long path prefixes if present.
    /// </summary>
    /// <param name="path">The input file or directory path.</param>
    /// <returns>The path with the Win32 long path prefix removed, if it was present.</returns>
    private static string CleanPathPrefix(string path) {
        if (string.IsNullOrWhiteSpace(path)) return path;
        // Strip Win32 long path prefix if present (\\?\ and \\?\UNC\)
        if (!path.StartsWith(@"\\?\", comparisonType: StringComparison.Ordinal)) return path;
        if (path.StartsWith(@"\\?\UNC\", comparisonType: StringComparison.OrdinalIgnoreCase)) {
            return @"\\" + path.Substring(startIndex: 8);
        }
        return path.Substring(startIndex: 4);
    }

    /// <summary>
    /// Normalizes the specified path to its full canonical form and converts it to lowercase.
    /// </summary>
    /// <param name="path">The input file or directory path.</param>
    /// <returns>The normalized full path in lowercase.</returns>
    private static string NormalizeLowerFullPath(string path) {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try {
            string fullPath = System.IO.Path.GetFullPath(path: path);
            return CleanPathPrefix(path: fullPath).ToLowerInvariant();
        } catch {
            return CleanPathPrefix(path: path).Replace(oldChar: '/', newChar: System.IO.Path.DirectorySeparatorChar).ToLowerInvariant();
        }
    }

    /// <summary>
    /// Determines the root directory for approval based on the specified path.
    /// </summary>
    /// <param name="path">The input file or directory path.</param>
    /// <returns>The root directory path that should be used for approval checks.</returns>
    private static string DetermineApprovalRoot(string path) {
        try {
            string full = System.IO.Path.GetFullPath(path: path);
            string? rootResult = full;
            if (System.IO.Directory.Exists(path: full)) {
                rootResult = new System.IO.DirectoryInfo(path: full).FullName;
            } else {
                string? dir = System.IO.Path.GetDirectoryName(path: full);
                if (!string.IsNullOrWhiteSpace(dir)) {
                    rootResult = new System.IO.DirectoryInfo(path: dir).FullName;
                }
            }
            return CleanPathPrefix(path: rootResult ?? full);
        } catch {
            Shared.IO.Diagnostics.Bug("DetermineApprovalRoot: Failed to determine root for path: " + path);
            return CleanPathPrefix(path: path);
        }
    }

    /// <summary>
    /// Attempts to get the allowed canonical path for the specified path, prompting the user for permission if necessary.
    /// </summary>
    /// <param name="path">The file or directory path to check.</param>
    /// <param name="canonicalPath">The resulting allowed canonical path if access is granted.</param>
    /// <returns>True if access is allowed and the canonical path is obtained; otherwise, false.</returns>
    internal static bool TryGetAllowedCanonicalPathWithPrompt(string path, out string canonicalPath) {
        if (TryGetAllowedCanonicalPath(path: path, canonicalPath: out canonicalPath)) {
            return true;
        }

        // Ask the user for permission to grant temporary access to this external path
        string root = DetermineApprovalRoot(path: path);
        string msg = $"Permission requested: Allow this script to access external path '\"{root}\"'?";

        bool allowed = Shared.IO.UI.EngineSdk.Confirm(msg, id: "ext_path_access", defaultValue: false);

        if (allowed) {
            try {
                string normalized = NormalizeLowerFullPath(path: root).TrimEnd(trimChar: System.IO.Path.DirectorySeparatorChar);
                UserApprovedRoots.TryAdd(normalized, 1);
            } catch {
                Shared.IO.Diagnostics.Bug("Failed to normalize and approve path: " + root);
            }

            if (TryGetAllowedCanonicalPath(path: path, canonicalPath: out canonicalPath)) {
                return true;
            }

            canonicalPath = ResolveCanonicalPathForIo(path: path);
            return !string.IsNullOrWhiteSpace(canonicalPath);
        }

        canonicalPath = string.Empty;
        Shared.IO.UI.EngineSdk.Error($"Access denied: File path '{path}' is outside allowed workspace areas");
        return false;
    }

    /// <summary>
    /// Security validation: Check if executable is approved for RemakeEngine use.
    /// Allows installed lockfile tools and a limited set of system utilities.
    /// </summary>
    internal static bool IsApprovedExecutable(string executable, Core.Abstractions.IJsonToolResolver tools) {
        // deny invalid input, such as null, empty, whitespace strings, or invalid characters that cannot be in a file path for any OS
        if (string.IsNullOrWhiteSpace(executable) || executable.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0) {
            return false;
        }

        // allow tracked tools
        if (tools.IsTrackedTool(executablePath: executable)) {
            return true;
        }

        return false; // simply deny all non-tracked tools
    }

    /// <summary>
    /// Security validation: Check if file path is within allowed workspace areas.
    /// Prevents access to sensitive system files while allowing game asset processing.
    /// </summary>
    internal static bool TryGetAllowedCanonicalPath(string path, out string canonicalPath) {
        canonicalPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path)) {
            return false;
        }

        try {
            string currentDir = System.IO.Directory.GetCurrentDirectory();
            string projectRoot = string.IsNullOrWhiteSpace(EngineNet.Shared.State.RootPath)
                ? currentDir
                : EngineNet.Shared.State.RootPath;

            // 1. Force absolute resolution IMMEDIATELY to resolve any "../" traversals.
            // If 'path' is relative, it resolves against 'projectRoot'.
            string fullPath = System.IO.Path.GetFullPath(path: path, basePath: projectRoot);

            // 2. Resolve symlinks using your existing canonical resolver
            string resolvedPath = ResolveCanonicalPathForIo(path: fullPath);

            // Fallback to the absolute path if symlink resolution yields nothing
            if (string.IsNullOrWhiteSpace(resolvedPath)) {
                resolvedPath = fullPath;
            }

            // 3. Define definitive allowed boundaries
            string[] allowedRoots = {
                currentDir,
                System.IO.Path.Combine(path1: projectRoot, path2: "EngineApps", path3: "Games"),
                System.IO.Path.Combine(path1: projectRoot, path2: "gamefiles"),
                System.IO.Path.Combine(projectRoot, "tools"),
                System.IO.Path.Combine(projectRoot, "tmp")
            };

            // 4. Check static boundaries
            foreach (string allowedPattern in allowedRoots) {
                if (IsPathSafelyWithinRoot(targetPath: resolvedPath, rootPath: allowedPattern)) {
                    canonicalPath = resolvedPath;
                    return true;
                }
            }

            // 5. Check dynamically approved roots
            foreach (string approvedRoot in UserApprovedRoots.Keys) {
                if (IsPathSafelyWithinRoot(targetPath: resolvedPath, rootPath: approvedRoot)) {
                    canonicalPath = resolvedPath;
                    return true;
                }
            }

            // Default Deny
            Shared.IO.Diagnostics.Trace($"Path '{resolvedPath}' is outside all allowed boundaries.");
            return false;

        } catch (Exception ex) {
            Shared.IO.Diagnostics.Bug("IsAllowedPath: Failed to check path: " + path + " with exception: " + ex);
            return false; // Fail secure on any parsing errors
        }
    }

    /// <summary>
    /// Guarantees a target path is a child of the root path using native OS relative path resolution.
    /// </summary>
    private static bool IsPathSafelyWithinRoot(string targetPath, string rootPath) {
        try {
            string fullRoot = System.IO.Path.GetFullPath(path: rootPath);
            string relativePath = System.IO.Path.GetRelativePath(relativeTo: fullRoot, path: targetPath);

            // If the path needs to traverse UP ("..") to reach the target, it's outside the boundary.
            // If it evaluates to a rooted path (e.g. "D:\folder" when relative to "C:\"), it's on a different drive.
            return !relativePath.StartsWith("..", StringComparison.Ordinal) && !System.IO.Path.IsPathRooted(path: relativePath);
        } catch {
            return false;
        }
    }

    /// <summary>
    /// Determines whether the specified path is allowed based on the defined security boundaries.
    /// </summary>
    /// <param name="path">The input file or directory path to check.</param>
    /// <returns>True if the path is allowed, false otherwise.</returns>
    internal static bool IsAllowedPath(string path) {
        return TryGetAllowedCanonicalPath(path: path, canonicalPath: out _);
    }
}
