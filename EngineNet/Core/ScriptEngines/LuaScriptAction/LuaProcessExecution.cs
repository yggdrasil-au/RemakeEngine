using MoonSharp.Interpreter;

using System.Collections.Generic;

namespace EngineNet.Core.ScriptEngines.LuaModules;

/// <summary>
/// Process execution functionality for Lua scripts.
/// Provides secure process execution with validation.
/// </summary>
internal static class LuaProcessExecution {
    // Lightweight managed process support for non-blocking process execution from Lua.
    // Processes are stored in a concurrent dictionary and can be polled/waited from Lua.
    private class ManagedProcess {
        internal System.Diagnostics.Process Process { get; set; } = null!;
        internal System.Text.StringBuilder Stdout { get; } = new System.Text.StringBuilder();
        internal System.Text.StringBuilder Stderr { get; } = new System.Text.StringBuilder();

        // NEW: locks to make System.Text.StringBuilder access thread-safe
        internal object StdoutLock { get; } = new object();
        internal object StderrLock { get; } = new object();

        // NEW: cursors so we can return deltas safely (without breaking existing API)
        internal int StdoutCursor { get; set; } = 0;
        internal int StderrCursor { get; set; } = 0;

        internal System.Threading.Tasks.TaskCompletionSource<int> ExitTcs { get; } =
            new System.Threading.Tasks.TaskCompletionSource<int>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // pid -> ManagedProcess
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, ManagedProcess> s_processes = new System.Collections.Concurrent.ConcurrentDictionary<int, ManagedProcess>();
    private static int s_nextPid = 0;

    internal static void AddProcessExecution(Table sdk, Script lua, Tools.IToolResolver tools) {
        // exec(args[, options]) -> { success=bool, exit_code=int }
        // options: { cwd=string, env=table, new_terminal=bool, keep_open=bool, title=string, wait=bool }
        sdk["exec"] = DynValue.NewCallback((ctx, args) => {
            if (args.Count < 1 || args[0].Type != DataType.Table) {
                throw new ScriptRuntimeException("exec expects first argument to be an array/table of strings (command + args)");
            }

            Table commandArgs = args[0].Table;
            Table? options = args.Count > 1 && args[1].Type == DataType.Table ? args[1].Table : null;

            // Security: Validate command before execution
            List<string> parts = LuaUtilities.TableToStringList(commandArgs);
            if (parts.Count == 0) {
                throw new ScriptRuntimeException("exec requires at least one argument (executable)");
            }

            if (!LuaSecurity.IsApprovedExecutable(parts[0], tools)) {
                throw new ScriptRuntimeException($"Executable '{parts[0]}' is not in the approved tools list. Use tool() function to resolve approved tools.");
            }

            return ExecProcess(lua, commandArgs, options);
        });

        sdk["run_process"] = DynValue.NewCallback((ctx, args) => {
            if (args.Count < 1 || args[0].Type != DataType.Table) {
                throw new ScriptRuntimeException("run_process expects argument table");
            }

            Table commandArgs = args[0].Table;
            Table? options = args.Count > 1 && args[1].Type == DataType.Table ? args[1].Table : null;

            // Security: Validate command before execution
            List<string> parts = LuaUtilities.TableToStringList(commandArgs);
            if (parts.Count == 0) {
                throw new ScriptRuntimeException("run_process requires at least one argument (executable)");
            }

            if (!LuaSecurity.IsApprovedExecutable(parts[0], tools)) {
                throw new ScriptRuntimeException($"Executable '{parts[0]}' is not in the approved tools list. Use tool() function to resolve approved tools.");
            }

            return RunProcess(lua, commandArgs, options);
        });

        // Register non-blocking spawn/poll/wait/close helpers for Lua scripts
        sdk["spawn_process"] = DynValue.NewCallback((ctx, args) => {
            if (args.Count < 1 || args[0].Type != DataType.Table) {
                throw new ScriptRuntimeException("spawn_process expects a table of command parts");
            }
            Table cmdTable = args[0].Table;
            Table? options = null;
            if (args.Count > 1 && args[1].Type == DataType.Table) {
                options = args[1].Table;
            }
            return SpawnProcess(lua, cmdTable, options, tools);
        });

        sdk["poll_process"] = DynValue.NewCallback((ctx, args) => {
            if (args.Count < 1 || args[0].Type != DataType.Number) {
                throw new ScriptRuntimeException("poll_process requires a numeric process id");
            }
            int pid = (int)args[0].Number;
            return PollProcess(lua, pid);
        });

        sdk["wait_process"] = DynValue.NewCallback((ctx, args) => {
            if (args.Count < 1 || args[0].Type != DataType.Number) {
                throw new ScriptRuntimeException("wait_process requires a numeric process id");
            }
            int pid = (int)args[0].Number;
            int? timeoutMs = null;
            if (args.Count > 1 && args[1].Type == DataType.Number) {
                timeoutMs = (int)args[1].Number;
            }
            return WaitProcess(lua, pid, timeoutMs);
        });

        sdk["close_process"] = DynValue.NewCallback((ctx, args) => {
            if (args.Count < 1 || args[0].Type != DataType.Number) {
                throw new ScriptRuntimeException("close_process requires a numeric process id");
            }
            int pid = (int)args[0].Number;
            return CloseProcess(lua, pid);
        });
    }

