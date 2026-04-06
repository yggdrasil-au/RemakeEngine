namespace EngineNet.Core;

/// <summary>
/// Executes external processes while streaming output and handling structured
/// events. Lines prefixed with the SDK event marker
/// are parsed as JSON event payloads and forwarded to <c>onEvent</c>.
/// Supports interactive prompts by invoking <c>stdinProvider</c> when a prompt event is received.
/// </summary>
public sealed partial class ProcessRunner {
    /// <summary>
    /// Callback to receive a single line of output from the child process.
    /// </summary>
    /// <param name="line">Text of the line.</param>
    /// <param name="streamName">"stdout" or "stderr".</param>
    public delegate void OutputHandler(string line, string streamName);
    /// <summary>
    /// Callback to receive a structured engine event decoded from the child output.
    /// </summary>
    public delegate void EventHandler(Dictionary<string, object?> evt);
    /// <summary>
    /// Provider used to gather user input when a prompt event is seen.
    /// Return value is written to the child's stdin with a trailing newline.
    /// </summary>
    public delegate string? StdinProvider();

    /// <summary>
    /// Execute a command line and stream output until completion or cancellation.
    /// </summary>
    /// <param name="commandParts">Executable followed by its arguments. Must contain at least one item (the executable).</param>
    /// <param name="opTitle">Human-readable operation title used in Shared.IO.Diagnostics.</param>
    /// <param name="onOutput">Optional callback for stdout/stderr lines.</param>
    /// <param name="onEvent">Optional callback for structured events.</param>
    /// <param name="stdinProvider">Optional provider for prompt responses.</param>
    /// <param name="envOverrides">Optional environment variables to inject/override for the child.</param>
    /// <param name="cancellationToken">Token to abort execution.</param>
    /// <returns>True on zero exit code; false otherwise.</returns>
    public bool Execute(
        IList<string> commandParts,
        string opTitle,
        OutputHandler? onOutput = null,
        EventHandler? onEvent = null,
        StdinProvider? stdinProvider = null,
        IDictionary<string, object?>? envOverrides = null,
        System.Threading.CancellationToken cancellationToken = default
    ) {
        if (commandParts.Count < 1) {
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
            Shared.IO.Diagnostics.Log(string.Empty);
            Shared.IO.Diagnostics.Log("Executing command:");
            Shared.IO.Diagnostics.Log("  " + FormatCommand(commandParts));
            Shared.IO.Diagnostics.Log($"  cwd: {System.IO.Directory.GetCurrentDirectory()}");
            if (envOverrides is { Count: > 0 }) {
                Shared.IO.Diagnostics.Log("  env overrides:");
                foreach (KeyValuePair<string, object?> kv in envOverrides) {
                    Shared.IO.Diagnostics.Log($"    {kv.Key}={kv.Value}");
                }
            }
        } catch (System.UnauthorizedAccessException) {
            // Ignore: Directory.GetCurrentDirectory() can throw if the user lacks permissions to the current path
        } catch (System.NotSupportedException) {
            // Ignore: Directory.GetCurrentDirectory() can throw on unsupported path formats
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

        using System.Diagnostics.Process proc = new System.Diagnostics.Process();
        proc.StartInfo = psi;
        proc.EnableRaisingEvents = true;

        // todo fix captured variable used in outer scope 'q'
        using var q = new System.Collections.Concurrent.BlockingCollection<(string stream, string line)>(boundedCapacity: 1000);

        System.Diagnostics.DataReceivedEventHandler outHandler = (_, e) => {
            if (e.Data != null) {
                q.Add(("stdout", e.Data), cancellationToken);
            }
        };
        System.Diagnostics.DataReceivedEventHandler errHandler = (_, e) => {
            if (e.Data != null) {
                q.Add(("stderr", e.Data), cancellationToken);
            }
        };

        using var job = System.OperatingSystem.IsWindows() ? new Utils.JobObject() : null;

        try {
            if (!proc.Start()) {
                throw new System.InvalidOperationException("Failed to start process");
            }

            if (job != null) {
                job.AddProcess(proc);
            }

            proc.OutputDataReceived += outHandler;
            proc.ErrorDataReceived += errHandler;
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            bool awaitingPrompt = false;

            void SendToChild(string? text) {
                try {
                    proc.StandardInput.WriteLine(text ?? string.Empty);
                    proc.StandardInput.Flush();
                }
                catch (System.IO.IOException ex) {
                    Shared.IO.Diagnostics.Bug("[ProcessRunner.cs::Execute()] IO error writing to child stdin: " + ex);
                }
                catch (System.ObjectDisposedException ex) {
                    Shared.IO.Diagnostics.Bug("[ProcessRunner.cs::Execute()] Child stdin disposed while writing: " + ex);
                }
                catch (System.InvalidOperationException ex) {
                    Shared.IO.Diagnostics.Bug("[ProcessRunner.cs::Execute()] Child stdin unavailable while writing: " + ex);
                }
            }

            string? HandleLine(string line, string streamName) {
                onOutput?.Invoke(line, streamName);
                return null; // Note: In your original code this always returned null. Ensure this matches your intent.
            }

            while (!proc.HasExited) {
                if (cancellationToken.IsCancellationRequested) {
                    TryTerminate(proc);
                    onEvent?.Invoke(new Dictionary<string, object?>
                        { ["event"] = "end", ["success"] = false, ["exit_code"] = 130 });
                    return false;
                }

                if (!q.TryTake(out (string stream, string line) item, 100)) {
                    continue;
                }

                string? promptMsg = HandleLine(item.line, item.stream);
                if (promptMsg != null) {
                    awaitingPrompt = true;
                }

                if (!awaitingPrompt || proc.HasExited) continue;
                string? ans = string.Empty;
                try {
                    ans = stdinProvider?.Invoke();
                } catch (System.IO.IOException ex) {
                    Shared.IO.Diagnostics.Bug("[ProcessRunner] IO error in stdinProvider while awaiting prompt: " + ex.Message);
                } catch (System.InvalidOperationException ex) {
                    Shared.IO.Diagnostics.Bug("[ProcessRunner] Invalid operation in stdinProvider while awaiting prompt: " + ex.Message);
                }

                SendToChild(ans);
                awaitingPrompt = false;
            }

            // Drain any remaining
            while (q.TryTake(out (string stream, string line) item)) {
                string? promptMsg = HandleLine(item.line, item.stream);
                if (promptMsg == null) continue;

                string? ans = string.Empty;
                try {
                    ans = stdinProvider?.Invoke();
                } catch (System.IO.IOException ex) {
                    Shared.IO.Diagnostics.Bug("[ProcessRunner] IO error in stdinProvider while draining output: " + ex.Message);
                } catch (System.InvalidOperationException ex) {
                    Shared.IO.Diagnostics.Bug("[ProcessRunner] Invalid operation in stdinProvider while draining output: " + ex.Message);
                }

                SendToChild(ans);
            }

            int rc = proc.ExitCode;
            bool success = rc == 0;
            onEvent?.Invoke(
                new Dictionary<string, object?> { ["event"] = "end", ["success"] = success, ["exit_code"] = rc });
            return success;
        } catch (System.OperationCanceledException ex) {
            Shared.IO.Diagnostics.Bug("[ProcessRunner::Execute()] Operation cancelled: " + ex.Message);
            TryTerminate(proc);
            onEvent?.Invoke(new Dictionary<string, object?>
                { ["event"] = "end", ["success"] = false, ["exit_code"] = 130 });
            return false;
        } catch (System.IO.FileNotFoundException ex) {
            Shared.IO.Diagnostics.Bug("[ProcessRunner::Execute()] Command or script not found: " + ex.Message);
            onEvent?.Invoke(new Dictionary<string, object?>
                { ["event"] = "error", ["kind"] = "FileNotFoundError", ["message"] = "Command or script not found." });
            return false;
        } catch (System.ComponentModel.Win32Exception ex) {
            // Catches OS-level process failures (e.g., Access Denied, bad executable format)
            Shared.IO.Diagnostics.Bug("[ProcessRunner::Execute()] OS error starting or running process: " + ex.Message);
            onEvent?.Invoke(new Dictionary<string, object?>
                { ["event"] = "error", ["kind"] = "Win32Exception", ["message"] = ex.Message });
            return false;
        } catch (System.InvalidOperationException ex) {
            // Catches bad process state operations (e.g., trying to read ExitCode before it exits, though HasExited check mitigates this)
            Shared.IO.Diagnostics.Bug("[ProcessRunner::Execute()] Invalid process state: " + ex.Message);
            onEvent?.Invoke(new Dictionary<string, object?>
                { ["event"] = "error", ["kind"] = "InvalidOperation", ["message"] = ex.Message });
            return false;
        }
        finally {
            try {
                proc.OutputDataReceived -= outHandler;
                proc.ErrorDataReceived -= errHandler;
            }
            catch (System.ObjectDisposedException) {
                // Unsubscribing from events doesn't throw under normal circumstances in .NET,
                // but ObjectDisposedException can occur if the underlying Component is deeply disposed.
                Shared.IO.Diagnostics.Bug("[ProcessRunner::Execute()] Process disposed while unsubscribing from events.");
            }
        }
    }

}

