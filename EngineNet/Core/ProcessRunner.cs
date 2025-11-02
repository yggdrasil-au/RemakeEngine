
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace EngineNet.Core;

/// <summary>
/// Executes external processes while streaming output and handling structured
/// engine events embedded in stdout/stderr. Lines starting with <see cref="EngineSdk.Prefix"/>
/// are parsed as JSON event payloads and forwarded to <c>onEvent</c>.
/// Supports interactive prompts by invoking <c>stdinProvider</c> when a prompt event is received.
/// </summary>
internal sealed class ProcessRunner {
    /// <summary>
    /// Callback to receive a single line of output from the child process.
    /// </summary>
    /// <param name="line">Text of the line.</param>
    /// <param name="streamName">"stdout" or "stderr".</param>
    internal delegate void OutputHandler(string line, string streamName);
    /// <summary>
    /// Callback to receive a structured engine event decoded from the child output.
    /// </summary>
    internal delegate void EventHandler(Dictionary<string, object?> evt);
    /// <summary>
    /// Provider used to gather user input when a prompt event is seen.
    /// Return value is written to the child's stdin with a trailing newline.
    /// </summary>
    internal delegate string? StdinProvider();

    /// <summary>
    /// Execute a command line and stream output until completion or cancellation.
    /// </summary>
    /// <param name="commandParts">Executable followed by its arguments. Must contain at least one item (the executable).</param>
    /// <param name="opTitle">Human-readable operation title used in diagnostics.</param>
    /// <param name="onOutput">Optional callback for stdout/stderr lines.</param>
    /// <param name="onEvent">Optional callback for structured events.</param>
    /// <param name="stdinProvider">Optional provider for prompt responses.</param>
    /// <param name="envOverrides">Optional environment variables to inject/override for the child.</param>
    /// <param name="cancellationToken">Token to abort execution.</param>
    /// <returns>True on zero exit code; false otherwise.</returns>
    internal bool Execute(
        IList<string> commandParts,
        string opTitle,
        OutputHandler? onOutput = null,
        EventHandler? onEvent = null,
        StdinProvider? stdinProvider = null,
        IDictionary<string, object?>? envOverrides = null,
        System.Threading.CancellationToken cancellationToken = default) {
        if (commandParts is null || commandParts.Count < 1) {
            onOutput?.Invoke($"Operation '{opTitle}' has no executable specified. Skipping.", "stderr");
            return false;
        }

        // Security: Validate executable is approved for RemakeEngine use
        string executable = commandParts[0];
        if (!IsApprovedExecutable(executable, onOutput)) {
            return false;
        }

        // Verbose: show exactly what will be executed
        try {
            System.Diagnostics.Trace.WriteLine(string.Empty);
            System.Diagnostics.Trace.WriteLine("Executing command:");
            System.Diagnostics.Trace.WriteLine("  " + FormatCommand(commandParts));
            System.Diagnostics.Trace.WriteLine($"  cwd: {System.IO.Directory.GetCurrentDirectory()}");
            if (envOverrides != null && envOverrides.Count > 0) {
                System.Diagnostics.Trace.WriteLine("  env overrides:");
                foreach (KeyValuePair<string, object?> kv in envOverrides) {
                    System.Diagnostics.Trace.WriteLine($"    {kv.Key}={kv.Value}");
                }
            }
        } catch {
            /* ignore formatting errors */
        }

        System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
            FileName = commandParts[0],
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        for (int i = 1; i < commandParts.Count; i++) {
            psi.ArgumentList.Add(commandParts[i]);
        }

        using System.Diagnostics.Process proc = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };

        System.Collections.Concurrent.BlockingCollection<(string stream, string line)> q = new System.Collections.Concurrent.BlockingCollection<(string stream, string line)>(boundedCapacity: 1000);

        System.Diagnostics.DataReceivedEventHandler outHandler = (_, e) => { if (e.Data != null) { q.Add(("stdout", e.Data)); } };
        System.Diagnostics.DataReceivedEventHandler errHandler = (_, e) => { if (e.Data != null) { q.Add(("stderr", e.Data)); } };

        try {
            if (!proc.Start()) {
                throw new System.InvalidOperationException("Failed to start process");
            }

            proc.OutputDataReceived += outHandler;
            proc.ErrorDataReceived += errHandler;
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            bool awaitingPrompt = false;
            string? lastPromptMsg = null;

            void SendToChild(string? text) {
                try {
                    proc.StandardInput.WriteLine(text ?? string.Empty);
                    proc.StandardInput.Flush();
                } catch {
                /* ignore */
}
            }

            string? HandleLine(string line, string streamName) {
                if (line.StartsWith(Core.Utils.EngineSdk.Prefix, System.StringComparison.Ordinal)) {
                    string payload = line.Substring(Core.Utils.EngineSdk.Prefix.Length).Trim();
                    try {
                        Dictionary<string, object?> evt = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payload) ?? new();
                        if (evt.TryGetValue("event", out object? evType) && (evType?.ToString() ?? "") == "prompt") {
                            onEvent?.Invoke(evt);

                            return evt.TryGetValue("message", out object? msg) ? msg?.ToString() ?? "Input required" : "Input required";
                        }
                        onEvent?.Invoke(evt);
                        return null;
                    } catch {
                        onOutput?.Invoke(line, streamName);
                        return null;
                    }
                } else {
                    onOutput?.Invoke(line, streamName);
                    return null;
                }
            }