    private static DynValue SpawnProcess(Script lua, Table commandArgs, Table? options, Tools.IToolResolver tools) {
        List<string> parts = LuaUtilities.TableToStringList(commandArgs);
        if (parts.Count == 0) throw new ScriptRuntimeException("spawn_process requires at least one argument (executable path)");
        if (!LuaSecurity.IsApprovedExecutable(parts[0], tools)) throw new ScriptRuntimeException($"Executable '{parts[0]}' is not approved");

        System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
            FileName = parts[0],
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        for (int i = 1; i < parts.Count; i++) psi.ArgumentList.Add(parts[i]);

        if (options != null) {
            DynValue cwd = options.Get("cwd");
            if (!cwd.IsNil() && cwd.Type == DataType.String) psi.WorkingDirectory = cwd.String;
            DynValue captureStdout = options.Get("capture_stdout");
            if (captureStdout.Type == DataType.Boolean && !captureStdout.Boolean) psi.RedirectStandardOutput = false;
            DynValue captureStderr = options.Get("capture_stderr");
            if (captureStderr.Type == DataType.Boolean && !captureStderr.Boolean) psi.RedirectStandardError = false;
            DynValue envOpt = options.Get("env");
            if (envOpt.Type == DataType.Table) {
                foreach (TablePair pair in envOpt.Table.Pairs) {
                    if (pair.Key.Type == DataType.String && pair.Value.Type == DataType.String) {
                        psi.Environment[pair.Key.String] = pair.Value.String;
                    }
                }
            }
        }

        ManagedProcess mp = new ManagedProcess();
        System.Diagnostics.Process p = new System.Diagnostics.Process();
        p.StartInfo = psi;
        p.EnableRaisingEvents = true;

        if (psi.RedirectStandardOutput) {
            p.OutputDataReceived += (_, e) => {
                if (e.Data != null) {
                    lock (mp.StdoutLock) {
                        mp.Stdout.AppendLine(e.Data);
                    }
                }
            };
        }
        if (psi.RedirectStandardError) {
            p.ErrorDataReceived += (_, e) => {
                if (e.Data != null) {
                    lock (mp.StderrLock) {
                        mp.Stderr.AppendLine(e.Data);
                    }
                }
            };
        }
        p.Exited += (_, __) => {
            try { mp.ExitTcs.TrySetResult(p.ExitCode); }  catch {
#if DEBUG
            System.Console.WriteLine($"[LuaProcessExecution] Error setting exit code for process '{psi.FileName}'");
#endif
        }
        };

        try {
            if (!p.Start()) throw new ScriptRuntimeException($"Failed to start process '{psi.FileName}'");
            if (psi.RedirectStandardOutput) p.BeginOutputReadLine();
            if (psi.RedirectStandardError) p.BeginErrorReadLine();
        } catch (System.Exception ex) {
            throw new ScriptRuntimeException($"Failed to spawn process: {ex.Message}");
        }

        mp.Process = p;
        int id = System.Threading.Interlocked.Increment(ref s_nextPid);
        s_processes[id] = mp;

        Table t = new Table(lua);
        t["pid"] = id;
        return DynValue.NewTable(t);
    }

