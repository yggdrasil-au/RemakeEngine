// MoonSharp -- Lua interpreter
using MoonSharp.Interpreter;
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using System.Globalization;
using System.Runtime.InteropServices;
//
using Microsoft.Data.Sqlite;
// internal usings
using EngineNet.Core.ScriptEngines.Helpers;
using EngineNet.Tools;
using EngineNet.Core.Sys;

namespace EngineNet.Core.ScriptEngines;

/// <summary>
/// Executes a Lua script using the embedded MoonSharp interpreter.
///
/// Exposed Lua globals for module authors:
/// - tool(name): resolve a registered tool to an absolute path.
/// - argv: string array of arguments passed to the script.
/// - emit(event, data?): send a structured engine event. `data` is an optional table.
///   Example: emit("warning", { message = "Heads up" })
/// - warn(message): shorthand for emit("warning", { message = message })
/// - error(message): shorthand for emit("error", { message = message })
/// - prompt(message, id?, secret?): prompts the user; returns the entered string.
///   Example: local answer = prompt("Continue? (y/n)", "confirm1", false)
/// - progress(total, id?, label?): creates a progress handle; returns an object with Update(inc?)
///   Example: local p = progress(100, "extract", "Extracting files"); p:Update(5)
///
/// All helpers wrap RemakeEngine.Utils.EngineSdk for consistent engine integration.
/// </summary>
public sealed class LuaScriptAction:IAction {
    private readonly String _scriptPath;
    private readonly String[] _args;

    public LuaScriptAction(String scriptPath) : this(scriptPath, Array.Empty<String>()) { }

    public LuaScriptAction(String scriptPath, IEnumerable<String>? args) {
        _scriptPath = scriptPath;
        _args = args is null ? Array.Empty<String>() : args as String[] ?? new List<String>(args).ToArray();
    }

