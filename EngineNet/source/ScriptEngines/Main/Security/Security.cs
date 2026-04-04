namespace EngineNet.ScriptEngines;

/// <summary>
/// Security validation methods for script execution.
/// Provides path validation and executable approval for RemakeEngine security.
/// </summary>
internal static class Security {
    private static readonly HashSet<string> UserApprovedRoots = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

    private static bool IsPathWithinBoundary(string normalizedPath, string normalizedPattern) {
        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(normalizedPattern)) {
            return false;
        }

        char sep = System.IO.Path.DirectorySeparatorChar;
        char altSep = System.IO.Path.AltDirectorySeparatorChar;

        string pathValue = normalizedPath.TrimEnd(sep, altSep);
        string patternValue = normalizedPattern.TrimEnd(sep, altSep);

        if (pathValue.Equals(patternValue, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        string patternWithSeparator = patternValue + sep;
        return pathValue.StartsWith(patternWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeBoundaryPattern(string pathPattern) {
        if (string.IsNullOrWhiteSpace(pathPattern)) {
            return string.Empty;
        }

        return CleanPathPrefix(pathPattern)
            .Replace('/', System.IO.Path.DirectorySeparatorChar)
            .ToLowerInvariant()
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    }

    private static string GetCanonicalFullPath(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return string.Empty;
        }

        try {
            string fullPath = System.IO.Path.GetFullPath(path);
            return CleanPathPrefix(fullPath);
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug("[Security.cs::GetCanonicalFullPath()] catch triggered for path: " + path + " with exception: " + ex);
            return CleanPathPrefix(path).Replace('/', System.IO.Path.DirectorySeparatorChar);
        }
    }

    private static string ResolveCanonicalPathForIo(string path) {
        string canonicalPath = GetCanonicalFullPath(path);
        if (string.IsNullOrWhiteSpace(canonicalPath)) {
            return canonicalPath;
        }

        try {
            if (System.IO.File.Exists(canonicalPath)) {
                System.IO.FileInfo fileInfo = new System.IO.FileInfo(canonicalPath);
                System.IO.FileSystemInfo? fileTarget = fileInfo.ResolveLinkTarget(true);
                if (fileTarget != null) {
                    return GetCanonicalFullPath(fileTarget.FullName);
                }
            }

            string? check = canonicalPath;
            string? root = System.IO.Path.GetPathRoot(check);

            while (!string.IsNullOrEmpty(check) && !string.Equals(check, root, System.StringComparison.OrdinalIgnoreCase)) {
                if (System.IO.Directory.Exists(check)) {
                    System.IO.DirectoryInfo info = new System.IO.DirectoryInfo(check);
                    System.IO.FileSystemInfo? target = info.ResolveLinkTarget(true);
                    if (target != null) {
                        string targetPath = GetCanonicalFullPath(target.FullName);
                        string suffix = canonicalPath.Length > check.Length
                            ? canonicalPath.Substring(check.Length)
                            : string.Empty;
                        return GetCanonicalFullPath(targetPath + suffix);
                    }
                }

                check = System.IO.Path.GetDirectoryName(check);
            }
        } catch {
            // Keep the non-resolved canonical path if link resolution fails.
        }

        return canonicalPath;
    }

    private static string CleanPathPrefix(string path) {
        if (string.IsNullOrWhiteSpace(path)) return path;
        // Strip Win32 long path prefix if present (\\?\ and \\?\UNC\)
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) {
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) {
                return @"\\" + path.Substring(8);
            }
            return path.Substring(4);
        }
        return path;
    }

    private static string NormalizeLowerFullPath(string path) {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try {
            string fullPath = System.IO.Path.GetFullPath(path);
            return CleanPathPrefix(fullPath).ToLowerInvariant();
        } catch {
            return CleanPathPrefix(path).Replace('/', System.IO.Path.DirectorySeparatorChar).ToLowerInvariant();
        }
    }

    private static string DetermineApprovalRoot(string path) {
        try {
            string full = System.IO.Path.GetFullPath(path);
            string? rootResult = full;
            if (System.IO.Directory.Exists(full)) {
                rootResult = new System.IO.DirectoryInfo(full).FullName;
            } else {
                string? dir = System.IO.Path.GetDirectoryName(full);
                if (!string.IsNullOrWhiteSpace(dir)) {
                    rootResult = new System.IO.DirectoryInfo(dir).FullName;
                }
            }
            return CleanPathPrefix(rootResult ?? full);
        } catch {
            Shared.IO.Diagnostics.Bug("DetermineApprovalRoot: Failed to determine root for path: " + path);
            return CleanPathPrefix(path);
        }
    }