    private static DynValue PollProcess(Script lua, int pid) {
        if (!s_processes.TryGetValue(pid, out var mp)) throw new ScriptRuntimeException($"Unknown process id {pid}");
        Table t = new Table(lua);
        bool running = !mp.Process.HasExited;
        t["running"] = running;
        if (!running) {
            t["exit_code"] = mp.Process.ExitCode;
        }

        // FULL buffers (compat) + DELTAS (new)
        string stdoutFull, stderrFull, stdoutDelta, stderrDelta;

        lock (mp.StdoutLock) {
            stdoutFull = mp.Stdout.ToString();
            int len = mp.Stdout.Length - mp.StdoutCursor;
            if (len < 0) len = 0;
            stdoutDelta = len == 0 ? string.Empty : mp.Stdout.ToString(mp.StdoutCursor, len);
            mp.StdoutCursor = mp.Stdout.Length;
        }
        lock (mp.StderrLock) {
            stderrFull = mp.Stderr.ToString();
            int len = mp.Stderr.Length - mp.StderrCursor;
            if (len < 0) len = 0;
            stderrDelta = len == 0 ? string.Empty : mp.Stderr.ToString(mp.StderrCursor, len);
            mp.StderrCursor = mp.Stderr.Length;
        }

        t["stdout"] = stdoutFull;
        t["stderr"] = stderrFull;
        t["stdout_delta"] = stdoutDelta;   // optional: use for incremental consumption
        t["stderr_delta"] = stderrDelta;
        return DynValue.NewTable(t);
    }

    private static DynValue WaitProcess(Script lua, int pid, int? timeoutMs) {
        if (!s_processes.TryGetValue(pid, out var mp)) throw new ScriptRuntimeException($"Unknown process id {pid}");
        bool finished;
        try {
            if (timeoutMs.HasValue) {
                finished = mp.ExitTcs.Task.Wait(timeoutMs.Value);
            } else {
                finished = mp.ExitTcs.Task.Wait(System.Threading.Timeout.Infinite);
            }
        } catch (System.Exception) {
            finished = mp.Process.HasExited;
        }
        Table t = new Table(lua);
        bool running = !mp.Process.HasExited;
        t["running"] = running;
        if (!running) t["exit_code"] = mp.Process.ExitCode;

        // Return the remaining tail as delta, plus full buffers for compatibility
        string stdoutFull, stderrFull, stdoutTail, stderrTail;
        lock (mp.StdoutLock) {
            stdoutFull = mp.Stdout.ToString();
            int len = mp.Stdout.Length - mp.StdoutCursor;
            if (len < 0) len = 0;
            stdoutTail = len == 0 ? string.Empty : mp.Stdout.ToString(mp.StdoutCursor, len);
            mp.StdoutCursor = mp.Stdout.Length;
        }
        lock (mp.StderrLock) {
            stderrFull = mp.Stderr.ToString();
            int len = mp.Stderr.Length - mp.StderrCursor;
            if (len < 0) len = 0;
            stderrTail = len == 0 ? string.Empty : mp.Stderr.ToString(mp.StderrCursor, len);
            mp.StderrCursor = mp.Stderr.Length;
        }

        t["stdout"] = stdoutFull;
        t["stderr"] = stderrFull;
        t["stdout_delta"] = stdoutTail;
        t["stderr_delta"] = stderrTail;
        return DynValue.NewTable(t);
    }

