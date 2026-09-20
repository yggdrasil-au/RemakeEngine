namespace EngineNet.Core.Services.CommandService;

/// <summary>
/// Executes external processes while streaming output and handling structured
/// events. Lines prefixed with the SDK event marker
/// are parsed as JSON event payloads and forwarded to <c>onEvent</c>.
/// Supports interactive prompts by invoking <c>stdinProvider</c> when a prompt event is received.
/// </summary>
public sealed partial class ProcessRunner : Core.Abstractions.IProcessRunner {

    private static void TryTerminate(System.Diagnostics.Process proc) {
        try {
            if (!proc.HasExited) {
                proc.Kill(entireProcessTree: true);
            }
        } catch (System.InvalidOperationException) {
            // Process has already exited between the check and the Kill call
        } catch (System.ComponentModel.Win32Exception ex) {
            // Access denied or the process is already terminating
            Shared.IO.Diagnostics.Bug($"Win32 error terminating process: {ex.Message}");
        } catch (System.NotSupportedException) {
            // entireProcessTree: true is not supported on some older/specific platforms
            proc.Kill();
        }
    }

    private static string FormatCommand(IList<string> parts) {
        System.Text.StringBuilder sb = new();
        for (int i = 0; i < parts.Count; i++) {
            if (i > 0) {
                sb.Append(' ');
            }

            sb.Append(QuoteArg(arg: parts[index: i]));
        }
        return sb.ToString();
    }

    private static string QuoteArg(string arg) {
        if (string.IsNullOrEmpty(arg)) {
            return "\"\"";
        }

        bool needsQuotes = arg.IndexOfAny(anyOf: new[] { ' ', '\t', '"' }) >= 0;
        if (!needsQuotes) {
            return arg;
        }
        // Escape embedded quotes by backslash
        string escaped = arg.Replace(oldValue: "\"", newValue: "\\\"");
        return "\"" + escaped + "\"";
    }

    /// <summary>
    /// Security validation: Check if executable is approved for RemakeEngine use.
    /// Prevents execution of blocked system utilities and suggests SDK alternatives.
    /// </summary>
    private static bool IsApprovedExecutable(string executable, Core.Abstractions.IProcessRunner.OutputHandler? onOutput) {
        if (string.IsNullOrWhiteSpace(executable)) {
            return false;
        }

        // Normalize executable name (remove path and extension for comparison)
        string exeName = System.IO.Path.GetFileNameWithoutExtension(path: executable).ToLowerInvariant();
        string fullName = System.IO.Path.GetFileName(path: executable).ToLowerInvariant();

        // Check for blocked system utilities and provide SDK alternatives
        Dictionary<string, string> blockedUtilities = new(comparer: System.StringComparer.OrdinalIgnoreCase) {
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

        if (blockedUtilities.TryGetValue(key: exeName, out string? suggestion) ||
            blockedUtilities.TryGetValue(key: fullName, out suggestion)) {
            onOutput?.Invoke(line: $"SECURITY: System utility '{executable}' is blocked for security. Use SDK alternative: {suggestion}", streamName: "stderr");
            return false;
        }

        // Approved RemakeEngine tools (case-insensitive)
        HashSet<string> approvedTools = new(comparer: System.StringComparer.OrdinalIgnoreCase) {
            // Core RemakeEngine tools from "EngineApps", "Registries", "Tools", TODO: "Main.json", resolve dynamically
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
            "node", "node.exe", "npm", "npm.exe",
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
            Shared.IO.Diagnostics.Log($"Allowing specific executable in Tools directory: {executable}");
            return true;
        } else if (executable.Contains("Tools", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
            Shared.IO.Diagnostics.Log($"Allowing executable in Tools directory: {executable}");
            return true;
        } else {
            Shared.IO.Diagnostics.Log($"executable not in Tools directory: {executable}, disallowing.");
        }

        // For unrecognized executables, provide guidance
        onOutput?.Invoke(line: $"SECURITY: Executable '{executable}' is not approved for RemakeEngine. Use registered tools from \"EngineApps\", \"Registries\", \"Tools\", \"Main.json\" or SDK methods for file operations.", streamName: "stderr");
        Shared.IO.Diagnostics.Log($"Blocked unrecognized executable: {executable}");
        return false;
    }

}

