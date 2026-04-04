
using MoonSharp.Interpreter;
using EngineNet.Core.Services;

namespace EngineNet.ScriptEngines.Global.SdkModule;

/// <summary>
/// Process execution functionality for Lua scripts.
/// Provides secure process execution with validation.
/// </summary>
internal static class ProcessExecution {

    // Heuristic helpers for path-like arguments and validation
    private static bool LooksLikePath(string s) {
        if (string.IsNullOrWhiteSpace(s)) return false;
        string v = s.Trim();
        // Ignore obvious URLs
        if (v.Contains("://", StringComparison.Ordinal)) return false;
        // key=value -> analyze the value part
        int eq = v.IndexOf('=');
        if (eq > 0 && eq < v.Length - 1) {
            v = v.Substring(eq + 1).Trim('"', '\'', ' ');
        }
        // Strip quotes
        v = v.Trim('"', '\'');
        if (System.IO.Path.IsPathRooted(v)) return true;
        if (v.Contains(System.IO.Path.DirectorySeparatorChar) || v.Contains(System.IO.Path.AltDirectorySeparatorChar)) return true;
        if (v.StartsWith("./") || v.StartsWith(".\\") || v.StartsWith("..") || v.StartsWith("~")) return true;
        if (v.Length >= 2 && char.IsLetter(v[0]) && v[1] == ':') return true; // Windows drive
        int dot = v.LastIndexOf('.');
        if (dot > 0 && dot < v.Length - 1 && dot >= v.Length - 8) return true;
        return false;
    }

    private static bool ValidateArgPaths(IEnumerable<string> args, string? cwd) {
        if (!string.IsNullOrEmpty(cwd)) {
            if (!EngineNet.ScriptEngines.Security.IsAllowedPath(cwd)) {
                Shared.UI.EngineSdk.Error($"Access denied: working directory outside allowed areas ('{cwd}')");
                return false;
            }
        }
        foreach (string a in args) {
            if (string.IsNullOrEmpty(a)) continue;
            string candidate = a;
            int eq = candidate.IndexOf('=');
            if (eq > 0 && eq < candidate.Length - 1) {
                candidate = candidate.Substring(eq + 1);
            }
            candidate = candidate.Trim('"', '\'', ' ');
            if (!LooksLikePath(candidate)) continue;
            if (!EngineNet.ScriptEngines.Security.IsAllowedPath(candidate)) {
                Shared.UI.EngineSdk.Error($"Access denied: process argument references path outside allowed areas ('{candidate}')");
                return false;
            }
        }
        return true;
    }

    internal static DynValue RunProcess(Script lua, CommandService cs, Table commandArgs, Table? options) {
        List<string> arguments = Lua.Globals.Utils.TableToStringList(commandArgs);
        if (arguments.Count == 0) throw new ScriptRuntimeException("run_process requires at least one argument");

        string? cwd = null;
        bool captureStdout = true;
        bool captureStderr = true;
        int? timeoutMs = null;
        Dictionary<string, string> env = new();

        if (options != null) {
            DynValue v = options.Get("cwd");
            if (!v.IsNil() && v.Type == DataType.String) cwd = v.String;
            v = options.Get("capture_stdout");
            if (v.Type == DataType.Boolean) captureStdout = v.Boolean;
            v = options.Get("capture_stderr");
            if (v.Type == DataType.Boolean) captureStderr = v.Boolean;
            v = options.Get("timeout_ms");
            if (v.Type == DataType.Number) timeoutMs = (int)Math.Max(0, v.Number);
            v = options.Get("env");
            if (v.Type == DataType.Table) {
                foreach (TablePair pair in v.Table.Pairs) {
                    if (pair.Key.Type == DataType.String && pair.Value.Type == DataType.String) {
                        env[pair.Key.String] = pair.Value.String;
                    }
                }
            }
        }

        if (!ValidateArgPaths(arguments, cwd)) throw new ScriptRuntimeException("Restricted path detected");

        try {
            ProcessResult res = cs.RunProcess(arguments[0], arguments.Skip(1), cwd, env, timeoutMs, captureStdout, captureStderr);
            Table t = new Table(lua);
            t["exit_code"] = res.ExitCode;
            t["success"] = res.Success;
            if (captureStdout) t["stdout"] = res.Stdout;
            if (captureStderr) t["stderr"] = res.Stderr;
            return DynValue.NewTable(t);
        } catch (Exception ex) {
            throw new ScriptRuntimeException(ex.Message);
        }
    }