    public async Task ExecuteAsync(IToolResolver tools, CancellationToken cancellationToken = default) {
        if (!File.Exists(_scriptPath)) {
            throw new FileNotFoundException("Lua script not found", _scriptPath);
        }

        String code = await File.ReadAllTextAsync(_scriptPath, cancellationToken);
        Script lua = new Script();

        // Expose a simple function to resolve tool paths from Lua:
        lua.Globals["tool"] = (Func<String, String>)tools.ResolveToolPath;
        lua.Globals["argv"] = _args;

        // Register types used as userdata
        UserData.RegisterType<EngineSdk.Progress>();
        UserData.RegisterType<SqliteHandle>();

        // EngineSdk wrappers
        lua.Globals["warn"] = (Action<String>)EngineSdk.Warn;
        lua.Globals["error"] = (Action<String>)EngineSdk.Error;

        // emit(event, data?) where data is an optional Lua table
        lua.Globals["emit"] = (Action<DynValue, DynValue>)((ev, data) => {
            String evName = ev.Type == DataType.String ? ev.String : ev.ToPrintString();
            IDictionary<String, Object?>? dict = data.Type == DataType.Nil || data.Type == DataType.Void ? null : TableToDictionary(data.Table);
            EngineSdk.Emit(evName, dict);
        });

        // prompt(message, id?, secret?) -> string
        lua.Globals["prompt"] = (Func<DynValue, DynValue, DynValue, String>)((message, id, secret) => {
            String msg = message.Type == DataType.String ? message.String : message.ToPrintString();
            String pid = id.Type == DataType.Nil || id.Type == DataType.Void ? "q1" : id.Type == DataType.String ? id.String : id.ToPrintString();
            Boolean sec = secret.Type == DataType.Boolean && secret.Boolean;
            return EngineSdk.Prompt(msg, pid, sec);
        });

        // progress(total, id?, label?) -> EngineSdk.Progress userdata
        lua.Globals["progress"] = (Func<Int32, String?, String?, EngineSdk.Progress>)((total, id, label) => {
            String pid = String.IsNullOrEmpty(id) ? "p1" : id!;
            return new EngineSdk.Progress(total, pid, label);
        });

        // sdk: config/filesystem helpers + terminal print helpers
        Table sdk = new Table(lua);
        // color/colour print: accepts either (color, message[, newline]) or a table { colour=?, color=?, message=?, newline=? }
        CallbackFunction colorPrintFunc = new CallbackFunction((ctx, args) => {
            String? color = null;
            String message = String.Empty;
            Boolean newline = true;
            if (args.Count >= 2 && (args[0].Type == DataType.String || args[0].Type == DataType.UserData)) {
                // color, message, [newline]
                color = args[0].ToPrintString();
                message = args[1].Type == DataType.String ? args[1].String : args[1].ToPrintString();
                if (args.Count >= 3 && args[2].Type == DataType.Boolean) {
                    newline = args[2].Boolean;
                }
            } else if (args.Count >= 1 && args[0].Type == DataType.Table) {
                Table t = args[0].Table;
                DynValue c = t.Get("color");
                if (c.IsNil()) {
                    c = t.Get("colour");
                }

                if (!c.IsNil()) {
                    color = c.Type == DataType.String ? c.String : c.ToPrintString();
                }

                DynValue m = t.Get("message");
                if (!m.IsNil()) {
                    message = m.Type == DataType.String ? m.String : m.ToPrintString();
                }

                DynValue nl = t.Get("newline");
                if (!nl.IsNil() && nl.Type == DataType.Boolean) {
                    newline = nl.Boolean;
                }
            }
            EngineSdk.Print(message, color, newline);
            return DynValue.Nil;
        });
        sdk["color_print"] = DynValue.NewCallback(colorPrintFunc);
        sdk["colour_print"] = DynValue.NewCallback(colorPrintFunc);
        sdk["ensure_project_config"] = (Func<String, String>)((root) => ConfigHelpers.EnsureProjectConfig(root));
        sdk["validate_source_dir"] = (Func<String, Boolean>)((dir) => {
            try {
                ConfigHelpers.ValidateSourceDir(dir);
                return true;
            } catch { return false; }
        });

        sdk["copy_dir"] = (Func<String, String, DynValue, Boolean>)((src, dst, overwrite) => {
            try {
                Boolean ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                ConfigHelpers.CopyDirectory(src, dst, ow);
                return true;
            } catch { return false; }
        });
        sdk["move_dir"] = (Func<String, String, DynValue, Boolean>)((src, dst, overwrite) => {
            try {
                Boolean ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                ConfigHelpers.MoveDirectory(src, dst, ow);
                return true;
            } catch { return false; }
        });
        sdk["find_subdir"] = (Func<String, String, String?>)((baseDir, name) => ConfigHelpers.FindSubdir(baseDir, name));
        sdk["has_all_subdirs"] = (Func<String, Table, Boolean>)((baseDir, names) => {
            try {
                List<String> list = TableToStringList(names);
                return ConfigHelpers.HasAllSubdirs(baseDir, list);
            } catch { return false; }
        });
        sdk["ensure_dir"] = (Func<String, Boolean>)(path => {
            try {
                Directory.CreateDirectory(path);
                return true;
            } catch { return false; }
        });
        sdk["path_exists"] = (Func<String, Boolean>)FsUtils.PathExists;
        sdk["lexists"] = (Func<String, Boolean>)FsUtils.PathExistsIncludingLinks;
        sdk["is_dir"] = (Func<String, Boolean>)Directory.Exists;
        sdk["is_file"] = (Func<String, Boolean>)File.Exists;
        sdk["remove_dir"] = (Func<String, Boolean>)(path => {
            try {
                if (Directory.Exists(path)) {
                    Directory.Delete(path, true);
                }

                return true;
            } catch { return false; }
        });
        sdk["remove_file"] = (Func<String, Boolean>)(path => {
            try {
                if (FsUtils.IsSymlink(path) || File.Exists(path)) {
                    File.Delete(path);
                }

                return true;
            } catch { return false; }
        });
        sdk["copy_file"] = (Func<String, String, DynValue, Boolean>)((src, dst, overwrite) => {
            try {
                Boolean ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                File.Copy(src, dst, ow);
                return true;
            } catch { return false; }
        });
        sdk["create_symlink"] = (Func<String, String, Boolean, Boolean>)FsUtils.CreateSymlink;
        sdk["is_symlink"] = (Func<String, Boolean>)FsUtils.IsSymlink;
        sdk["realpath"] = (Func<String, String?>)FsUtils.RealPath;
        sdk["readlink"] = (Func<String, String?>)FsUtils.ReadLink;
        sdk["sleep"] = (Action<Double>)(seconds => {
            if (Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds <= 0) {
                return;
            }

            try {
                Thread.Sleep(TimeSpan.FromSeconds(seconds));
            } catch { }
        });
        sdk["md5"] = (Func<String, String>)(text => {
            try {
                Byte[] data = MD5.HashData(Encoding.UTF8.GetBytes(text ?? String.Empty));
                return Convert.ToHexString(data).ToLowerInvariant();
            } catch {
                return String.Empty;
            }
        });
        // TOML helpers
        sdk["toml_read_file"] = (Func<String, DynValue>)(path => {
            try {
                Object obj = TomlHelpers.ParseFileToPlainObject(path);
                return ToDynValue(lua, obj);
            } catch (Exception ex) {
                EngineSdk.Error($"TOML read failed: {ex.Message}");
                return DynValue.Nil;
            }
        });
        sdk["toml_write_file"] = (Action<String, DynValue>)((path, value) => {
            try {
                Object? obj = FromDynValue(value);
                TomlHelpers.WriteTomlFile(path, obj);
            } catch (Exception ex) {
                EngineSdk.Error($"TOML write failed: {ex.Message}");
            }
        });
        // exec(args[, options]) -> { success=bool, exit_code=int }
        // options: { cwd=string, env=table, new_terminal=bool, keep_open=bool, title=string, wait=bool }
        sdk["exec"] = DynValue.NewCallback((ctx, args) => {
            if (args.Count < 1 || args[0].Type != DataType.Table) {
                throw new ScriptRuntimeException("exec expects first argument to be an array/table of strings (command + args)");
            }

            Table commandArgs = args[0].Table;
            Table? options = args.Count > 1 && args[1].Type == DataType.Table ? args[1].Table : null;
            return ExecProcess(lua, commandArgs, options);
        });
        sdk["run_process"] = DynValue.NewCallback((ctx, args) => {
            if (args.Count < 1 || args[0].Type != DataType.Table) {
                throw new ScriptRuntimeException("run_process expects argument table");
            }

            Table commandArgs = args[0].Table;
            Table? options = args.Count > 1 && args[1].Type == DataType.Table ? args[1].Table : null;
            return RunProcess(lua, commandArgs, options);
        });
        lua.Globals["sdk"] = sdk;
        lua.Globals["sqlite"] = CreateSqliteModule(lua);

        // Preload minimal shims for LuaFileSystem (lfs) and dkjson used by game modules
        PreloadShimModules(lua, _scriptPath);
        Console.WriteLine($"Running lua script '{_scriptPath}' with {_args.Length} args...");
        Console.WriteLine($"input args: {String.Join(", ", _args)}");
        await Task.Run(() => lua.DoString(code), cancellationToken);
    }

