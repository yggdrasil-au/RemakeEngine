using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

using EngineNet.Core.Utils;

namespace EngineNet.Core.Services;

public class CommandService {
    private readonly CommandBuilder _builder = new CommandBuilder();
    private readonly ProcessRunner _runner = new ProcessRunner();

    // Tracks spawned background processes
    private readonly ConcurrentDictionary<int, ManagedProcess> _spawnedProcesses = new ConcurrentDictionary<int, ManagedProcess>();
    private int _nextPid;

    public List<string> BuildCommand(string currentGame, Dictionary<string, Data.GameModuleInfo> games, IDictionary<string, object?> engineData, IDictionary<string, object?> op, Data.PromptAnswers promptAnswers) {
        return _builder.Build(currentGame, games, engineData, op, promptAnswers);
    }

    public bool ExecuteCommand(IList<string> commandParts, string title, ProcessRunner.OutputHandler? onOutput = null, ProcessRunner.EventHandler? onEvent = null, ProcessRunner.StdinProvider? stdinProvider = null, IDictionary<string, object?>? envOverrides = null, CancellationToken cancellationToken = default(CancellationToken)) {
        return _runner.Execute(commandParts, title, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdinProvider, envOverrides: envOverrides, cancellationToken: cancellationToken);
    }

    // --- Centralized Process Execution Methods ---

