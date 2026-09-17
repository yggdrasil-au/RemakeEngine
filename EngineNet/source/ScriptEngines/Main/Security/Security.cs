namespace EngineNet.ScriptEngines;

/// <summary>
/// Security validation methods for script execution.
/// Provides path validation and executable approval for RemakeEngine security.
/// </summary>
internal static class Security {
    private static readonly HashSet<string> UserApprovedRoots = new(comparer: System.StringComparer.OrdinalIgnoreCase);

    private static bool IsPathWithinBoundary(string normalizedPath, string normalizedPattern) {
        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(normalizedPattern)) {
            return false;
        }

        char sep = System.IO.Path.DirectorySeparatorChar;
        char altSep = System.IO.Path.AltDirectorySeparatorChar;

        string pathValue = normalizedPath.TrimEnd(trimChars: [sep, altSep]);
        string patternValue = normalizedPattern.TrimEnd(trimChars: [sep, altSep]);

        if (pathValue.Equals(patternValue, comparisonType: StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        string patternWithSeparator = patternValue + sep;
        return pathValue.StartsWith(patternWithSeparator, comparisonType: StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeBoundaryPattern(string pathPattern) {
        if (string.IsNullOrWhiteSpace(pathPattern)) {
            return string.Empty;
        }

        return CleanPathPrefix(path: pathPattern)
            .Replace(oldChar: '/', newChar: System.IO.Path.DirectorySeparatorChar)
            .ToLowerInvariant()
            .TrimEnd(trimChars: [System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar]);
    }

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

    private static string CleanPathPrefix(string path) {
        if (string.IsNullOrWhiteSpace(path)) return path;
        // Strip Win32 long path prefix if present (\\?\ and \\?\UNC\)
        if (!path.StartsWith(@"\\?\", comparisonType: StringComparison.Ordinal)) return path;
        if (path.StartsWith(@"\\?\UNC\", comparisonType: StringComparison.OrdinalIgnoreCase)) {
            return @"\\" + path.Substring(startIndex: 8);
        }
        return path.Substring(startIndex: 4);
    }

    private static string NormalizeLowerFullPath(string path) {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try {
            string fullPath = System.IO.Path.GetFullPath(path: path);
            return CleanPathPrefix(path: fullPath).ToLowerInvariant();
        } catch {
            return CleanPathPrefix(path: path).Replace(oldChar: '/', newChar: System.IO.Path.DirectorySeparatorChar).ToLowerInvariant();
        }
    }

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

    private static bool IsForbiddenPath(string normalizedPath) {
        if (string.IsNullOrWhiteSpace(normalizedPath)) return false;

        string currentDir = NormalizeLowerFullPath(path: System.IO.Directory.GetCurrentDirectory());
        string projectRoot = string.IsNullOrWhiteSpace(EngineNet.Shared.State.RootPath)
            ? currentDir
            : NormalizeLowerFullPath(path: EngineNet.Shared.State.RootPath);

        List<string> forbiddenPatterns = new()
        {
            "/etc", "/bin", "/sbin",
            System.IO.Path.Combine(path1: "/usr", path2: "bin"),
            System.IO.Path.Combine(path1: "/usr", path2: "sbin"),
            "/sys", "/proc", "/dev",
            // Explicitly deny access to Engine Files to prevent tampering
            System.IO.Path.Combine(path1: projectRoot, path2: "EngineApps", path3: "Registries").Replace(oldChar: '/', newChar: System.IO.Path.DirectorySeparatorChar).ToLowerInvariant(),
            System.IO.Path.Combine(path1: projectRoot, path2: "EngineApps", path3: "api_definitions").Replace(oldChar: '/', newChar: System.IO.Path.DirectorySeparatorChar).ToLowerInvariant(),
            //System.IO.Path.Combine(projectRoot, "EngineApps", "Tools").Replace('/', System.IO.Path.DirectorySeparatorChar).ToLowerInvariant(),
            // if the script is running from Source, also deny access to EngineNet source to prevent tampering
            System.IO.Path.Combine(path1: projectRoot, path2: "EngineNet").Replace(oldChar: '/', newChar: System.IO.Path.DirectorySeparatorChar).ToLowerInvariant(),
        };

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(osPlatform: System.Runtime.InteropServices.OSPlatform.Windows)) {
            forbiddenPatterns.Add(item: System.Environment.GetFolderPath(folder: System.Environment.SpecialFolder.Windows).ToLowerInvariant());
            forbiddenPatterns.Add(item: System.Environment.GetFolderPath(folder: System.Environment.SpecialFolder.System).ToLowerInvariant());
            forbiddenPatterns.Add(item: System.Environment.GetFolderPath(folder: System.Environment.SpecialFolder.SystemX86).ToLowerInvariant());
            forbiddenPatterns.Add(item: System.Environment.GetFolderPath(folder: System.Environment.SpecialFolder.ProgramFiles).ToLowerInvariant());
            forbiddenPatterns.Add(item: System.Environment.GetFolderPath(folder: System.Environment.SpecialFolder.ProgramFilesX86).ToLowerInvariant());
            forbiddenPatterns.Add(item: System.Environment.GetFolderPath(folder: System.Environment.SpecialFolder.CommonApplicationData).ToLowerInvariant());
        }

        return (from forbiddenPattern in forbiddenPatterns where !string.IsNullOrWhiteSpace(forbiddenPattern) select NormalizeBoundaryPattern(pathPattern: forbiddenPattern)).Any(predicate: normalizedForbidden => IsPathWithinBoundary(normalizedPath: normalizedPath, normalizedPattern: normalizedForbidden));
    }

    internal static bool TryGetAllowedCanonicalPathWithPrompt(string path, out string canonicalPath) {
        if (TryGetAllowedCanonicalPath(path: path, canonicalPath: out canonicalPath)) {
            return true;
        }

        if (IsForbiddenPath(normalizedPath: NormalizeLowerFullPath(path: path))) {
            canonicalPath = string.Empty;
            Shared.IO.UI.EngineSdk.Error($"Access denied: File path '{path}' is a protected system or engine path");
            return false;
        }

        // Ask the user for permission to grant temporary access to this external path
        string root = DetermineApprovalRoot(path: path);
        string msg = $"Permission requested: Allow this script to access external path '\"{root}\"'?";

        bool allowed = Shared.IO.UI.EngineSdk.Confirm(msg, id: "ext_path_access", defaultValue: false);

        if (allowed) {
            try {
                string normalized = NormalizeLowerFullPath(path: root).TrimEnd(trimChar: System.IO.Path.DirectorySeparatorChar);
                UserApprovedRoots.Add(item: normalized);
            } catch {
                Shared.IO.Diagnostics.Bug("Failed to normalize and approve path: " + root);
                /* ignore */
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
    /// Allows registered tools, common system utilities, and resolved tool paths.
    /// </summary>
    internal static bool IsApprovedExecutable(string executable, Core.ExternalTools.JsonToolResolver tools) {
        if (string.IsNullOrWhiteSpace(executable)) {
            return false;
        }

        // Normalize executable name (remove path and extension for comparison)
        string exeName = System.IO.Path.GetFileNameWithoutExtension(path: executable).ToLowerInvariant();
        string fullName = System.IO.Path.GetFileName(path: executable).ToLowerInvariant();

        // Allow resolved tool paths (tools that came from tool() function)
        try {
            string resolvedPath = tools.ResolveToolPath(toolId: exeName);
            if (!string.IsNullOrEmpty(resolvedPath) &&
                (executable.Equals(resolvedPath, comparisonType: System.StringComparison.OrdinalIgnoreCase) ||
                 executable.EndsWith(resolvedPath, comparisonType: System.StringComparison.OrdinalIgnoreCase))) {
                return true;
            }
        } catch (Exception ex) {
            Shared.IO.Diagnostics.Trace("Tool resolution failed for: " + exeName + " with exception: " + ex);
            /* Tool resolution may fail, continue with other checks */
        }

        // Approved RemakeEngine tools (case-insensitive)
        HashSet<string> approvedTools = new(comparer: System.StringComparer.OrdinalIgnoreCase) {
            // Core RemakeEngine tools from "EngineApps", "Registries", "Tools", "Main.json", TODO: resolve dynamically
            "blender", "blender.exe", "blender-launcher.exe",
            "quickbms", "quickbms.exe",
            "godot", "godot.exe",
            "vgmstream-cli", "vgmstream-cli.exe",
            "ffmpeg", "ffmpeg.exe",

            // Git (for repository operations)
            "git", "git.exe",

            // PowerShell/cmd (very limited - only for specific safe operations)
            // Note: These require additional argument validation
            "pwsh", "pwsh.exe", "powershell", "powershell.exe",

        };

        // Check both with and without common extensions
        if (approvedTools.Contains(item: exeName) || approvedTools.Contains(item: fullName)) {
            return true;
        }

        // Allow executables that are in the Tools directory structure
        if (executable.Contains("Tools", comparisonType: System.StringComparison.OrdinalIgnoreCase) &&
            (executable.Contains("Blender", comparisonType: System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("QuickBMS", comparisonType: System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("Godot", comparisonType: System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("vgmstream", comparisonType: System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("ffmpeg", comparisonType: System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("ImageMagick", comparisonType: System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("Lucas_Radcore_Cement_Library_Builder", comparisonType: System.StringComparison.OrdinalIgnoreCase))) {
            return true;
        } else if (executable.Contains("Tools", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
            Shared.IO.Diagnostics.Log($"Allowing executable in Tools directory: {executable}");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Security validation: Check if file path is within allowed workspace areas.
    /// Prevents access to sensitive system files while allowing game asset processing.
    /// </summary>
    internal static bool TryGetAllowedCanonicalPath(string path, out string canonicalPath) {
        canonicalPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path)) {
            //Shared.IO.Diagnostics.Trace("Denying access to empty or whitespace path");
            return false;
        }

        try {
            string normalizedPath = NormalizeLowerFullPath(path: path);
            
            // Deny explicitly forbidden paths immediately
            if (IsForbiddenPath(normalizedPath: normalizedPath)) {
                Shared.IO.Diagnostics.Trace($"Path '{normalizedPath}' is forbidden");
                return false;
            }

            string fullPath = GetCanonicalFullPath(path: path);
            //Shared.IO.Diagnostics.Trace($"Checking path '{fullPath}'");
            //Shared.IO.Diagnostics.Trace($"Normalized path '{normalizedPath}'");

            // First, allow any user-approved roots for this session
            foreach (string approved in UserApprovedRoots) {
                if (IsPathWithinBoundary(normalizedPath: normalizedPath, normalizedPattern: approved)) {
                    canonicalPath = ResolveCanonicalPathForIo(path: fullPath);
                    return true;
                }
            }

            // Get current working directory and common workspace patterns
            string currentDir = NormalizeLowerFullPath(path: System.IO.Directory.GetCurrentDirectory());
            string projectRoot = string.IsNullOrWhiteSpace(EngineNet.Shared.State.RootPath)
                ? currentDir
                : NormalizeLowerFullPath(path: EngineNet.Shared.State.RootPath);

            //Shared.IO.Diagnostics.Trace($"Current directory '{currentDir}'");
            //Shared.IO.Diagnostics.Trace($"Project root '{projectRoot}'");

            // Allowed path patterns (case-insensitive)
            // Note: We check full path starts with these patterns to allow subdirectories,
            // but we also check for exact match to allow files directly in these directories
            string[] allowedPatterns = {
                // Current workspace and subdirectories
                currentDir,

                // must allow access to EngineApps/Games/** for game asset processing
                // but not EngineApps/Registries or EngineApps/Tools to prevent tampering with engine files
                System.IO.Path.Combine(path1: projectRoot, path2: "EngineApps", path3: "Games"),

                // allow random items
                System.IO.Path.Combine(path1: projectRoot, path2: "gamefiles"),
                System.IO.Path.Combine(path1: projectRoot, path2: "tools"),
                System.IO.Path.Combine(path1: projectRoot, path2: "tmp"),
            };

            // Allow if path starts with any allowed pattern
            foreach (string allowedPattern in allowedPatterns) {
                if (IsPathWithinBoundary(normalizedPath: normalizedPath, normalizedPattern: allowedPattern)) {
                    //Shared.IO.Diagnostics.Trace($"Path '{normalizedPath}' starts with allowed pattern '{allowedPattern}'");
                    canonicalPath = ResolveCanonicalPathForIo(path: fullPath);
                    return true;
                } else {
                    Shared.IO.Diagnostics.Trace($"Path '{normalizedPath}' does not start with allowed pattern '{allowedPattern}'");
                }
            }

            // Check if the path itself or any of its parents are symlinks that resolve to an allowed path
            try {
                string? check = fullPath;
                string? root = System.IO.Path.GetPathRoot(path: check);
                Shared.IO.Diagnostics.Trace($"Checking symlinks for path '{check}'");

                while (!string.IsNullOrEmpty(check) && !string.Equals(a: check, b: root, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                    if (System.IO.Directory.Exists(path: check)) {
                        DirectoryInfo info = new(path: check);
                        FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: true); // true = return final target
                        if (target != null) {
                            string targetPath = target.FullName;
                            string suffix = "";
                            if (fullPath.Length > check.Length) {
                                suffix = fullPath.Substring(startIndex: check.Length);
                            }

                            string resolvedFullPath = targetPath + suffix;
                            string normalizedResolved = NormalizeLowerFullPath(path: resolvedFullPath);

                            foreach (string allowedPattern in allowedPatterns) {
                                if (IsPathWithinBoundary(normalizedPath: normalizedResolved, normalizedPattern: allowedPattern)) {
                                    canonicalPath = GetCanonicalFullPath(path: resolvedFullPath);
                                    return true;
                                }
                            }
                        }
                    }
                    check = System.IO.Path.GetDirectoryName(path: check);
                }
            } catch (Exception ex) {
                /* ignore */
                Shared.IO.Diagnostics.Bug("IsAllowedPath: Failed to resolve symlink for path: " + fullPath + " with exception: " + ex);
            }

            // Additional check: allow relative paths within current directory
            if (!System.IO.Path.IsPathRooted(path: path)) {
                return true; // Relative paths are generally safe within workspace
            }

            return false; // Default deny for unrecognized absolute paths
        } catch (Exception ex) {
            Shared.IO.Diagnostics.Bug("IsAllowedPath: Failed to check path: " + path + " with exception: " + ex);
            return false; // Path parsing errors = deny
        }
    }

    internal static bool IsAllowedPath(string path) {
        return TryGetAllowedCanonicalPath(path: path, canonicalPath: out _);
    }
}
