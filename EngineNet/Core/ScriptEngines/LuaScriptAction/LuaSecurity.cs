
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections;

namespace EngineNet.Core.ScriptEngines.LuaModules;

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
            return path;
        }
    }

    internal static bool EnsurePathAllowedWithPrompt(string path) {
        if (IsAllowedPath(path)) {
            return true;
        }
        // Ask the user for permission to grant temporary access to this external path
        string root = DetermineApprovalRoot(path);
        string msg = $"Permission requested: Allow this script to access external path '\"{root}\"'?\nType 'y' to allow for this session, anything else to deny.";
        string answer = Core.Utils.EngineSdk.Prompt(msg, "ext_path_access", false) ?? string.Empty;
        if (answer.Trim().Equals("y", System.StringComparison.OrdinalIgnoreCase) ||
            answer.Trim().Equals("yes", System.StringComparison.OrdinalIgnoreCase)) {
            try {
                string normalized = NormalizeLowerFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar);
                UserApprovedRoots.Add(normalized);
            } catch { /* ignore */ }
            return true;
        }
        Core.Utils.EngineSdk.Error($"Access denied: File path '{path}' is outside allowed workspace areas");
        return false;
    }
    /// <summary>
    /// Security validation: Check if executable is approved for RemakeEngine use.
    /// Allows registered tools, common system utilities, and resolved tool paths.
    /// </summary>
    internal static bool IsApprovedExecutable(string executable, Tools.IToolResolver tools) {
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
        } catch { /* Tool resolution may fail, continue with other checks */ }

        // Approved RemakeEngine tools (case-insensitive)
        HashSet<string> approvedTools = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) {
            // Core RemakeEngine tools from Tools.json
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

            // Python/Node (if needed for legacy scripts)
            "python", "python.exe", "python3", "python3.exe",
            "node", "node.exe", "npm", "npm.exe"
        };

        // Check both with and without common extensions
        if (approvedTools.Contains(exeName) || approvedTools.Contains(fullName)) {
            return true;
        }

        // Check for blocked system utilities and provide SDK alternatives
        Dictionary<string, string> blockedUtilities = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) {
            { "copy", "sdk.copy_file(src, dst, overwrite)" },
            { "xcopy", "sdk.copy_dir(src, dst, overwrite)" },
            { "robocopy", "sdk.copy_dir(src, dst, overwrite)" },
            { "move", "sdk.move_dir(src, dst, overwrite) or sdk.rename_file(old, new)" },
            { "ren", "sdk.rename_file(old_name, new_name)" },
            { "rename", "sdk.rename_file(old_name, new_name)" },
            { "cp", "sdk.copy_file(src, dst, overwrite) or sdk.copy_dir(src, dst, overwrite)" },
            { "mv", "sdk.move_dir(src, dst, overwrite) or sdk.rename_file(old, new)" },
            { "rm", "sdk.remove_file(path) or sdk.remove_dir(path)" },
            { "mkdir", "sdk.ensure_dir(path) or sdk.mkdir(path)" },
            { "rmdir", "sdk.remove_dir(path)" },
            { "tar", "sdk.extract_archive(archive, dest) or sdk.create_archive(src, dest, type)" },
            { "unzip", "sdk.extract_archive(archive, dest)" },
            { "7z", "sdk.extract_archive(archive, dest) or sdk.create_archive(src, dest, 'zip')" },
            { "7za", "sdk.extract_archive(archive, dest) or sdk.create_archive(src, dest, 'zip')" }
        };

        if (blockedUtilities.TryGetValue(exeName, out string? suggestion) ||
            blockedUtilities.TryGetValue(fullName, out suggestion)) {
            Core.Utils.EngineSdk.Error($"System utility '{executable}' is blocked for security. Use SDK alternative: {suggestion}");
            return false;
        }

        // Allow executables that are in the Tools directory structure
        if (executable.Contains("Tools", System.StringComparison.OrdinalIgnoreCase) &&
            (executable.Contains("Blender", System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("QuickBMS", System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("Godot", System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("vgmstream", System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("ffmpeg", System.StringComparison.OrdinalIgnoreCase))) {
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
            return false;
        }

        try {
            string fullPath = System.IO.Path.GetFullPath(path);
            string normalizedPath = fullPath.Replace('/', System.IO.Path.DirectorySeparatorChar).ToLowerInvariant();

            // First, allow any user-approved roots for this session
            foreach (string approved in UserApprovedRoots) {
                if (normalizedPath.StartsWith(approved, System.StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            // Get current working directory and common workspace patterns
            string currentDir = System.IO.Directory.GetCurrentDirectory().Replace('/', System.IO.Path.DirectorySeparatorChar).ToLowerInvariant();

            // Allowed path patterns (case-insensitive)
            string[] allowedPatterns = {
                // Current workspace and subdirectories
                currentDir,

                // Common game/project directories
                System.IO.Path.Combine(currentDir, "EngineApps"),
                System.IO.Path.Combine(currentDir, "gamefiles"),
                System.IO.Path.Combine(currentDir, "tools"),
                System.IO.Path.Combine(currentDir, "tmp"),
                System.IO.Path.Combine(currentDir, "source"),

                // Temp directories
                System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar).ToLowerInvariant(),

                // User profile directories (for game installations)
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile).Replace('/', System.IO.Path.DirectorySeparatorChar).ToLowerInvariant(),
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments).Replace('/', System.IO.Path.DirectorySeparatorChar).ToLowerInvariant(),
            };

            // Allow if path starts with any allowed pattern
            foreach (string allowedPattern in allowedPatterns) {
                if (normalizedPath.StartsWith(allowedPattern, System.StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            // Deny access to sensitive system directories
            string[] forbiddenPatterns = {
                @"c:\windows\system32",
                @"c:\windows\syswow64",
                @"c:\program files",
                @"c:\program files (x86)",
                "/etc/", "/bin/", "/sbin/", "/usr/bin/", "/usr/sbin/",
                "/sys/", "/proc/", "/dev/"
            };

            foreach (string forbiddenPattern in forbiddenPatterns) {
                string normalizedForbidden = forbiddenPattern.Replace('/', System.IO.Path.DirectorySeparatorChar).ToLowerInvariant();
                if (normalizedPath.StartsWith(normalizedForbidden, System.StringComparison.OrdinalIgnoreCase)) {
                    return false;
                }
            }

            // Additional check: allow relative paths within current directory
            if (!System.IO.Path.IsPathRooted(path)) {
                return true; // Relative paths are generally safe within workspace
            }

            return false; // Default deny for unrecognized absolute paths
        } catch {
            return false; // Path parsing errors = deny
        }
    }
}