    internal static DynValue ExecProcess(Script lua, CommandService cs, Table commandArgs, Table? options, bool silentRun) {
        List<string> parts = Lua.Globals.Utils.TableToStringList(commandArgs);
        if (parts.Count == 0) throw new ScriptRuntimeException("exec requires at least one argument");

        string? cwd = null;
        bool newTerminal = false;
        bool keepOpen = false;
        bool wait = true;
        Dictionary<string, string> env = new();

        if (options != null) {
            DynValue v = options.Get("cwd");
            if (!v.IsNil() && v.Type == DataType.String) cwd = v.String;
            v = options.Get("new_terminal");
            if (v.Type == DataType.Boolean) newTerminal = v.Boolean;
            v = options.Get("keep_open");
            if (v.Type == DataType.Boolean) keepOpen = v.Boolean;
            v = options.Get("wait");
            if (v.Type == DataType.Boolean) wait = v.Boolean;
            v = options.Get("env");
            if (v.Type == DataType.Table) {
                foreach (TablePair p in v.Table.Pairs) {
                    if (p.Key.Type == DataType.String && p.Value.Type == DataType.String) {
                        env[p.Key.String] = p.Value.String;
                    }
                }
            }
        }

        if (!ValidateArgPaths(parts, cwd)) throw new ScriptRuntimeException("Restricted path detected");

        if (newTerminal) {
            return HandleNewTerminalExecution(lua, cs, parts, cwd, env, keepOpen, wait, silentRun);
        }

        return ExecInCurrentTerminal(lua, cs, parts, cwd, env, silentRun);
    }

    internal static DynValue SpawnProcess(Script lua, CommandService cs, Table commandArgs, Table? options, Core.ExternalTools.JsonToolResolver tools) {
        List<string> parts = Lua.Globals.Utils.TableToStringList(commandArgs);
        if (parts.Count == 0) throw new ScriptRuntimeException("spawn_process requires executable");
        if (!EngineNet.ScriptEngines.Security.IsApprovedExecutable(parts[0], tools)) throw new ScriptRuntimeException("Not approved");

        string? cwd = null;
        bool captureStdout = true;
        bool captureStderr = true;
        Dictionary<string, string> env = new();

        if (options != null) {
            DynValue v = options.Get("cwd");
            if (!v.IsNil() && v.Type == DataType.String) cwd = v.String;
            v = options.Get("capture_stdout");
            if (v.Type == DataType.Boolean) captureStdout = v.Boolean;
            v = options.Get("capture_stderr");
            if (v.Type == DataType.Boolean) captureStderr = v.Boolean;
            v = options.Get("env");
            if (v.Type == DataType.Table) {
                foreach (TablePair pair in v.Table.Pairs) {
                    if (pair.Key.Type == DataType.String && pair.Value.Type == DataType.String) {
                        env[pair.Key.String] = pair.Value.String;
                    }
                }
            }
        }

        if (!ValidateArgPaths(parts, cwd)) throw new ScriptRuntimeException("Restricted path detected");

        try {
            int pid = cs.SpawnProcess(parts[0], parts.Skip(1), cwd, env, captureStdout, captureStderr);
            Table t = new Table(lua);
            t["pid"] = pid;
            return DynValue.NewTable(t);
        } catch (Exception ex) {
            throw new ScriptRuntimeException(ex.Message);
        }
    }

    internal static DynValue PollProcess(Script lua, CommandService cs, int pid) {
        try {
            ProcessPollResult res = cs.PollProcess(pid);
            Table t = new Table(lua);
            t["running"] = res.Running;
            if (!res.Running) t["exit_code"] = res.ExitCode;
            t["stdout"] = res.StdoutFull;
            t["stderr"] = res.StderrFull;
            t["stdout_delta"] = res.StdoutDelta;
            t["stderr_delta"] = res.StderrDelta;
            return DynValue.NewTable(t);
        } catch (Exception ex) {
            throw new ScriptRuntimeException(ex.Message);
        }
    }