    private static DynValue CloseProcess(Script lua, int pid) {
        if (!s_processes.TryRemove(pid, out var mp)) return DynValue.NewBoolean(false);
        try { if (!mp.Process.HasExited) mp.Process.Kill(true); }  catch {
#if DEBUG
            System.Console.WriteLine($"Error .....'");
#endif
        }
        try { mp.Process.Dispose(); }  catch {
#if DEBUG
            System.Console.WriteLine($"Error .....'");
#endif
        }
        return DynValue.NewBoolean(true);
    }

    internal static DynValue RunProcess(Script lua, Table commandArgs, Table? options) {
        if (commandArgs == null) {
            throw new ScriptRuntimeException("run_process expects argument table");
        }

        List<string> arguments = LuaUtilities.TableToStringList(commandArgs);
        if (arguments.Count == 0) {
            throw new ScriptRuntimeException("run_process requires at least one argument (executable path)");
        }

        string fileName = arguments[0];
        System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        for (int i = 1; i < arguments.Count; i++) {
            psi.ArgumentList.Add(arguments[i]);
        }

        bool captureStdout = true;
        bool captureStderr = true;
        int? timeoutMs = null;
        if (options != null) {
            DynValue cwd = options.Get("cwd");
            if (!cwd.IsNil() && cwd.Type == DataType.String) {
                psi.WorkingDirectory = cwd.String;
            }

            DynValue captureStdoutOpt = options.Get("capture_stdout");
            if (captureStdoutOpt.Type == DataType.Boolean) {
                captureStdout = captureStdoutOpt.Boolean;
            }

            DynValue captureStderrOpt = options.Get("capture_stderr");
            if (captureStderrOpt.Type == DataType.Boolean) {
                captureStderr = captureStderrOpt.Boolean;
            }

            DynValue timeoutOpt = options.Get("timeout_ms");
            if (timeoutOpt.Type == DataType.Number) {
                timeoutMs = (int)System.Math.Max(0, timeoutOpt.Number);
            }

            DynValue envOpt = options.Get("env");
            if (envOpt.Type == DataType.Table) {
                foreach (TablePair pair in envOpt.Table.Pairs) {
                    if (pair.Key.Type == DataType.String && pair.Value.Type == DataType.String) {
                        psi.Environment[pair.Key.String] = pair.Value.String;
                    }
                }
            }
        }

        if (!captureStdout) {
            psi.RedirectStandardOutput = false;
        }

        if (!captureStderr) {
            psi.RedirectStandardError = false;
        }

        System.Text.StringBuilder stdoutBuilder = new System.Text.StringBuilder();
        System.Text.StringBuilder stderrBuilder = new System.Text.StringBuilder();

        using System.Diagnostics.Process process = new System.Diagnostics.Process();
        process.StartInfo = psi;
        if (captureStdout) {
            process.OutputDataReceived += (_, e) => { if (e.Data != null) { stdoutBuilder.AppendLine(e.Data); } };
        }

        if (captureStderr) {
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) { stderrBuilder.AppendLine(e.Data); } };
        }

        try {
            process.Start();
            if (captureStdout) {
                process.BeginOutputReadLine();
            }

            if (captureStderr) {
                process.BeginErrorReadLine();
            }

            if (timeoutMs.HasValue) {
                if (!process.WaitForExit(timeoutMs.Value)) {
                    try {
                        process.Kill(entireProcessTree: true);
                    }  catch {
#if DEBUG
            System.Console.WriteLine($"[LuaProcessExecution] Error killing timed-out process '{fileName}'");
#endif
        }
                    throw new ScriptRuntimeException($"Process '{fileName}' timed out after {timeoutMs.Value} ms");
                }
            } else {
                process.WaitForExit();
            }
        } catch (System.Exception ex) {
            throw new ScriptRuntimeException($"Failed to run process '{fileName}': {ex.Message}");
        }