    private static bool IsForbiddenPath(string normalizedPath) {
        if (string.IsNullOrWhiteSpace(normalizedPath)) return false;

        string currentDir = NormalizeLowerFullPath(System.IO.Directory.GetCurrentDirectory());
        string projectRoot = string.IsNullOrWhiteSpace(EngineNet.Core.Main.RootPath)
            ? currentDir
            : NormalizeLowerFullPath(EngineNet.Core.Main.RootPath);

        List<string> forbiddenPatterns = new List<string> {
            "/etc", "/bin", "/sbin",
            System.IO.Path.Combine("/usr", "bin"),
            System.IO.Path.Combine("/usr", "sbin"),
            "/sys", "/proc", "/dev",
            // Explicitly deny access to Engine Files to prevent tampering
            System.IO.Path.Combine(projectRoot, "EngineApps", "Registries").Replace('/', System.IO.Path.DirectorySeparatorChar).ToLowerInvariant(),
            System.IO.Path.Combine(projectRoot, "EngineApps", "api_definitions").Replace('/', System.IO.Path.DirectorySeparatorChar).ToLowerInvariant(),
            System.IO.Path.Combine(projectRoot, "EngineApps", "Tools").Replace('/', System.IO.Path.DirectorySeparatorChar).ToLowerInvariant(),
            // if the script is running from Source, also deny access to EngineNet source to prevent tampering
            System.IO.Path.Combine(projectRoot, "EngineNet").Replace('/', System.IO.Path.DirectorySeparatorChar).ToLowerInvariant(),
        };

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) {
            forbiddenPatterns.Add(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows).ToLowerInvariant());
            forbiddenPatterns.Add(System.Environment.GetFolderPath(System.Environment.SpecialFolder.System).ToLowerInvariant());
            forbiddenPatterns.Add(System.Environment.GetFolderPath(System.Environment.SpecialFolder.SystemX86).ToLowerInvariant());
            forbiddenPatterns.Add(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles).ToLowerInvariant());
            forbiddenPatterns.Add(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86).ToLowerInvariant());
            forbiddenPatterns.Add(System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData).ToLowerInvariant());
        }

        foreach (string forbiddenPattern in forbiddenPatterns) {
            if (string.IsNullOrWhiteSpace(forbiddenPattern)) continue;
            string normalizedForbidden = NormalizeBoundaryPattern(forbiddenPattern);
            if (IsPathWithinBoundary(normalizedPath, normalizedForbidden)) {
                return true;
            }
        }
        return false;
    }

    internal static bool TryGetAllowedCanonicalPathWithPrompt(string path, out string canonicalPath) {
        if (TryGetAllowedCanonicalPath(path, out canonicalPath)) {
            return true;
        }

        if (IsForbiddenPath(NormalizeLowerFullPath(path))) {
            canonicalPath = string.Empty;
            Shared.IO.UI.EngineSdk.Error($"Access denied: File path '{path}' is a protected system or engine path");
            return false;
        }

        // Ask the user for permission to grant temporary access to this external path
        string root = DetermineApprovalRoot(path);
        string msg = $"Permission requested: Allow this script to access external path '\"{root}\"'?";

        bool allowed = Shared.IO.UI.EngineSdk.Confirm(msg, "ext_path_access", false);

        if (allowed) {
            try {
                string normalized = NormalizeLowerFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar);
                UserApprovedRoots.Add(normalized);
            } catch {
                Shared.IO.Diagnostics.Bug("[Security.cs::EnsurePathAllowedWithPrompt()] Failed to normalize and approve path: " + root);
                /* ignore */
            }

            if (TryGetAllowedCanonicalPath(path, out canonicalPath)) {
                return true;
            }

            canonicalPath = ResolveCanonicalPathForIo(path);
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
        string exeName = System.IO.Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
        string fullName = System.IO.Path.GetFileName(executable).ToLowerInvariant();

        // Allow resolved tool paths (tools that came from tool() function)
        try {
            string resolvedPath = tools.ResolveToolPath(exeName);
            if (!string.IsNullOrEmpty(resolvedPath) &&
                (executable.Equals(resolvedPath, System.StringComparison.OrdinalIgnoreCase) ||
                 executable.EndsWith(resolvedPath, System.StringComparison.OrdinalIgnoreCase))) {
                return true;
            }
        } catch (Exception ex) {
            Shared.IO.Diagnostics.Trace("[Security.cs::IsApprovedExecutable()] Tool resolution failed for: " + exeName + " with exception: " + ex);
            /* Tool resolution may fail, continue with other checks */
        }

        // Approved RemakeEngine tools (case-insensitive)
        HashSet<string> approvedTools = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) {
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
        if (approvedTools.Contains(exeName) || approvedTools.Contains(fullName)) {
            return true;
        }

        // Allow executables that are in the Tools directory structure
        if (executable.Contains("Tools", System.StringComparison.OrdinalIgnoreCase) &&
            (executable.Contains("Blender", System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("QuickBMS", System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("Godot", System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("vgmstream", System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("ffmpeg", System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("ImageMagick", System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("Lucas_Radcore_Cement_Library_Builder", System.StringComparison.OrdinalIgnoreCase))) {
            return true;
        } else if (executable.Contains("Tools", System.StringComparison.OrdinalIgnoreCase)) {
            Shared.IO.Diagnostics.Log($"[Security.cs::IsApprovedExecutable()] Allowing executable in Tools directory: {executable}");
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
            //Shared.IO.Diagnostics.Trace("[Security.cs::IsAllowedPath()] Denying access to empty or whitespace path");
            return false;
        }

        try {
            string normalizedPath = NormalizeLowerFullPath(path);
            
            // Deny explicitly forbidden paths immediately
            if (IsForbiddenPath(normalizedPath)) {
                Shared.IO.Diagnostics.Trace($"[Security.cs::IsAllowedPath()] Path '{normalizedPath}' is forbidden");
                return false;
            }

            string fullPath = GetCanonicalFullPath(path);
            //Shared.IO.Diagnostics.Trace($"[Security.cs::IsAllowedPath()] Checking path '{fullPath}'");
            //Shared.IO.Diagnostics.Trace($"[Security.cs::IsAllowedPath()] Normalized path '{normalizedPath}'");

            // First, allow any user-approved roots for this session
            foreach (string approved in UserApprovedRoots) {
                if (IsPathWithinBoundary(normalizedPath, approved)) {
                    canonicalPath = ResolveCanonicalPathForIo(fullPath);
                    return true;
                }
            }

            // Get current working directory and common workspace patterns
            string currentDir = NormalizeLowerFullPath(System.IO.Directory.GetCurrentDirectory());
            string projectRoot = string.IsNullOrWhiteSpace(EngineNet.Core.Main.RootPath)
                ? currentDir
                : NormalizeLowerFullPath(EngineNet.Core.Main.RootPath);

            //Shared.IO.Diagnostics.Trace($"[Security.cs::IsAllowedPath()] Current directory '{currentDir}'");
            //Shared.IO.Diagnostics.Trace($"[Security.cs::IsAllowedPath()] Project root '{projectRoot}'");

            // Allowed path patterns (case-insensitive)
            // Note: We check full path starts with these patterns to allow subdirectories,
            // but we also check for exact match to allow files directly in these directories
            string[] allowedPatterns = {
                // Current workspace and subdirectories
                currentDir,

                // must allow access to EngineApps/Games/** for game asset processing
                // but not EngineApps/Registries or EngineApps/Tools to prevent tampering with engine files
                System.IO.Path.Combine(projectRoot, "EngineApps", "Games"),

                // allow random items
                System.IO.Path.Combine(projectRoot, "gamefiles"),
                System.IO.Path.Combine(projectRoot, "tools"),
                System.IO.Path.Combine(projectRoot, "tmp"),
            };

            // Allow if path starts with any allowed pattern
            foreach (string allowedPattern in allowedPatterns) {
                if (IsPathWithinBoundary(normalizedPath, allowedPattern)) {
                    //Shared.IO.Diagnostics.Trace($"[Security.cs::IsAllowedPath()] Path '{normalizedPath}' starts with allowed pattern '{allowedPattern}'");
                    canonicalPath = ResolveCanonicalPathForIo(fullPath);
                    return true;
                } else {
                    Shared.IO.Diagnostics.Trace($"[Security.cs::IsAllowedPath()] Path '{normalizedPath}' does not start with allowed pattern '{allowedPattern}'");
                }
            }

            // Check if the path itself or any of its parents are symlinks that resolve to an allowed path
            try {
                string? check = fullPath;
                string? root = System.IO.Path.GetPathRoot(check);
                Shared.IO.Diagnostics.Trace($"[Security.cs::IsAllowedPath()] Checking symlinks for path '{check}'");

                while (!string.IsNullOrEmpty(check) && !string.Equals(check, root, System.StringComparison.OrdinalIgnoreCase)) {
                    if (System.IO.Directory.Exists(check)) {
                        var info = new System.IO.DirectoryInfo(check);
                        var target = info.ResolveLinkTarget(true); // true = return final target
                        if (target != null) {
                            string targetPath = target.FullName;
                            string suffix = "";
                            if (fullPath.Length > check.Length) {
                                suffix = fullPath.Substring(check.Length);
                            }

                            string resolvedFullPath = targetPath + suffix;
                            string normalizedResolved = NormalizeLowerFullPath(resolvedFullPath);

                            foreach (string allowedPattern in allowedPatterns) {
                                if (IsPathWithinBoundary(normalizedResolved, allowedPattern)) {
                                    canonicalPath = GetCanonicalFullPath(resolvedFullPath);
                                    return true;
                                }
                            }
                        }
                    }
                    check = System.IO.Path.GetDirectoryName(check);
                }
            } catch (Exception ex) {
                /* ignore */
                Shared.IO.Diagnostics.Bug("IsAllowedPath: Failed to resolve symlink for path: " + fullPath + " with exception: " + ex);
            }

            // Additional check: allow relative paths within current directory
            if (!System.IO.Path.IsPathRooted(path)) {
                return true; // Relative paths are generally safe within workspace
            }

            return false; // Default deny for unrecognized absolute paths
        } catch (Exception ex) {
            Shared.IO.Diagnostics.Bug("IsAllowedPath: Failed to check path: " + path + " with exception: " + ex);
            return false; // Path parsing errors = deny
        }
    }

    internal static bool IsAllowedPath(string path) {
        return TryGetAllowedCanonicalPath(path, out _);
    }
}