    internal static DynValue WaitProcess(Script lua, CommandService cs, int pid, int? timeoutMs) {
        try {
            // Keep parity with previous behavior: wait_process acted as a status check.
            _ = timeoutMs;
            ProcessPollResult res = cs.PollProcess(pid);
            Table t = new Table(lua);
            t["running"] = res.Running;
            if (!res.Running) t["exit_code"] = res.ExitCode;
            t["stdout"] = res.StdoutFull;
            t["stderr"] = res.StderrFull;
            t["stdout_delta"] = res.StdoutDelta;
            t["stderr_delta"] = res.StderrDelta;
            return DynValue.NewTable(t);
        } catch (Exception ex) {
            throw new ScriptRuntimeException(ex.Message);
        }
    }

    internal static DynValue CloseProcess(Script lua, CommandService cs, int pid) {
        return DynValue.NewBoolean(cs.CloseProcess(pid));
    }

    private static DynValue HandleNewTerminalExecution(Script lua, CommandService cs, List<string> parts, string? cwd, Dictionary<string, string> env, bool keepOpen, bool wait, bool silentRun) {
        try {
            // Parity fallback: when no terminal emulator is available on Unix-like systems,
            // execute in the current terminal path instead of failing.
            if ((System.OperatingSystem.IsLinux() || System.OperatingSystem.IsMacOS()) && !HasKnownTerminalEmulator()) {
                return ExecInCurrentTerminal(lua, cs, parts, cwd, env, silentRun);
            }

            ProcessResult res = cs.RunInNewTerminal(parts[0], parts.Skip(1), cwd, env, keepOpen, wait);
            Table t = new Table(lua);
            t["success"] = res.Success;
            t["exit_code"] = res.ExitCode;
            return DynValue.NewTable(t);
        } catch (Exception ex) {
            throw new ScriptRuntimeException(ex.Message);
        }
    }

    private static DynValue ExecInCurrentTerminal(Script lua, CommandService cs, List<string> parts, string? cwd, Dictionary<string, string> env, bool silentRun) {
        try {
            Dictionary<string, object?> envObj = env.ToDictionary(k => k.Key, v => (object?)v.Value);
            if (!string.IsNullOrEmpty(cwd)) {
                envObj["PWD"] = cwd;
            }

            int exitCode = -1;
            bool success = cs.ExecuteCommand(
                commandParts: parts,
                title: System.IO.Path.GetFileName(parts[0]),
                onOutput: (msg, type) => {
                    if (!silentRun) {
                        string? color = type == "stderr" ? "red" : null;
                        Shared.UI.EngineSdk.Print(msg, color, true);
                        Shared.Diagnostics.Log($"[ProcessRunner][{type}] {msg}");
                    }
                },
                onEvent: evt => {
                    if (evt.TryGetValue("event", out object? ev) && string.Equals(ev?.ToString(), "end", System.StringComparison.OrdinalIgnoreCase)) {
                        if (evt.TryGetValue("exit_code", out object? code) && int.TryParse(code?.ToString(), out int parsed)) {
                            exitCode = parsed;
                        }
                    }
                },
                envOverrides: envObj
            );

            if (!success && exitCode == -1) {
                exitCode = 1;
            }

            Table result = new Table(lua);
            result["exit_code"] = exitCode >= 0 ? exitCode : (success ? 0 : 1);
            result["success"] = success && (exitCode == 0 || exitCode == -1);
            return DynValue.NewTable(result);
        } catch (Exception ex) {
            throw new ScriptRuntimeException(ex.Message);
        }
    }

    private static bool HasKnownTerminalEmulator() {
        if (System.OperatingSystem.IsWindows()) {
            return true;
        }

        string[] candidates = new[] {
            "/usr/bin/gnome-terminal",
            "/usr/bin/konsole",
            "/usr/bin/xterm",
            "/usr/bin/alacritty",
            "/usr/bin/xfce4-terminal"
        };

        foreach (string candidate in candidates) {
            if (System.IO.File.Exists(candidate)) {
                return true;
            }
        }

        return false;
    }
}
