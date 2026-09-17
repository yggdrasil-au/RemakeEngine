
using MoonSharp.Interpreter;
using EngineNet.Core.Services;

namespace EngineNet.ScriptEngines.Global.SdkModule;

// todo refactor this class to be a general-purpose process execution utility for all languages, and move Lua-specific code to ModuleHelpers/Lua.ProcessExecution.cs

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
        if (v.Contains("://", comparisonType: StringComparison.Ordinal)) return false;
        // key=value -> analyze the value part
        int eq = v.IndexOf('=');
        if (eq > 0 && eq < v.Length - 1) {
            v = v.Substring(startIndex: eq + 1).Trim(trimChars: ['"', '\'', ' ']);
        }
        // Strip quotes
        v = v.Trim(trimChars: ['"', '\'']);
        if (System.IO.Path.IsPathRooted(path: v)) return true;
        if (v.Contains(System.IO.Path.DirectorySeparatorChar) || v.Contains(System.IO.Path.AltDirectorySeparatorChar)) return true;
        if (v.StartsWith("./") || v.StartsWith(".\\") || v.StartsWith("..") || v.StartsWith("~")) return true;
        if (v.Length >= 2 && char.IsLetter(c: v[index: 0]) && v[index: 1] == ':') return true; // Windows drive
        int dot = v.LastIndexOf('.');
        if (dot > 0 && dot < v.Length - 1 && dot >= v.Length - 8) return true;
        return false;
    }

    private static bool ValidateArgPaths(IEnumerable<string> args, string? cwd) {
        if (!string.IsNullOrEmpty(cwd) && !EngineNet.ScriptEngines.Security.IsAllowedPath(path: cwd)) {
            Shared.IO.UI.EngineSdk.Error($"Access denied: working directory outside allowed areas ('{cwd}')");
            return false;
        }
        foreach (string a in args) {
            if (string.IsNullOrEmpty(a)) continue;
            string candidate = a;
            int eq = candidate.IndexOf('=');
            if (eq > 0 && eq < candidate.Length - 1) {
                candidate = candidate.Substring(startIndex: eq + 1);
            }
            candidate = candidate.Trim(trimChars: ['"', '\'', ' ']);
            if (!LooksLikePath(s: candidate)) continue;
            if (EngineNet.ScriptEngines.Security.IsAllowedPath(path: candidate)) continue;
            Shared.IO.UI.EngineSdk.Error($"Access denied: process argument references path outside allowed areas ('{candidate}')");
            return false;
        }
        return true;
    }

    internal static DynValue RunProcess(Script lua, CommandService cs, Table commandArgs, Table? options) {
        List<string> arguments = Lua.Globals.Utils.TableToStringList(t: commandArgs);
        if (arguments.Count == 0) throw new ScriptRuntimeException("run_process requires at least one argument");

        string? cwd = null;
        bool captureStdout = true;
        bool captureStderr = true;
        int? timeoutMs = null;
        Dictionary<string, string> env = new();

        if (options != null) {
            DynValue v = options.Get(key: "cwd");
            if (!v.IsNil() && v.Type == DataType.String) cwd = v.String;
            v = options.Get(key: "capture_stdout");
            if (v.Type == DataType.Boolean) captureStdout = v.Boolean;
            v = options.Get(key: "capture_stderr");
            if (v.Type == DataType.Boolean) captureStderr = v.Boolean;
            v = options.Get(key: "timeout_ms");
            if (v.Type == DataType.Number) timeoutMs = (int)Math.Max(val1: 0, val2: v.Number);
            v = options.Get(key: "env");
            if (v.Type == DataType.Table) {
                foreach (TablePair pair in v.Table.Pairs) {
                    if (pair.Key.Type == DataType.String && pair.Value.Type == DataType.String) {
                        env[key: pair.Key.String] = pair.Value.String;
                    }
                }
            }
        }

        if (!ValidateArgPaths(args: arguments, cwd: cwd)) throw new ScriptRuntimeException("Restricted path detected");

        try {
            ProcessResult res = cs.RunProcess(executable: arguments[index: 0], args: arguments.Skip(count: 1), cwd: cwd, env: env, timeoutMs: timeoutMs, captureStdout: captureStdout, captureStderr: captureStderr);
            Table t = new(owner: lua) {
                [key: "exit_code"] = res.ExitCode,
                [key: "success"] = res.Success
            };
            if (captureStdout) t[key: "stdout"] = res.Stdout;
            if (captureStderr) t[key: "stderr"] = res.Stderr;
            return DynValue.NewTable(table: t);
        } catch (Exception ex) {
            throw new ScriptRuntimeException(ex.Message);
        }
    }

    internal static DynValue ExecProcess(Script lua, CommandService cs, Table commandArgs, Table? options, bool silentRun) {
        List<string> parts = Lua.Globals.Utils.TableToStringList(t: commandArgs);
        if (parts.Count == 0) throw new ScriptRuntimeException("exec requires at least one argument");

        string? cwd = null;
        bool newTerminal = false;
        bool keepOpen = false;
        bool wait = true;
        Dictionary<string, string> env = new();

        if (options != null) {
            DynValue v = options.Get(key: "cwd");
            if (!v.IsNil() && v.Type == DataType.String) cwd = v.String;
            v = options.Get(key: "new_terminal");
            if (v.Type == DataType.Boolean) newTerminal = v.Boolean;
            v = options.Get(key: "keep_open");
            if (v.Type == DataType.Boolean) keepOpen = v.Boolean;
            v = options.Get(key: "wait");
            if (v.Type == DataType.Boolean) wait = v.Boolean;
            v = options.Get(key: "env");
            if (v.Type == DataType.Table) {
                foreach (TablePair p in v.Table.Pairs) {
                    if (p.Key.Type == DataType.String && p.Value.Type == DataType.String) {
                        env[key: p.Key.String] = p.Value.String;
                    }
                }
            }
        }

        if (!ValidateArgPaths(args: parts, cwd: cwd)) throw new ScriptRuntimeException("Restricted path detected");

        if (newTerminal) {
            return HandleNewTerminalExecution(lua: lua, cs: cs, parts: parts, cwd: cwd, env: env, keepOpen: keepOpen, wait: wait, silentRun: silentRun);
        }

        return ExecInCurrentTerminal(lua: lua, cs: cs, parts: parts, cwd: cwd, env: env, silentRun: silentRun);
    }

    internal static DynValue SpawnProcess(Script lua, CommandService cs, Table commandArgs, Table? options, Core.ExternalTools.JsonToolResolver tools) {
        List<string> parts = Lua.Globals.Utils.TableToStringList(t: commandArgs);
        if (parts.Count == 0) throw new ScriptRuntimeException("spawn_process requires executable");
        if (!EngineNet.ScriptEngines.Security.IsApprovedExecutable(executable: parts[index: 0], tools: tools)) throw new ScriptRuntimeException("Not approved");

        string? cwd = null;
        bool captureStdout = true;
        bool captureStderr = true;
        Dictionary<string, string> env = new();

        if (options != null) {
            DynValue v = options.Get(key: "cwd");
            if (!v.IsNil() && v.Type == DataType.String) cwd = v.String;
            v = options.Get(key: "capture_stdout");
            if (v.Type == DataType.Boolean) captureStdout = v.Boolean;
            v = options.Get(key: "capture_stderr");
            if (v.Type == DataType.Boolean) captureStderr = v.Boolean;
            v = options.Get(key: "env");
            if (v.Type == DataType.Table) {
                foreach (TablePair pair in v.Table.Pairs) {
                    if (pair.Key.Type == DataType.String && pair.Value.Type == DataType.String) {
                        env[key: pair.Key.String] = pair.Value.String;
                    }
                }
            }
        }

        if (!ValidateArgPaths(args: parts, cwd: cwd)) throw new ScriptRuntimeException("Restricted path detected");

        try {
            int pid = cs.SpawnProcess(executable: parts[index: 0], args: parts.Skip(count: 1), cwd: cwd, env: env, captureStdout: captureStdout, captureStderr: captureStderr);
            Table t = new(owner: lua) {
                [key: "pid"] = pid,
            };
            return DynValue.NewTable(table: t);
        } catch (Exception ex) {
            throw new ScriptRuntimeException(ex.Message);
        }
    }

    internal static DynValue PollProcess(Script lua, CommandService cs, int pid) {
        try {
            ProcessPollResult res = cs.PollProcess(pid: pid);
            Table t = new(owner: lua) {
                [key: "running"] = res.Running,
            };
            if (!res.Running) t[key: "exit_code"] = res.ExitCode;
            t[key: "stdout"] = res.StdoutFull;
            t[key: "stderr"] = res.StderrFull;
            t[key: "stdout_delta"] = res.StdoutDelta;
            t[key: "stderr_delta"] = res.StderrDelta;
            return DynValue.NewTable(table: t);
        } catch (Exception ex) {
            throw new ScriptRuntimeException(ex.Message);
        }
    }

    internal static DynValue WaitProcess(Script lua, CommandService cs, int pid, int? timeoutMs) {
        try {
            // Keep parity with previous behavior: wait_process acted as a status check.
            _ = timeoutMs;
            ProcessPollResult res = cs.PollProcess(pid: pid);
            Table t = new(owner: lua) {
                [key: "running"] = res.Running,
            };
            if (!res.Running) t[key: "exit_code"] = res.ExitCode;
            t[key: "stdout"] = res.StdoutFull;
            t[key: "stderr"] = res.StderrFull;
            t[key: "stdout_delta"] = res.StdoutDelta;
            t[key: "stderr_delta"] = res.StderrDelta;
            return DynValue.NewTable(table: t);
        } catch (Exception ex) {
            throw new ScriptRuntimeException(ex.Message);
        }
    }

    internal static DynValue CloseProcess(Script lua, CommandService cs, int pid) {
        return DynValue.NewBoolean(v: cs.CloseProcess(pid: pid));
    }

    private static DynValue HandleNewTerminalExecution(Script lua, CommandService cs, List<string> parts, string? cwd, Dictionary<string, string> env, bool keepOpen, bool wait, bool silentRun) {
        try {
            // Parity fallback: when no terminal emulator is available on Unix-like systems,
            // execute in the current terminal path instead of failing.
            if ((System.OperatingSystem.IsLinux() || System.OperatingSystem.IsMacOS()) && !HasKnownTerminalEmulator()) {
                return ExecInCurrentTerminal(lua: lua, cs: cs, parts: parts, cwd: cwd, env: env, silentRun: silentRun);
            }

            ProcessResult res = cs.RunInNewTerminal(executable: parts[index: 0], args: parts.Skip(count: 1), cwd: cwd, env: env, keepOpen: keepOpen, wait: wait);
            Table t = new(owner: lua) {
                [key: "success"] = res.Success,
                [key: "exit_code"] = res.ExitCode,
            };
            return DynValue.NewTable(table: t);
        } catch (Exception ex) {
            throw new ScriptRuntimeException(ex.Message);
        }
    }

    /// <summary>
    /// Executes a command in the current terminal, capturing its output and exit code.
    /// </summary>
    /// <param name="lua">The Lua script context.</param>
    /// <param name="cs">The command service used to execute the command.</param>
    /// <param name="parts">The command and its arguments as a list of strings.</param>
    /// <param name="cwd">The current working directory for the command execution.</param>
    /// <param name="env">Environment variables to set for the command execution.</param>
    /// <param name="silentRun">If true, suppresses output to the terminal.</param>
    /// <returns>A DynValue representing the result of the command execution, including success status and exit code.</returns>
    private static DynValue ExecInCurrentTerminal(Script lua, CommandService cs, List<string> parts, string? cwd, Dictionary<string, string> env, bool silentRun) {
        try {
            Dictionary<string, object?> envObj = env.ToDictionary(keySelector: k => k.Key, elementSelector: v => (object?)v.Value);
            if (!string.IsNullOrEmpty(cwd)) {
                envObj[key: "PWD"] = cwd;
            }

            int exitCode = -1;
            bool success = cs.ExecuteCommand(
                commandParts: parts,
                title: System.IO.Path.GetFileName(path: parts[index: 0]),
                onOutput: (msg, type) => {
                    if (silentRun) return;
                    string? color = type == "stderr" ? "red" : null;
                    Shared.IO.UI.EngineSdk.Print(msg, color: color, newline: true);
                    Shared.IO.Diagnostics.Log($"[{type}] {msg}");
                },
                onEvent: evt => {
                    if (!evt.TryGetValue(key: "event", out object? ev) || !string.Equals(a: ev?.ToString(), b: "end", comparisonType: System.StringComparison.OrdinalIgnoreCase)) return;
                    if (evt.TryGetValue(key: "exit_code", out object? code) && int.TryParse(s: code?.ToString(), result: out int parsed)) {
                        exitCode = parsed;
                    }
                },
                envOverrides: envObj
            );

            if (!success && exitCode == -1) {
                exitCode = 1;
            }

            Table result = new(owner: lua) {
                [key: "exit_code"] = exitCode >= 0 ? exitCode : (success ? 0 : 1),
                [key: "success"] = success && (exitCode == 0 || exitCode == -1),
            };
            return DynValue.NewTable(table: result);
        } catch (Exception ex) {
            throw new ScriptRuntimeException(ex.Message);
        }
    }

    /// <summary>
    /// Checks if the current system has a known terminal emulator available.
    /// </summary>
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

        return candidates.Any(predicate: static candidate => System.IO.File.Exists(path: candidate));
    }
}