    public ProcessResult RunProcess(string executable, IEnumerable<string> args, string? cwd, IDictionary<string, string>? env, int? timeoutMs, bool captureStdout, bool captureStderr) {
        ProcessStartInfo psi = CreateStandardPsi(executable, args, cwd, env);
        psi.RedirectStandardOutput = captureStdout;
        psi.RedirectStandardError = captureStderr;

        StringBuilder stdoutBuilder = new StringBuilder();
        StringBuilder stderrBuilder = new StringBuilder();

        using Process process = new Process();
        process.StartInfo = psi;

        if (captureStdout) {
            process.OutputDataReceived += (_, e) => { if (e.Data != null) { lock (stdoutBuilder) { stdoutBuilder.AppendLine(e.Data); } } };
        }
        if (captureStderr) {
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) { lock (stderrBuilder) { stderrBuilder.AppendLine(e.Data); } } };
        }

        try {
            process.Start();
            if (captureStdout) process.BeginOutputReadLine();
            if (captureStderr) process.BeginErrorReadLine();

            if (timeoutMs.HasValue) {
                if (!process.WaitForExit(timeoutMs.Value)) {
                    try { process.Kill(entireProcessTree: true); } catch (Exception ex) { Shared.IO.Diagnostics.Bug($"[CommandService::RunProcess()] Failed to kill timed-out process '{executable}': {ex}"); /* ignore */ }
                    throw new Exception($"Process '{executable}' timed out after {timeoutMs.Value} ms");
                }
            } else {
                process.WaitForExit();
            }
        } catch (Exception ex) {
            Shared.IO.Diagnostics.Bug($"[CommandService::RunProcess()] Failed to run process '{executable}': {ex}");
            throw new Exception($"Failed to run process '{executable}': {ex.Message}");
        }

        return new ProcessResult {
            ExitCode = process.ExitCode,
            Success = process.ExitCode == 0,
            Stdout = captureStdout ? stdoutBuilder.ToString() : string.Empty,
            Stderr = captureStderr ? stderrBuilder.ToString() : string.Empty
        };
    }

    public int SpawnProcess(string executable, IEnumerable<string> args, string? cwd, IDictionary<string, string>? env, bool captureStdout, bool captureStderr) {
        ProcessStartInfo psi = CreateStandardPsi(executable, args, cwd, env);
        psi.RedirectStandardOutput = captureStdout;
        psi.RedirectStandardError = captureStderr;

        ManagedProcess mp = new ManagedProcess();
        Process p = new Process { StartInfo = psi, EnableRaisingEvents = true };

        if (captureStdout) {
            p.OutputDataReceived += (_, e) => {
                if (e.Data != null) {
                    lock (mp.StdoutLock) { mp.Stdout.AppendLine(e.Data); }
                }
            };
        }

        if (captureStderr) {
            p.ErrorDataReceived += (_, e) => {
                if (e.Data != null) {
                    lock (mp.StderrLock) { mp.Stderr.AppendLine(e.Data); }
                }
            };
        }

        p.Exited += (_, _) => {
            try { mp.ExitTcs.TrySetResult(p.ExitCode); } catch (Exception ex) { Shared.IO.Diagnostics.Bug($"[CommandService::SpawnProcess()] Failed setting ExitTcs result for '{executable}': {ex}"); /* ignore */ }
        };

        if (!p.Start()) {
            throw new Exception($"Failed to start process '{executable}'");
        }

        if (captureStdout) p.BeginOutputReadLine();
        if (captureStderr) p.BeginErrorReadLine();

        mp.Process = p;
        int id = Interlocked.Increment(ref _nextPid);
        _spawnedProcesses[id] = mp;
        return id;
    }

    public ProcessPollResult PollProcess(int pid) {
        if (!_spawnedProcesses.TryGetValue(pid, out ManagedProcess? mp)) {
            throw new Exception($"Unknown process id {pid}");
        }
        return ExtractProcessState(mp);
    }

    public ProcessPollResult WaitProcess(int pid, int? timeoutMs) {
        if (!_spawnedProcesses.TryGetValue(pid, out ManagedProcess? mp)) {
            throw new Exception($"Unknown process id {pid}");
        }

        try {
            if (timeoutMs.HasValue) {
                mp.ExitTcs.Task.Wait(timeoutMs.Value);
            } else {
                mp.ExitTcs.Task.Wait(Timeout.Infinite);
            }
        } catch (Exception ex) {
            Shared.IO.Diagnostics.Bug($"[CommandService::WaitProcess()] Wait failed for pid {pid}: {ex}");
            /* ignore wait errors */
        }

        return ExtractProcessState(mp);
    }

    public bool CloseProcess(int pid) {
        if (!_spawnedProcesses.TryRemove(pid, out ManagedProcess? mp)) return false;
        try { if (!mp.Process.HasExited) mp.Process.Kill(true); } catch (Exception ex) { Shared.IO.Diagnostics.Bug($"[CommandService] CloseProcess kill catch triggered for pid {pid}: {ex}"); /* ignore */ }
        try { mp.Process.Dispose(); } catch (Exception ex) { Shared.IO.Diagnostics.Bug($"[CommandService] CloseProcess dispose catch triggered for pid {pid}: {ex}"); /* ignore */ }
        return true;
    }

    public bool LaunchDetached(string executable, IEnumerable<string> args, string? cwd) {
        return LaunchDetached(executable, args, cwd, new DetachedLaunchOptions {
            UseShellExecute = true
        });
    }

    public bool LaunchDetached(string executable, IEnumerable<string> args, string? cwd, DetachedLaunchOptions options) {
        try {
            ProcessStartInfo psi = new ProcessStartInfo {
                FileName = executable,
                WorkingDirectory = cwd ?? string.Empty,
                UseShellExecute = options.UseShellExecute
            };
            if (options.CreateNoWindow.HasValue) {
                psi.CreateNoWindow = options.CreateNoWindow.Value;
            }

            if (options.WindowStyle.HasValue) {
                psi.WindowStyle = options.WindowStyle.Value;
            }

            foreach (string arg in args) {
                psi.ArgumentList.Add(arg);
            }
            Process.Start(psi);
            return true;
        } catch (Exception ex) {
            Shared.IO.Diagnostics.Bug($"[CommandService] Error launching detached process '{executable}': {ex.Message}");
            return false;
        }
    }

    public void OpenFolder(string path) {
        try {
            ProcessStartInfo psi = new ProcessStartInfo { UseShellExecute = true };
            if (OperatingSystem.IsWindows()) {
                psi.FileName = "explorer";
                psi.Arguments = $"\"{path}\"";
            } else if (OperatingSystem.IsMacOS()) {
                psi.FileName = "open";
                psi.Arguments = $"\"{path}\"";
            } else {
                psi.FileName = "xdg-open";
                psi.Arguments = $"\"{path}\"";
            }
            Process.Start(psi);
        } catch (Exception ex) {
            Shared.IO.Diagnostics.Bug($"[CommandService] Failed to open folder: {ex.Message}");
        }
    }

    public ProcessResult RunInNewTerminal(string executable, IEnumerable<string> args, string? cwd, IDictionary<string, string>? env, bool keepOpen, bool wait) {
        int exitCode = 0;
        try {
            if (OperatingSystem.IsWindows()) {
                ProcessStartInfo psi = new ProcessStartInfo {
                    UseShellExecute = true,
                    FileName = executable,
                    Arguments = string.Join(" ", args.Select(QuoteArg)),
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal,
                };
                if (!string.IsNullOrEmpty(cwd)) { psi.WorkingDirectory = cwd; }
                if (env != null) {
                    foreach (var kv in env) { psi.Environment[kv.Key] = kv.Value; }
                }

                using Process p = new Process();
                p.StartInfo = psi;
                p.Start();
                if (wait) {
                    p.WaitForExit();
                    exitCode = p.ExitCode;
                }
            } else if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()) {
                string cmdline = string.Join(" ", args.Prepend(executable).Select(QuoteArg));
                string? term = FindTerminalEmulator();

                if (term != null) {
                    ProcessStartInfo psi = new ProcessStartInfo {
                        UseShellExecute = false,
                        CreateNoWindow = false,
                        FileName = term
                    };

                    if (term.Contains("gnome-terminal")) {
                        psi.ArgumentList.Add("--");
                        psi.ArgumentList.Add("bash");
                        psi.ArgumentList.Add("-lc");
                        psi.ArgumentList.Add(keepOpen ? cmdline + "; exec bash" : cmdline);
                    } else {
                        psi.ArgumentList.Add("-e");
                        psi.ArgumentList.Add(keepOpen ? $"bash -lc \"{cmdline}; exec bash\"" : $"bash -lc \"{cmdline}\"");
                    }

                    if (!string.IsNullOrEmpty(cwd)) { psi.WorkingDirectory = cwd; }
                    using Process p = new Process();
                    p.StartInfo = psi;
                    p.Start();
                    if (wait) {
                        p.WaitForExit();
                        exitCode = p.ExitCode;
                    }
                } else {
                    // Fallback or error
                    Shared.IO.Diagnostics.Bug("[CommandService] No terminal emulator found on Linux/macOS.");
                    return new ProcessResult { Success = false, ExitCode = -1 };
                }
            }
        } catch (Exception ex) {
            Shared.IO.Diagnostics.Bug($"[CommandService] Failed to start new terminal: {ex.Message}");
            return new ProcessResult { Success = false, ExitCode = -1 };
        }

        return new ProcessResult { Success = exitCode == 0, ExitCode = exitCode };
    }

    // --- Helpers ---

    private ProcessStartInfo CreateStandardPsi(string executable, IEnumerable<string> args, string? cwd, IDictionary<string, string>? env) {
        ProcessStartInfo psi = new ProcessStartInfo {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string arg in args) {
            psi.ArgumentList.Add(arg);
        }
        if (!string.IsNullOrEmpty(cwd)) {
            psi.WorkingDirectory = cwd;
        }
        if (env != null) {
            foreach (var kvp in env) {
                psi.Environment[kvp.Key] = kvp.Value;
            }
        }
        return psi;
    }

    private ProcessPollResult ExtractProcessState(ManagedProcess mp) {
        bool running = !mp.Process.HasExited;
        ProcessPollResult result = new ProcessPollResult {
            Running = running,
            ExitCode = running ? null : mp.Process.ExitCode
        };

        lock (mp.StdoutLock) {
            result.StdoutFull = mp.Stdout.ToString();
            int len = Math.Max(0, mp.Stdout.Length - mp.StdoutCursor);
            result.StdoutDelta = len == 0 ? string.Empty : mp.Stdout.ToString(mp.StdoutCursor, len);
            mp.StdoutCursor = mp.Stdout.Length;
        }

        lock (mp.StderrLock) {
            result.StderrFull = mp.Stderr.ToString();
            int len = Math.Max(0, mp.Stderr.Length - mp.StderrCursor);
            result.StderrDelta = len == 0 ? string.Empty : mp.Stderr.ToString(mp.StderrCursor, len);
            mp.StderrCursor = mp.Stderr.Length;
        }

        return result;
    }

    private string QuoteArg(string arg) {
        if (string.IsNullOrEmpty(arg)) return "\"\"";
        if (arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }

    private string? FindTerminalEmulator() {
        string[] candidates = { "/usr/bin/gnome-terminal", "/usr/bin/konsole", "/usr/bin/xterm", "/usr/bin/alacritty", "/usr/bin/xfce4-terminal" };
        return candidates.FirstOrDefault(System.IO.File.Exists);
    }

    // --- Internal State Classes ---

    private class ManagedProcess {
        internal Process Process { get; set; } = null!;
        internal StringBuilder Stdout { get; } = new StringBuilder();
        internal StringBuilder Stderr { get; } = new StringBuilder();
        internal object StdoutLock { get; } = new object();
        internal object StderrLock { get; } = new object();
        internal int StdoutCursor { get; set; }
        internal int StderrCursor { get; set; }
        internal TaskCompletionSource<int> ExitTcs { get; } = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

public class ProcessResult {
    public int ExitCode { get; set; }
    public bool Success { get; set; }
    public string Stdout { get; set; } = string.Empty;
    public string Stderr { get; set; } = string.Empty;
}

public class ProcessPollResult {
    public bool Running { get; set; }
    public int? ExitCode { get; set; }
    public string StdoutFull { get; set; } = string.Empty;
    public string StderrFull { get; set; } = string.Empty;
    public string StdoutDelta { get; set; } = string.Empty;
    public string StderrDelta { get; set; } = string.Empty;
}

public sealed class DetachedLaunchOptions {
    public bool UseShellExecute { get; set; } = true;
    public bool? CreateNoWindow { get; set; }
    public ProcessWindowStyle? WindowStyle { get; set; }
}