    private static Table CreateSqliteModule(Script lua) {
        Table module = new Table(lua);
        module["open"] = DynValue.NewCallback((ctx, args) => {
            if (args.Count < 1 || args[0].Type != DataType.String) {
                throw new ScriptRuntimeException("sqlite.open(path) requires a string path");
            }

            String path = args[0].String;
            SqliteHandle handle = new SqliteHandle(lua, path);
            return DynValue.NewTable(CreateSqliteHandleTable(lua, handle));
        });
        return module;
    }

    private static Table CreateSqliteHandleTable(Script lua, SqliteHandle handle) {
        Table table = new Table(lua);
        table["exec"] = DynValue.NewCallback((ctx, args) => {
            Int32 offset = args.Count > 0 && args[0].Type == DataType.Table ? 1 : 0;
            if (args.Count <= offset || args[offset].Type != DataType.String) {
                throw new ScriptRuntimeException("sqlite handle exec(sql [, params])");
            }

            String sql = args[offset].String;
            Table? paramTable = args.Count > offset + 1 && args[offset + 1].Type == DataType.Table ? args[offset + 1].Table : null;
            Int32 affected = handle.Execute(sql, paramTable);
            return DynValue.NewNumber(affected);
        });
        table["query"] = DynValue.NewCallback((ctx, args) => {
            Int32 offset = args.Count > 0 && args[0].Type == DataType.Table ? 1 : 0;
            if (args.Count <= offset || args[offset].Type != DataType.String) {
                throw new ScriptRuntimeException("sqlite handle query(sql [, params])");
            }

            String sql = args[offset].String;
            Table? paramTable = args.Count > offset + 1 && args[offset + 1].Type == DataType.Table ? args[offset + 1].Table : null;
            return handle.Query(sql, paramTable);
        });
        table["begin"] = DynValue.NewCallback((ctx, args) => {
            handle.BeginTransaction();
            return DynValue.Nil;
        });
        table["commit"] = DynValue.NewCallback((ctx, args) => {
            handle.Commit();
            return DynValue.Nil;
        });
        table["rollback"] = DynValue.NewCallback((ctx, args) => {
            handle.Rollback();
            return DynValue.Nil;
        });
        table["close"] = DynValue.NewCallback((ctx, args) => {
            handle.Dispose();
            return DynValue.Nil;
        });
        table["dispose"] = table.Get("close");
        table["__handle"] = UserData.Create(handle);
        return table;
    }
    private static DynValue RunProcess(Script lua, Table commandArgs, Table? options) {
        if (commandArgs == null) {
            throw new ScriptRuntimeException("run_process expects argument table");
        }

        List<String> arguments = TableToStringList(commandArgs);
        if (arguments.Count == 0) {
            throw new ScriptRuntimeException("run_process requires at least one argument (executable path)");
        }

        String fileName = arguments[0];
        ProcessStartInfo psi = new ProcessStartInfo {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        for (Int32 i = 1; i < arguments.Count; i++) {
            psi.ArgumentList.Add(arguments[i]);
        }

        Boolean captureStdout = true;
        Boolean captureStderr = true;
        Int32? timeoutMs = null;
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
                timeoutMs = (Int32)Math.Max(0, timeoutOpt.Number);
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

        StringBuilder stdoutBuilder = new StringBuilder();
        StringBuilder stderrBuilder = new StringBuilder();

        using Process process = new Process();
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
                    } catch { }
                    throw new ScriptRuntimeException($"Process '{fileName}' timed out after {timeoutMs.Value} ms");
                }
            } else {
                process.WaitForExit();
            }
        } catch (Exception ex) {
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
    private static DynValue ExecProcess(Script lua, Table commandArgs, Table? options) {
        if (commandArgs == null) {
            throw new ScriptRuntimeException("exec expects argument table");
        }

        List<String> parts = TableToStringList(commandArgs);
        if (parts.Count == 0) {
            throw new ScriptRuntimeException("exec requires at least one argument (executable path)");
        }

        String cwd = String.Empty;
        Boolean newTerminal = false;
        Boolean keepOpen = false;
        Boolean wait = true;
        String? title = null;
        Dictionary<String, Object?> env = new Dictionary<String, Object?>(StringComparer.Ordinal);

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
                        String k = p.Key.String;
                        Object? val = FromDynValue(p.Value);
                        env[k] = val?.ToString();
                    }
                }
            }
        }

        // If requested to open in a new terminal window, best-effort platform specific handling
        if (newTerminal) {
            Int32 exitCode = 0;
            try {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    // Run the target directly with a visible window; do not redirect streams
                    ProcessStartInfo psi = new ProcessStartInfo {
                        UseShellExecute = true,
                        FileName = parts[0],
                        Arguments = BuildArguments(parts, 1),
                        CreateNoWindow = false,
                        WindowStyle = ProcessWindowStyle.Normal,
                    };
                    if (!String.IsNullOrEmpty(cwd)) { psi.WorkingDirectory = cwd; }
                    foreach (KeyValuePair<String, Object?> kv in env) { psi.Environment[kv.Key] = kv.Value?.ToString() ?? String.Empty; }

                    using Process p = new Process { StartInfo = psi };
                    p.Start();
                    if (wait) {
                        p.WaitForExit();
                        exitCode = p.ExitCode;
                    } else {
                        exitCode = 0; // not waited; assume success for now
                    }
                } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                    // Try to launch a terminal emulator; fall back to current window streaming
                    String cmdline = BuildCommandLine(parts);
                    String term = FindTerminalEmulator();
                    if (term != String.Empty) {
                        ProcessStartInfo psi = new ProcessStartInfo {
                            UseShellExecute = false,
                            RedirectStandardOutput = false,
                            RedirectStandardError = false,
                            RedirectStandardInput = false,
                            CreateNoWindow = false,
                        };
                        if (term.Contains("gnome-terminal", StringComparison.OrdinalIgnoreCase)) {
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
                        if (!String.IsNullOrEmpty(cwd)) { psi.WorkingDirectory = cwd; }
                        using Process p = new Process { StartInfo = psi };
                        p.Start();
                        if (wait) { p.WaitForExit(); exitCode = p.ExitCode; } else { exitCode = 0; }
                    } else {
                        // No terminal emulator found; stream in current window instead
                        return ExecInCurrentTerminal(lua, parts, cwd, env);
                    }
                }
            } catch (Exception ex) {
                throw new ScriptRuntimeException($"Failed to start new terminal: {ex.Message}");
            }

            Table result = new Table(lua);
            result["exit_code"] = exitCode;
            result["success"] = exitCode == 0;
            return DynValue.NewTable(result);
        }

        // Default: stream in current engine output using ProcessRunner
        return ExecInCurrentTerminal(lua, parts, cwd, env);
    }

    private static DynValue ExecInCurrentTerminal(Script lua, List<String> parts, String cwd, IDictionary<String, Object?> env) {
        ProcessRunner runner = new ProcessRunner();
        Int32 exit = -1;
        // Merge env overrides
        Dictionary<String, Object?> envOverrides = new Dictionary<String, Object?>(env, StringComparer.Ordinal);
        if (!String.IsNullOrEmpty(cwd)) {
            envOverrides["PWD"] = cwd;
        }

        // Print each line through EngineSdk
        Boolean ok = runner.Execute(
            commandParts: parts,
            opTitle: Path.GetFileName(parts[0]),
            onOutput: (line, stream) => {
                // Map stderr to red for visibility
                String? color = stream == "stderr" ? "red" : null;
                EngineSdk.Print(line, color, true);
            },
            onEvent: (evt) => {
                if (evt.TryGetValue("event", out Object? ev) && (ev?.ToString() ?? String.Empty) == "end") {
                    if (evt.TryGetValue("exit_code", out Object? code) && Int32.TryParse(code?.ToString(), out Int32 c)) {
                        exit = c;
                    }
                }
            },
            stdinProvider: null,
            envOverrides: envOverrides,
            cancellationToken: default
        );

        Table result = new Table(lua);
        result["exit_code"] = exit >= 0 ? exit : (ok ? 0 : 1);
        result["success"] = ok && (exit == 0 || exit == -1);
        return DynValue.NewTable(result);
    }

    private static String BuildArguments(List<String> args, Int32 startIdx) {
        StringBuilder sb = new StringBuilder();
        for (Int32 i = startIdx; i < args.Count; i++) {
            if (i > startIdx) sb.Append(' ');
            sb.Append(QuoteArg(args[i] ?? String.Empty));
        }
        return sb.ToString();
    }

    private static String QuoteArg(String arg) {
        if (String.IsNullOrEmpty(arg)) return "\"\"";
        if (arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }

    private static String BuildCommandLine(List<String> parts) {
        return BuildArguments(parts, 0);
    }

    private static String FindTerminalEmulator() {
        try {
            // Try common terminals
            String[] candidates = new[] { "/usr/bin/gnome-terminal", "/usr/bin/konsole", "/usr/bin/xterm", "/usr/bin/alacritty", "/usr/bin/xfce4-terminal" };
            foreach (String c in candidates) { if (File.Exists(c)) return c; }
        } catch { }
        return String.Empty;
    }
    private sealed class SqliteHandle:IDisposable {
        private readonly Script _script;
        private readonly SqliteConnection _connection;
        private SqliteTransaction? _transaction;
        private Boolean _disposed;

        public SqliteHandle(Script script, String path) {
            _script = script;
            String fullPath = Path.GetFullPath(path);
            SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder {
                DataSource = fullPath
            };
            _connection = new SqliteConnection(builder.ConnectionString);
            _connection.Open();
        }

        public Int32 Execute(String sql, Table? parameters) {
            EnsureNotDisposed();
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = sql;
            if (_transaction != null) {
                command.Transaction = _transaction;
            }

            BindParameters(command, parameters);
            return command.ExecuteNonQuery();
        }

        public DynValue Query(String sql, Table? parameters) {
            EnsureNotDisposed();
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = sql;
            if (_transaction != null) {
                command.Transaction = _transaction;
            }

            BindParameters(command, parameters);
            using SqliteDataReader reader = command.ExecuteReader();
            Table result = new Table(_script);
            Int32 index = 1;
            while (reader.Read()) {
                Table row = new Table(_script);
                for (Int32 i = 0; i < reader.FieldCount; i++) {
                    String columnName = reader.GetName(i);
                    Object? value = reader.GetValue(i);
                    row[columnName] = ToDynValue(_script, value);
                }
                result[index++] = DynValue.NewTable(row);
            }
            return DynValue.NewTable(result);
        }

        public void BeginTransaction() {
            EnsureNotDisposed();
            _transaction ??= _connection.BeginTransaction();
        }

        public void Commit() {
            if (_disposed) {
                return;
            }

            if (_transaction != null) {
                _transaction.Commit();
                _transaction.Dispose();
                _transaction = null;
            }
        }

        public void Rollback() {
            if (_disposed) {
                return;
            }

            if (_transaction != null) {
                _transaction.Rollback();
                _transaction.Dispose();
                _transaction = null;
            }
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }

            try {
                _transaction?.Dispose();
                _connection.Dispose();
            } finally {
                _transaction = null;
                _disposed = true;
            }
        }

        private void EnsureNotDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(SqliteHandle));
            }
        }

        private static void BindParameters(SqliteCommand command, Table? parameters) {
            if (parameters == null) {
                return;
            }

            IDictionary<String, Object?> dict = TableToDictionary(parameters);
            foreach (KeyValuePair<String, Object?> kv in dict) {
                SqliteParameter parameter = command.CreateParameter();
                String name = kv.Key;
                if (!name.StartsWith(":", StringComparison.Ordinal) && !name.StartsWith("@", StringComparison.Ordinal) && !name.StartsWith("$", StringComparison.Ordinal)) {
                    name = ":" + name;
                }

                parameter.ParameterName = name;
                parameter.Value = kv.Value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
        }
    }

    private static DynValue ToDynValue(Script lua, Object? value) {
        if (value is null || value is DBNull)
            return DynValue.Nil;

        // Map common primitives directly first
        switch (value) {
            case Boolean b: return DynValue.NewBoolean(b);
            case Byte bt: return DynValue.NewNumber(bt);
            case SByte sb: return DynValue.NewNumber(sb);
            case Int16 i16: return DynValue.NewNumber(i16);
            case UInt16 ui16: return DynValue.NewNumber(ui16);
            case Int32 i32: return DynValue.NewNumber(i32);
            case UInt32 ui32: return DynValue.NewNumber(ui32);
            case Int64 i64: return DynValue.NewNumber(i64);
            case UInt64 ui64: return DynValue.NewNumber(ui64);
            case Single f: return DynValue.NewNumber(f);
            case Double d: return DynValue.NewNumber(d);
            case Decimal dec: return DynValue.NewNumber((Double)dec);
            case DateTime dt: return DynValue.NewString(dt.ToString("o", CultureInfo.InvariantCulture));
            case DateTimeOffset dto: return DynValue.NewString(dto.ToString("o", CultureInfo.InvariantCulture));
            case Byte[] bytes: return DynValue.NewString(Convert.ToHexString(bytes));
            case String s: return DynValue.NewString(s);
        }

        // IDictionary -> Lua table with string keys
        if (value is System.Collections.IDictionary idict) {
            Table t = new Table(lua);
            foreach (System.Collections.DictionaryEntry entry in idict) {
                String key = entry.Key?.ToString() ?? String.Empty;
                t[key] = ToDynValue(lua, entry.Value);
            }
            return DynValue.NewTable(t);
        }

        // IEnumerable -> Lua array-like table (1-based)
        if (value is System.Collections.IEnumerable ienum && value is not String) {
            Table t = new Table(lua);
            Int32 i = 1;
            foreach (Object? item in ienum) {
                t[i++] = ToDynValue(lua, item);
            }
            return DynValue.NewTable(t);
        }

        // Fallback to string representation
        return DynValue.NewString(value.ToString() ?? String.Empty);
    }

    private static class FsUtils {
        public static Boolean PathExists(String path) => Directory.Exists(path) || File.Exists(path);

        public static Boolean PathExistsIncludingLinks(String path) {
            if (PathExists(path)) {
                return true;
            }

            try {
                FileSystemInfo info = GetInfo(path);
                return info.Exists ? true : info.LinkTarget != null;
            } catch {
                return false;
            }
        }

        public static Boolean IsSymlink(String path) {
            try {
                FileSystemInfo info = GetInfo(path);
                return info.LinkTarget != null || info.Attributes.HasFlag(FileAttributes.ReparsePoint);
            } catch {
                return false;
            }
        }

        public static Boolean CreateSymlink(String source, String destination, Boolean isDirectory) {
            try {
                String destFull = Path.GetFullPath(destination);
                String srcFull = Path.GetFullPath(source);
                String? parent = Path.GetDirectoryName(destFull);
                if (!String.IsNullOrEmpty(parent)) {
                    Directory.CreateDirectory(parent);
                }

                if (isDirectory) {
                    Directory.CreateSymbolicLink(destFull, srcFull);
                } else {
                    File.CreateSymbolicLink(destFull, srcFull);
                }

                return true;
            } catch {
                return false;
            }
        }

        public static String? RealPath(String path) {
            try {
                return Path.GetFullPath(path);
            } catch {
                return null;
            }
        }

        public static String? ReadLink(String path) {
            try {
                FileSystemInfo info = GetInfo(path);
                return info.LinkTarget;
            } catch {
                return null;
            }
        }

        private static FileSystemInfo GetInfo(String path) {
            String full = Path.GetFullPath(path);
            DirectoryInfo dirInfo = new DirectoryInfo(full);
            if (dirInfo.Exists) {
                return dirInfo;
            }

            FileInfo fileInfo = new FileInfo(full);
            if (fileInfo.Exists) {
                return fileInfo;
            }
            // Determine based on trailing separator
            return full.EndsWith(Path.DirectorySeparatorChar) || full.EndsWith(Path.AltDirectorySeparatorChar)
                ? new DirectoryInfo(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : fileInfo;
        }
    }

    private static void PreloadShimModules(Script lua, String scriptPath) {
        // Ensure package.loaded exists
        Table package = lua.Globals.Get("package").IsNil() ? new Table(lua) : lua.Globals.Get("package").Table;
        if (package.Get("loaded").IsNil()) {
            package["loaded"] = new Table(lua);
        }

        Table loaded = package.Get("loaded").Table;

        // Minimal 'require' shim: return preloaded modules from package.loaded
        if (lua.Globals.Get("require").IsNil()) {
            lua.Globals["require"] = (Func<String, DynValue>)(name => {
                DynValue mod = loaded.Get(name);
                return !mod.IsNil() ? mod : throw new ScriptRuntimeException($"module '{name}' not found (only preloaded modules available)");
            });
        }

        // lfs shim
        Table lfs = new Table(lua);
        lfs["currentdir"] = () => Environment.CurrentDirectory;
        // lfs.mkdir(path) -> true on success, nil on failure (minimal behavior)
        lfs["mkdir"] = (Func<String, DynValue>)((path) => {
            try {
                Directory.CreateDirectory(path);
                return DynValue.True;
            } catch (Exception) {
                // Return nil to indicate failure; message not used by current scripts
                return DynValue.Nil;
            }
        });
        lfs["attributes"] = (Func<String, DynValue>)((path) => {
            if (Directory.Exists(path)) {
                Table t = new Table(lua);
                t["mode"] = "directory";
                return DynValue.NewTable(t);
            }
            if (File.Exists(path)) {
                FileInfo info = new FileInfo(path);
                Table t = new Table(lua);
                t["mode"] = "file";
                t["size"] = info.Length;
                t["modtime"] = info.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture);
                return DynValue.NewTable(t);
            }
            return DynValue.Nil;
        });
        lfs["dir"] = (Func<String, DynValue>)((path) => {
            // Return an iterator function like lfs.dir
            IEnumerable<String> Enumerate() {
                // In real lfs, '.' and '..' are included; we'll include them for compatibility
                yield return ".";
                yield return "..";
                if (Directory.Exists(path)) {
                    foreach (String entry in Directory.EnumerateFileSystemEntries(path)) {
                        yield return Path.GetFileName(entry);
                    }
                }
            }
            IEnumerator<String> enumerator = Enumerate().GetEnumerator();
            CallbackFunction iterator = new CallbackFunction((ctx, args) => {
                return enumerator.MoveNext() ? DynValue.NewString(enumerator.Current) : DynValue.Nil;
            });
            return DynValue.NewCallback(iterator);
        });
        loaded["lfs"] = DynValue.NewTable(lfs);

        // dkjson shim: provides encode(value, opts?) and decode(string)
        Table dkjson = new Table(lua);
        dkjson["encode"] = (Func<DynValue, DynValue, String>)((val, opts) => {
            Boolean indent = false;
            if (opts.Type == DataType.Table) {
                DynValue indentVal = opts.Table.Get("indent");
                indent = indentVal.Type == DataType.Boolean && indentVal.Boolean;
            }
            Object? obj = FromDynValue(val);
            JsonSerializerOptions jsonOpts = new JsonSerializerOptions { WriteIndented = indent };
            return JsonSerializer.Serialize(obj, jsonOpts);
        });
        dkjson["decode"] = (Func<String, DynValue>)((json) => {
            try {
                using JsonDocument doc = JsonDocument.Parse(json);
                return JsonElementToDynValue(lua, doc.RootElement);
            } catch {
                return DynValue.Nil; // caller will treat as error
            }
        });
        loaded["dkjson"] = DynValue.NewTable(dkjson);

        // debug shim: provide getinfo with .source used by modules to find their file path
        Table debugTbl = lua.Globals.Get("debug").IsNil() ? new Table(lua) : lua.Globals.Get("debug").Table;
        debugTbl["getinfo"] = (Func<DynValue, DynValue, DynValue>)((level, what) => {
            Table t = new Table(lua);
            // Lua expects '@' prefix for file paths
            t["source"] = "@" + scriptPath;
            return DynValue.NewTable(t);
        });
        lua.Globals["debug"] = debugTbl;

        // publish back package (in case it didn't exist)
        lua.Globals["package"] = package;
    }

    private static DynValue JsonElementToDynValue(Script lua, JsonElement el) {
        switch (el.ValueKind) {
            case JsonValueKind.Object:
                Table t = new Table(lua);
                foreach (JsonProperty p in el.EnumerateObject()) {
                    t[p.Name] = JsonElementToDynValue(lua, p.Value);
                }
                return DynValue.NewTable(t);
            case JsonValueKind.Array:
                Table arr = new Table(lua);
                Int32 i = 1;
                foreach (JsonElement item in el.EnumerateArray()) {
                    arr[i++] = JsonElementToDynValue(lua, item);
                }
                return DynValue.NewTable(arr);
            case JsonValueKind.String:
                return DynValue.NewString(el.GetString() ?? String.Empty);
            case JsonValueKind.Number:
                if (el.TryGetDouble(out Double d)) {
                    return DynValue.NewNumber(d);
                }

                return DynValue.NewNumber(0);
            case JsonValueKind.True:
                return DynValue.True;
            case JsonValueKind.False:
                return DynValue.False;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                return DynValue.Nil;
        }
    }

    private static IDictionary<String, Object?> TableToDictionary(Table table) {
        Dictionary<String, Object?> dict = new Dictionary<String, Object?>(StringComparer.Ordinal);
        foreach (TablePair pair in table.Pairs) {
            // Convert key to string
            String key = pair.Key.Type switch {
                DataType.String => pair.Key.String,
                DataType.Number => pair.Key.Number.ToString(CultureInfo.InvariantCulture),
                _ => pair.Key.ToPrintString()
            };
            dict[key] = FromDynValue(pair.Value);
        }
        return dict;
    }

    private static Object? FromDynValue(DynValue v) => v.Type switch {
        DataType.Nil or DataType.Void => null,
        DataType.Boolean => v.Boolean,
        DataType.Number => v.Number,
        DataType.String => v.String,
        DataType.Table => TableToPlainObject(v.Table),
        _ => v.ToPrintString()
    };

    private static Object TableToPlainObject(Table t) {
        // Heuristic: if all keys are consecutive 1..n numbers, treat as array
        Int32 count = 0;
        Boolean arrayLike = true;
        foreach (TablePair pair in t.Pairs) {
            count++;
            if (pair.Key.Type != DataType.Number) {
                arrayLike = false;
            }
        }
        if (arrayLike) {
            List<Object?> list = new List<Object?>(count);
            for (Int32 i = 1; i <= count; i++) {
                DynValue dv = t.Get(i);
                list.Add(FromDynValue(dv));
            }
            return list;
        }
        return TableToDictionary(t);
    }

    private static List<String> TableToStringList(Table t) {
        List<String> list = new List<String>();
        // Iterate up to the numeric length; stop when we hit a Nil entry
        for (Int32 i = 1; i <= t.Length; i++) {
            DynValue dv = t.Get(i);
            if (dv.Type == DataType.Nil || dv.Type == DataType.Void) {
                break;
            }

            String s = dv.Type == DataType.String ? dv.String : dv.ToPrintString();
            list.Add(s);
        }
        return list;
    }
}