        Table result = new Table(lua);
        result["exit_code"] = process.ExitCode;
        result["success"] = process.ExitCode == 0;
        if (captureStdout) {
            result["stdout"] = stdoutBuilder.ToString();
        }

        if (captureStderr) {
            result["stderr"] = stderrBuilder.ToString();
        }

        return DynValue.NewTable(result);
    }

    internal static DynValue ExecProcess(Script lua, Table commandArgs, Table? options) {
        if (commandArgs == null) {
            throw new ScriptRuntimeException("exec expects argument table");
        }

        List<string> parts = LuaUtilities.TableToStringList(commandArgs);
        if (parts.Count == 0) {
            throw new ScriptRuntimeException("exec requires at least one argument (executable path)");
        }

        string cwd = string.Empty;
        bool newTerminal = false;
        bool keepOpen = false;
        bool wait = true;
        string? title = null;
        Dictionary<string, object?> env = new Dictionary<string, object?>(System.StringComparer.Ordinal);

        if (options != null) {
            DynValue v;
            v = options.Get("cwd");
            if (!v.IsNil() && v.Type == DataType.String) { cwd = v.String; }

            v = options.Get("new_terminal");
            if (v.Type == DataType.Boolean) { newTerminal = v.Boolean; }

            v = options.Get("keep_open");
            if (v.Type == DataType.Boolean) { keepOpen = v.Boolean; }

            v = options.Get("wait");
            if (v.Type == DataType.Boolean) { wait = v.Boolean; }

            v = options.Get("title");
            if (v.Type == DataType.String) { title = v.String; }

            v = options.Get("env");
            if (v.Type == DataType.Table) {
                foreach (TablePair p in v.Table.Pairs) {
                    if (p.Key.Type == DataType.String) {
                        string k = p.Key.String;
                        object? val = LuaUtilities.FromDynValue(p.Value);
                        env[k] = val?.ToString();
                    }
                }
            }
        }

        // If requested to open in a new terminal window, best-effort platform specific handling
        if (newTerminal) {
            return HandleNewTerminalExecution(lua, parts, cwd, env, keepOpen, wait);
        }

        // Default: stream in current engine output using ProcessRunner
        return ExecInCurrentTerminal(lua, parts, cwd, env);
    }

    private static DynValue HandleNewTerminalExecution(Script lua, List<string> parts, string cwd, Dictionary<string, object?> env, bool keepOpen, bool wait) {
        int exitCode = 0;
        try {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) {
                // Run the target directly with a visible window; do not redirect streams
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
                    UseShellExecute = true,
                    FileName = parts[0],
                    Arguments = BuildArguments(parts, 1),
                    CreateNoWindow = false,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal,
                };
                if (!string.IsNullOrEmpty(cwd)) { psi.WorkingDirectory = cwd; }
                foreach (KeyValuePair<string, object?> kv in env) { psi.Environment[kv.Key] = kv.Value?.ToString() ?? string.Empty; }

                using System.Diagnostics.Process p = new System.Diagnostics.Process { StartInfo = psi };
                p.Start();
                if (wait) {
                    p.WaitForExit();
                    exitCode = p.ExitCode;
                } else {
                    exitCode = 0; // not waited; assume success for now
                }
            } else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX) || System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux)) {
                // Try to launch a terminal emulator; fall back to current window streaming
                string cmdline = BuildCommandLine(parts);
                string term = FindTerminalEmulator();
                if (term != string.Empty) {
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
                        UseShellExecute = false,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false,
                        RedirectStandardInput = false,
                        CreateNoWindow = false,
                    };
                    if (term.Contains("gnome-terminal", System.StringComparison.OrdinalIgnoreCase)) {
                        psi.FileName = term;
                        psi.ArgumentList.Add("--");
                        psi.ArgumentList.Add("bash");
                        psi.ArgumentList.Add("-lc");
                        psi.ArgumentList.Add(keepOpen ? cmdline + "; exec bash" : cmdline);
                    } else {
                        // xterm and others
                        psi.FileName = term;
                        psi.ArgumentList.Add("-e");
                        psi.ArgumentList.Add(keepOpen ? $"bash -lc \"{cmdline}; exec bash\"" : $"bash -lc \"{cmdline}\"");
                    }
                    if (!string.IsNullOrEmpty(cwd)) { psi.WorkingDirectory = cwd; }
                    using System.Diagnostics.Process p = new System.Diagnostics.Process { StartInfo = psi };
                    p.Start();
                    if (wait) { p.WaitForExit(); exitCode = p.ExitCode; } else { exitCode = 0; }
                } else {
                    // No terminal emulator found; stream in current window instead
                    return ExecInCurrentTerminal(lua, parts, cwd, env);
                }
            }
        } catch (System.Exception ex) {
            throw new ScriptRuntimeException($"Failed to start new terminal: {ex.Message}");
        }

        Table result = new Table(lua);
        result["exit_code"] = exitCode;
        result["success"] = exitCode == 0;
        return DynValue.NewTable(result);
    }

    internal static DynValue ExecInCurrentTerminal(Script lua, List<string> parts, string cwd, IDictionary<string, object?> env) {
        Utils.ProcessRunner runner = new Utils.ProcessRunner();
        int exit = -1;
        // Merge env overrides
        Dictionary<string, object?> envOverrides = new Dictionary<string, object?>(env, System.StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(cwd)) {
            envOverrides["PWD"] = cwd;
        }

        // Print each line through EngineSdk
        bool ok = runner.Execute(
            commandParts: parts,
            opTitle: System.IO.Path.GetFileName(parts[0]),
            onOutput: (line, stream) => {
                // Map stderr to red for visibility
                string? color = stream == "stderr" ? "red" : null;
                Core.Utils.EngineSdk.Print(line, color, true);
            },
            onEvent: (evt) => {
                if (evt.TryGetValue("event", out object? ev) && (ev?.ToString() ?? string.Empty) == "end") {
                    if (evt.TryGetValue("exit_code", out object? code) && int.TryParse(code?.ToString(), out int c)) {
                        exit = c;
                    }
                }
            },
            stdinProvider: null,
            envOverrides: envOverrides,
            cancellationToken: default
        );

        // If ProcessRunner returned false, it means execution was blocked or failed
        if (!ok && exit == -1) {
            exit = 1; // Set non-zero exit code for blocked execution
        }

        Table result = new Table(lua);
        result["exit_code"] = exit >= 0 ? exit : (ok ? 0 : 1);
        result["success"] = ok && (exit == 0 || exit == -1);
        return DynValue.NewTable(result);
    }

    private static string BuildArguments(List<string> args, int startIdx) {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = startIdx; i < args.Count; i++) {
            if (i > startIdx) sb.Append(' ');
            sb.Append(QuoteArg(args[i] ?? string.Empty));
        }
        return sb.ToString();
    }

    private static string QuoteArg(string arg) {
        if (string.IsNullOrEmpty(arg)) return "\"\"";
        if (arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }

    private static string BuildCommandLine(List<string> parts) {
        return BuildArguments(parts, 0);
    }

    private static string FindTerminalEmulator() {
        try {
            // Try common terminals
            string[] candidates = new[] { "/usr/bin/gnome-terminal", "/usr/bin/konsole", "/usr/bin/xterm", "/usr/bin/alacritty", "/usr/bin/xfce4-terminal" };
            foreach (string c in candidates) { if (System.IO.File.Exists(c)) return c; }
        } catch (System.Exception ex) {
            throw new ScriptRuntimeException($"Failed to find terminal emulator: {ex.Message}");
        }
        return string.Empty;
    }
}
