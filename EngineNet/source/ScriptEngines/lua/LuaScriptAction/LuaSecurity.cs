using System;
using System.Collections.Generic;

namespace EngineNet.ScriptEngines.lua.LuaModules;

/// <summary>
/// Security validation methods for Lua script execution.
/// Provides path validation and executable approval for RemakeEngine security.
/// </summary>
internal static class LuaSecurity {
    private static readonly HashSet<string> UserApprovedRoots = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

    private static string NormalizeLowerFullPath(string path) {
        string fullPath = System.IO.Path.GetFullPath(path);
        return fullPath.Replace('/', System.IO.Path.DirectorySeparatorChar).ToLowerInvariant();
    }

    private static string DetermineApprovalRoot(string path) {
        try {
            string full = System.IO.Path.GetFullPath(path);
            if (System.IO.Directory.Exists(full)) {
                return new System.IO.DirectoryInfo(full).FullName;
            }
            string? dir = System.IO.Path.GetDirectoryName(full);
            if (!string.IsNullOrWhiteSpace(dir)) {
                return new System.IO.DirectoryInfo(dir).FullName;
            }
            return full;
        } catch {
            Core.Diagnostics.Bug("DetermineApprovalRoot: Failed to determine root for path: " + path);
            return path;
        }
    }

    internal static bool EnsurePathAllowedWithPrompt(string path) {
        if (IsAllowedPath(path)) {
            return true;
        }
        // Ask the user for permission to grant temporary access to this external path
        string root = DetermineApprovalRoot(path);
        string msg = $"Permission requested: Allow this script to access external path '\"{root}\"'?";

        bool allowed = Core.UI.EngineSdk.Confirm(msg, "ext_path_access", false);

        if (allowed) {
            try {
                string normalized = NormalizeLowerFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar);
                UserApprovedRoots.Add(normalized);
            } catch {
                Core.Diagnostics.Bug("[LuaSecurity.cs::EnsurePathAllowedWithPrompt()] Failed to normalize and approve path: " + root);
                /* ignore */
            }
            return true;
        }
        Core.UI.EngineSdk.Error($"Access denied: File path '{path}' is outside allowed workspace areas");
        return false;
    }
    /// <summary>
    /// Security validation: Check if executable is approved for RemakeEngine use.
    /// Allows registered tools, common system utilities, and resolved tool paths.
    /// </summary>
    internal static bool IsApprovedExecutable(string executable, Core.ExternalTools.IToolResolver tools) {
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
            Core.Diagnostics.Trace("[LuaSecurity.cs::IsApprovedExecutable()] Tool resolution failed for: " + exeName + " with exception: " + ex);
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
            Core.Diagnostics.Log($"[LuaSecurity.cs::IsApprovedExecutable()] Allowing executable in Tools directory: {executable}");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Security validation: Check if file path is within allowed workspace areas.
    /// Prevents access to sensitive system files while allowing game asset processing.
    /// </summary>
    internal static bool IsAllowedPath(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            Core.Diagnostics.Trace("[LuaSecurity.cs::IsAllowedPath()] Denying access to empty or whitespace path");
            return false;
        }

        try {
            string fullPath = System.IO.Path.GetFullPath(path);
            string normalizedPath = fullPath.Replace('/', System.IO.Path.DirectorySeparatorChar).ToLowerInvariant();
            Core.Diagnostics.Trace($"[LuaSecurity.cs::IsAllowedPath()] Checking path '{fullPath}'");
            Core.Diagnostics.Trace($"[LuaSecurity.cs::IsAllowedPath()] Normalized path '{normalizedPath}'");

            // First, allow any user-approved roots for this session
            foreach (string approved in UserApprovedRoots) {
                if (normalizedPath.StartsWith(approved, System.StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            // Get current working directory and common workspace patterns
            string currentDir = System.IO.Directory.GetCurrentDirectory().Replace('/', System.IO.Path.DirectorySeparatorChar).ToLowerInvariant();
            string projectRoot = string.IsNullOrWhiteSpace(EngineNet.Program.rootPath)
                ? currentDir
                : EngineNet.Program.rootPath.Replace('/', System.IO.Path.DirectorySeparatorChar).ToLowerInvariant();

            Core.Diagnostics.Trace($"[LuaSecurity.cs::IsAllowedPath()] Current directory '{currentDir}'");
            Core.Diagnostics.Trace($"[LuaSecurity.cs::IsAllowedPath()] Project root '{projectRoot}'");

            // Allowed path patterns (case-insensitive)
            string[] allowedPatterns = {
                // Current workspace and subdirectories
                currentDir,
                //
                projectRoot,

                // project directories
                System.IO.Path.Combine(projectRoot, "EngineApps"),
                System.IO.Path.Combine(projectRoot, "gamefiles"),
                System.IO.Path.Combine(projectRoot, "tools"),
                System.IO.Path.Combine(projectRoot, "tmp"),
            };

            // Allow if path starts with any allowed pattern
            foreach (string allowedPattern in allowedPatterns) {
                if (normalizedPath.StartsWith(allowedPattern, System.StringComparison.OrdinalIgnoreCase)) {
                    Core.Diagnostics.Trace($"[LuaSecurity.cs::IsAllowedPath()] Path '{normalizedPath}' starts with allowed pattern '{allowedPattern}'");
                    return true;
                } else {
                    Core.Diagnostics.Trace($"[LuaSecurity.cs::IsAllowedPath()] Path '{normalizedPath}' does not start with allowed pattern '{allowedPattern}'");
                }
            }

            // Check if the path itself or any of its parents are symlinks that resolve to an allowed path
            try {
                string? check = fullPath;
                string? root = System.IO.Path.GetPathRoot(check);
                Core.Diagnostics.Trace($"[LuaSecurity.cs::IsAllowedPath()] Checking symlinks for path '{check}'");

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
                            string normalizedResolved = resolvedFullPath.Replace('/', System.IO.Path.DirectorySeparatorChar).ToLowerInvariant();

                            foreach (string allowedPattern in allowedPatterns) {
                                if (normalizedResolved.StartsWith(allowedPattern, System.StringComparison.OrdinalIgnoreCase)) {
                                    return true;
                                }
                            }
                        }
                    }
                    check = System.IO.Path.GetDirectoryName(check);
                }
            } catch (Exception ex) {
                /* ignore */
                Core.Diagnostics.Bug("IsAllowedPath: Failed to resolve symlink for path: " + fullPath + " with exception: " + ex);
            }

            // Deny access to sensitive system directories
            string[] forbiddenPatterns = {
                @"c:\windows\system32",
                @"c:\windows\syswow64",
                @"c:\program files",
                @"c:\program files (x86)",
                "/etc/", "/bin/", "/sbin/", "/usr/bin/", "/usr/sbin/",
                "/sys/", "/proc/", "/dev/",
                // Explicitly deny access to Registries to prevent tampering
                System.IO.Path.Combine(projectRoot, "EngineApps", "Registries").Replace('/', System.IO.Path.DirectorySeparatorChar).ToLowerInvariant()
            };

            foreach (string forbiddenPattern in forbiddenPatterns) {
                string normalizedForbidden = forbiddenPattern.Replace('/', System.IO.Path.DirectorySeparatorChar).ToLowerInvariant();
                if (normalizedPath.StartsWith(normalizedForbidden, System.StringComparison.OrdinalIgnoreCase)) {
                    Core.Diagnostics.Trace($"[LuaSecurity.cs::IsAllowedPath()] Path '{normalizedPath}' starts with forbidden pattern '{normalizedForbidden}'");
                    return false;
                }
            }

            // Additional check: allow relative paths within current directory
            if (!System.IO.Path.IsPathRooted(path)) {
                return true; // Relative paths are generally safe within workspace
            }

            return false; // Default deny for unrecognized absolute paths
        } catch (Exception ex) {
            Core.Diagnostics.Bug("IsAllowedPath: Failed to check path: " + path + " with exception: " + ex);
            return false; // Path parsing errors = deny
        }
    }
}