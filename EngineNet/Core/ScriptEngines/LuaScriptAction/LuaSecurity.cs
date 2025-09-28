using System;
using System.Collections.Generic;
using System.IO;
using EngineNet.Tools;
using EngineNet.Core.Sys;
using EngineNet.Core.ScriptEngines.Helpers;

namespace EngineNet.Core.ScriptEngines.LuaModules;

/// <summary>
/// Security validation methods for Lua script execution.
/// Provides path validation and executable approval for RemakeEngine security.
/// </summary>
internal static class LuaSecurity {
    /// <summary>
    /// Security validation: Check if executable is approved for RemakeEngine use.
    /// Allows registered tools, common system utilities, and resolved tool paths.
    /// </summary>
    public static Boolean IsApprovedExecutable(String executable, IToolResolver tools) {
        if (String.IsNullOrWhiteSpace(executable)) {
            return false;
        }
        
        // Normalize executable name (remove path and extension for comparison)
        String exeName = Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
        String fullName = Path.GetFileName(executable).ToLowerInvariant();
        
        // Allow resolved tool paths (tools that came from tool() function)
        try {
            String resolvedPath = tools.ResolveToolPath(exeName);
            if (!String.IsNullOrEmpty(resolvedPath) && 
                (executable.Equals(resolvedPath, StringComparison.OrdinalIgnoreCase) ||
                 executable.EndsWith(resolvedPath, StringComparison.OrdinalIgnoreCase))) {
                return true;
            }
        } catch { /* Tool resolution may fail, continue with other checks */ }
        
        // Approved RemakeEngine tools (case-insensitive)
        HashSet<String> approvedTools = new HashSet<String>(StringComparer.OrdinalIgnoreCase) {
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
        Dictionary<String, String> blockedUtilities = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase) {
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
        
        if (blockedUtilities.TryGetValue(exeName, out String? suggestion) || 
            blockedUtilities.TryGetValue(fullName, out suggestion)) {
            EngineSdk.Error($"System utility '{executable}' is blocked for security. Use SDK alternative: {suggestion}");
            return false;
        }
        
        // Allow executables that are in the Tools directory structure
        if (executable.Contains("Tools", StringComparison.OrdinalIgnoreCase) && 
            (executable.Contains("Blender", StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("QuickBMS", StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("Godot", StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("vgmstream", StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase))) {
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// Security validation: Check if file path is within allowed workspace areas.
    /// Prevents access to sensitive system files while allowing game asset processing.
    /// </summary>
    public static Boolean IsAllowedPath(String path) {
        if (String.IsNullOrWhiteSpace(path)) {
            return false;
        }
        
        try {
            String fullPath = Path.GetFullPath(path);
            String normalizedPath = fullPath.Replace('/', Path.DirectorySeparatorChar).ToLowerInvariant();
            
            // Get current working directory and common workspace patterns
            String currentDir = Directory.GetCurrentDirectory().Replace('/', Path.DirectorySeparatorChar).ToLowerInvariant();
            
            // Allowed path patterns (case-insensitive)
            String[] allowedPatterns = {
                // Current workspace and subdirectories
                currentDir,
                
                // Common game/project directories
                Path.Combine(currentDir, "remakeregistry"),
                Path.Combine(currentDir, "gamefiles"),
                Path.Combine(currentDir, "tools"), 
                Path.Combine(currentDir, "tmp"),
                Path.Combine(currentDir, "source"),
                
                // Temp directories
                Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant(),
                
                // User profile directories (for game installations)
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace('/', Path.DirectorySeparatorChar).ToLowerInvariant(),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments).Replace('/', Path.DirectorySeparatorChar).ToLowerInvariant(),
            };
            
            // Allow if path starts with any allowed pattern
            foreach (String allowedPattern in allowedPatterns) {
                if (normalizedPath.StartsWith(allowedPattern, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            
            // Deny access to sensitive system directories
            String[] forbiddenPatterns = {
                @"c:\windows\system32",
                @"c:\windows\syswow64", 
                @"c:\program files",
                @"c:\program files (x86)",
                "/etc/", "/bin/", "/sbin/", "/usr/bin/", "/usr/sbin/",
                "/sys/", "/proc/", "/dev/"
            };
            
            foreach (String forbiddenPattern in forbiddenPatterns) {
                String normalizedForbidden = forbiddenPattern.Replace('/', Path.DirectorySeparatorChar).ToLowerInvariant();
                if (normalizedPath.StartsWith(normalizedForbidden, StringComparison.OrdinalIgnoreCase)) {
                    return false;
                }
            }
            
            // Additional check: allow relative paths within current directory
            if (!Path.IsPathRooted(path)) {
                return true; // Relative paths are generally safe within workspace
            }
            
            return false; // Default deny for unrecognized absolute paths
        } catch {
            return false; // Path parsing errors = deny
        }
    }
}