            while (!proc.HasExited) {
                if (cancellationToken.IsCancellationRequested) {
                    TryTerminate(proc);
                    onEvent?.Invoke(new Dictionary<string, object?> { ["event"] = "end", ["success"] = false, ["exit_code"] = 130 });
                    return false;
                }

                if (!q.TryTake(out (string stream, string line) item, 100)) {
                    continue;
                }

                string? promptMsg = HandleLine(item.line, item.stream);
                if (promptMsg != null) {
                    awaitingPrompt = true;
                    lastPromptMsg = promptMsg;
                }

                if (awaitingPrompt && !proc.HasExited) {
                    string? ans;
                    try {
                        ans = stdinProvider?.Invoke();
                    } catch { ans = string.Empty; }
                    SendToChild(ans);
                    awaitingPrompt = false;
                    lastPromptMsg = null;
                }
            }

            // Drain any remaining
            while (q.TryTake(out (string stream, string line) item)) {
                string? promptMsg = HandleLine(item.line, item.stream);
                if (promptMsg != null) {
                    string? ans;
                    try {
                        ans = stdinProvider?.Invoke();
                    } catch { ans = string.Empty; }
                    SendToChild(ans);
                }
            }

            int rc = proc.ExitCode;
            if (rc == 0) {
                onEvent?.Invoke(new Dictionary<string, object?> { ["event"] = "end", ["success"] = true, ["exit_code"] = 0 });
                return true;
            } else {
                onEvent?.Invoke(new Dictionary<string, object?> { ["event"] = "end", ["success"] = false, ["exit_code"] = rc });
                return false;
            }
        } catch (System.OperationCanceledException) {
            TryTerminate(proc);
            onEvent?.Invoke(new Dictionary<string, object?> { ["event"] = "end", ["success"] = false, ["exit_code"] = 130 });
            return false;
        } catch (System.IO.FileNotFoundException) {
            onEvent?.Invoke(new Dictionary<string, object?> { ["event"] = "error", ["kind"] = "FileNotFoundError", ["message"] = "Command or script not found." });
            return false;
        } catch (System.Exception ex) {
            onEvent?.Invoke(new Dictionary<string, object?> { ["event"] = "error", ["kind"] = "Exception", ["message"] = ex.Message });
            return false;
        } finally {
            try {
                proc.OutputDataReceived -= outHandler;
                proc.ErrorDataReceived -= errHandler;
            } catch {
#if DEBUG
                System.Diagnostics.Trace.WriteLine("Warning: Failed to detach event handlers.");
#endif
                /* ignore */
            }
        }
    }

    private static void TryTerminate(System.Diagnostics.Process proc) {
        try {
            if (!proc.HasExited) {
                proc.Kill(entireProcessTree: true);
            }
        } catch {
#if DEBUG
            System.Diagnostics.Trace.WriteLine("Warning: Failed to terminate process.");
#endif
            /* ignore */
        }
    }

    private static string FormatCommand(IList<string> parts) {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < parts.Count; i++) {
            if (i > 0) {
                sb.Append(' ');
            }

            sb.Append(QuoteArg(parts[i] ?? string.Empty));
        }
        return sb.ToString();
    }

    private static string QuoteArg(string arg) {
        if (string.IsNullOrEmpty(arg)) {
            return "\"\"";
        }

        bool needsQuotes = arg.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0;
        if (!needsQuotes) {
            return arg;
        }
        // Escape embedded quotes by backslash
        string escaped = arg.Replace("\"", "\\\"");
        return "\"" + escaped + "\"";
    }

    /// <summary>
    /// Security validation: Check if executable is approved for RemakeEngine use.
    /// Prevents execution of blocked system utilities and suggests SDK alternatives.
    /// </summary>
    private static bool IsApprovedExecutable(string executable, OutputHandler? onOutput) {
        if (string.IsNullOrWhiteSpace(executable)) {
            return false;
        }

        // Normalize executable name (remove path and extension for comparison)
        string exeName = System.IO.Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
        string fullName = System.IO.Path.GetFileName(executable).ToLowerInvariant();

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
            onOutput?.Invoke($"SECURITY: System utility '{executable}' is blocked for security. Use SDK alternative: {suggestion}", "stderr");
            return false;
        }

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

        // Allow executables that are in the Tools directory structure
        if (executable.Contains("Tools", System.StringComparison.OrdinalIgnoreCase) &&
            (executable.Contains("Blender", System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("bms", System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("QuickBMS", System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("Godot", System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("vgmstream", System.StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("ffmpeg", System.StringComparison.OrdinalIgnoreCase))) {
            return true;
        }

        // For unrecognized executables, provide guidance
        onOutput?.Invoke($"SECURITY: Executable '{executable}' is not approved for RemakeEngine. Use registered tools from Tools.json or SDK methods for file operations.", "stderr");
        return false;
    }
}
