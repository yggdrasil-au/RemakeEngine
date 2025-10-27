using System.Diagnostics;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections;

namespace EngineNet.Core.Sys;

/// <summary>
/// Executes external processes while streaming output and handling structured
/// engine events embedded in stdout/stderr. Lines starting with <see cref="Types.RemakePrefix"/>
/// are parsed as JSON event payloads and forwarded to <c>onEvent</c>.
/// Supports interactive prompts by invoking <c>stdinProvider</c> when a prompt event is received.
/// </summary>
public sealed class ProcessRunner {
    /// <summary>
    /// Callback to receive a single line of output from the child process.
    /// </summary>
    /// <param name="line">Text of the line.</param>
    /// <param name="streamName">"stdout" or "stderr".</param>
    public delegate void OutputHandler(String line, String streamName);
    /// <summary>
    /// Callback to receive a structured engine event decoded from the child output.
    /// </summary>
    public delegate void EventHandler(Dictionary<String, Object?> evt);
    /// <summary>
    /// Provider used to gather user input when a prompt event is seen.
    /// Return value is written to the child's stdin with a trailing newline.
    /// </summary>
    public delegate String? StdinProvider();

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
    public Boolean Execute(
        IList<String> commandParts,
        String opTitle,
        OutputHandler? onOutput = null,
        EventHandler? onEvent = null,
        StdinProvider? stdinProvider = null,
        IDictionary<String, Object?>? envOverrides = null,
        CancellationToken cancellationToken = default) {
        if (commandParts is null || commandParts.Count < 1) {
            onOutput?.Invoke($"Operation '{opTitle}' has no executable specified. Skipping.", "stderr");
            return false;
        }

        // Security: Validate executable is approved for RemakeEngine use
        String executable = commandParts[0];
        if (!IsApprovedExecutable(executable, onOutput)) {
            return false;
        }

        // Verbose: show exactly what will be executed
        try {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("\nExecuting command:");
            Console.ResetColor();
            Console.WriteLine("  " + FormatCommand(commandParts));
            Console.WriteLine($"  cwd: {Directory.GetCurrentDirectory()}");
            if (envOverrides != null && envOverrides.Count > 0) {
                Console.WriteLine("  env overrides:");
                foreach (KeyValuePair<String, Object?> kv in envOverrides) {
                    Console.WriteLine($"    {kv.Key}={kv.Value}");
                }
            }
        } catch { /* ignore formatting errors */ }

        ProcessStartInfo psi = new ProcessStartInfo {
            FileName = commandParts[0],
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        for (Int32 i = 1; i < commandParts.Count; i++) {
            psi.ArgumentList.Add(commandParts[i]);
        }

        foreach (DictionaryEntry de in Environment.GetEnvironmentVariables()) {
            psi.Environment[de.Key.ToString()!] = de.Value?.ToString() ?? String.Empty;
        }

        if (envOverrides != null) {
            foreach (KeyValuePair<String, Object?> kv in envOverrides) {
                psi.Environment[kv.Key] = kv.Value?.ToString() ?? String.Empty;
            }
        }

        // Encourage line-buffered UTF-8 for child Python
        if (!psi.Environment.ContainsKey("PYTHONUNBUFFERED")) {
            psi.Environment["PYTHONUNBUFFERED"] = "1";
        }

        if (!psi.Environment.ContainsKey("PYTHONIOENCODING")) {
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
        }

        using Process proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        BlockingCollection<(String stream, String line)> q = new BlockingCollection<(String stream, String line)>(boundedCapacity: 1000);

        DataReceivedEventHandler outHandler = (_, e) => { if (e.Data != null) { q.Add(("stdout", e.Data)); } };
        DataReceivedEventHandler errHandler = (_, e) => { if (e.Data != null) { q.Add(("stderr", e.Data)); } };

        try {
            if (!proc.Start()) {
                throw new InvalidOperationException("Failed to start process");
            }

            proc.OutputDataReceived += outHandler;
            proc.ErrorDataReceived += errHandler;
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            Boolean awaitingPrompt = false;
            String? lastPromptMsg = null;
            Boolean suppressPromptEcho = !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ENGINE_SUPPRESS_PROMPT_ECHO"));

            void SendToChild(String? text) {
                try {
                    proc.StandardInput.WriteLine(text ?? String.Empty);
                    proc.StandardInput.Flush();
                } catch { /* ignore */ }
            }

            String? HandleLine(String line, String streamName) {
                if (line.StartsWith(Types.RemakePrefix, StringComparison.Ordinal)) {
                    String payload = line.Substring(Types.RemakePrefix.Length).Trim();
                    try {
                        Dictionary<String, Object?> evt = JsonSerializer.Deserialize<Dictionary<String, Object?>>(payload) ?? new();
                        if (evt.TryGetValue("event", out Object? evType) && (evType?.ToString() ?? "") == "prompt") {
                            if (!suppressPromptEcho) {
                                onEvent?.Invoke(evt);
                            }

                            return evt.TryGetValue("message", out Object? msg) ? msg?.ToString() ?? "Input required" : "Input required";
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
                    onEvent?.Invoke(new Dictionary<String, Object?> { ["event"] = "end", ["success"] = false, ["exit_code"] = 130 });
                    return false;
                }

                if (!q.TryTake(out (String stream, String line) item, 100)) {
                    continue;
                }

                String? promptMsg = HandleLine(item.line, item.stream);
                if (promptMsg != null) {
                    awaitingPrompt = true;
                    lastPromptMsg = promptMsg;
                }

                if (awaitingPrompt && !proc.HasExited) {
                    String? ans;
                    try {
                        ans = stdinProvider?.Invoke();
                    } catch { ans = String.Empty; }
                    SendToChild(ans);
                    awaitingPrompt = false;
                    lastPromptMsg = null;
                }
            }

            // Drain any remaining
            while (q.TryTake(out (String stream, String line) item)) {
                String? promptMsg = HandleLine(item.line, item.stream);
                if (promptMsg != null) {
                    String? ans;
                    try {
                        ans = stdinProvider?.Invoke();
                    } catch { ans = String.Empty; }
                    SendToChild(ans);
                }
            }

            Int32 rc = proc.ExitCode;
            if (rc == 0) {
                onEvent?.Invoke(new Dictionary<String, Object?> { ["event"] = "end", ["success"] = true, ["exit_code"] = 0 });
                return true;
            } else {
                onEvent?.Invoke(new Dictionary<String, Object?> { ["event"] = "end", ["success"] = false, ["exit_code"] = rc });
                return false;
            }
        } catch (OperationCanceledException) {
            TryTerminate(proc);
            onEvent?.Invoke(new Dictionary<String, Object?> { ["event"] = "end", ["success"] = false, ["exit_code"] = 130 });
            return false;
        } catch (FileNotFoundException) {
            onEvent?.Invoke(new Dictionary<String, Object?> { ["event"] = "error", ["kind"] = "FileNotFoundError", ["message"] = "Command or script not found." });
            return false;
        } catch (Exception ex) {
            onEvent?.Invoke(new Dictionary<String, Object?> { ["event"] = "error", ["kind"] = "Exception", ["message"] = ex.Message });
            return false;
        } finally {
            try {
                proc.OutputDataReceived -= outHandler;
                proc.ErrorDataReceived -= errHandler;
            } catch { /* ignore */ }
        }
    }

    private static void TryTerminate(Process proc) {
        try {
            if (!proc.HasExited) {
                proc.Kill(entireProcessTree: true);
            }
        } catch { /* ignore */ }
    }

    private static String FormatCommand(IList<String> parts) {
        StringBuilder sb = new StringBuilder();
        for (Int32 i = 0; i < parts.Count; i++) {
            if (i > 0) {
                sb.Append(' ');
            }

            sb.Append(QuoteArg(parts[i] ?? String.Empty));
        }
        return sb.ToString();
    }

    private static String QuoteArg(String arg) {
        if (String.IsNullOrEmpty(arg)) {
            return "\"\"";
        }

        Boolean needsQuotes = arg.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0;
        if (!needsQuotes) {
            return arg;
        }
        // Escape embedded quotes by backslash
        String escaped = arg.Replace("\"", "\\\"");
        return "\"" + escaped + "\"";
    }

    /// <summary>
    /// Security validation: Check if executable is approved for RemakeEngine use.
    /// Prevents execution of blocked system utilities and suggests SDK alternatives.
    /// </summary>
    private static Boolean IsApprovedExecutable(String executable, OutputHandler? onOutput) {
        if (String.IsNullOrWhiteSpace(executable)) {
            return false;
        }
        
        // Normalize executable name (remove path and extension for comparison)
        String exeName = Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
        String fullName = Path.GetFileName(executable).ToLowerInvariant();
        
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
            onOutput?.Invoke($"SECURITY: System utility '{executable}' is blocked for security. Use SDK alternative: {suggestion}", "stderr");
            return false;
        }
        
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
        
        // Allow executables that are in the Tools directory structure
        if (executable.Contains("Tools", StringComparison.OrdinalIgnoreCase) && 
            (executable.Contains("Blender", StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("QuickBMS", StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("Godot", StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("vgmstream", StringComparison.OrdinalIgnoreCase) ||
             executable.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase))) {
            return true;
        }
        
        // For unrecognized executables, provide guidance
        onOutput?.Invoke($"SECURITY: Executable '{executable}' is not approved for RemakeEngine. Use registered tools from Tools.json or SDK methods for file operations.", "stderr");
        return false;
    }
}
