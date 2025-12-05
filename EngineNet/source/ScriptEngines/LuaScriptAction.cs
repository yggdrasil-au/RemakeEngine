using MoonSharp.Interpreter;
using SQLitePCL;
using System.Collections.Generic;
using System.Diagnostics;

namespace EngineNet.ScriptEngines;

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
internal sealed class LuaScriptAction : Helpers.IAction {
    private readonly string _scriptPath;
    private readonly string[] _args;

    //internal LuaScriptAction(string scriptPath) : this(scriptPath, System.Array.Empty<string>()) { }

    internal LuaScriptAction(string scriptPath, IEnumerable<string>? args) {
        _scriptPath = scriptPath;
        _args = args is null ? System.Array.Empty<string>() : args as string[] ?? new List<string>(args).ToArray();
    }

    public async System.Threading.Tasks.Task ExecuteAsync(Core.Tools.IToolResolver tools, System.Threading.CancellationToken cancellationToken = default) {
        if (!System.IO.File.Exists(_scriptPath)) {
            throw new System.IO.FileNotFoundException("Lua script not found", _scriptPath);
        }

        string code = await System.IO.File.ReadAllTextAsync(_scriptPath, cancellationToken);

        // Security: Use controlled Lua environment (not full sandbox as it blocks too much)
        Script lua = new Script(CoreModules.Preset_Default);

        // Setup safe Lua environment
        SetupSafeLuaEnvironment(lua);

        // Expose core functions
        SetupCoreFunctions(lua, tools);

        // Register UserData types
        UserData.RegisterType<Core.Utils.EngineSdk.PanelProgress>();
        UserData.RegisterType<Core.Utils.EngineSdk.ScriptProgress>();
        UserData.RegisterType<LuaModules.SqliteHandle>();

        // Setup SDK and modules
        lua.Globals["sdk"] = LuaModules.LuaSdkModule.CreateSdkModule(lua, tools);
        lua.Globals["sqlite"] = LuaModules.LuaSqliteModule.CreateSqliteModule(lua);

        // Preload minimal shims for LuaFileSystem (lfs) and dkjson used by game modules
        LuaModules.LuaShimModules.PreloadShimModules(lua, _scriptPath);

        Core.Utils.EngineSdk.PrintLine(message: $"Running lua script '{_scriptPath}' with {_args.Length} args...", color: System.ConsoleColor.Cyan);
        Core.Utils.EngineSdk.PrintLine(message: $"input args: {string.Join(", ", _args)}", color: System.ConsoleColor.Gray);

        // Signal GUI that a script is active so the bottom panel can reflect activity even without progress events
        Core.Utils.EngineSdk.ScriptActiveStart(scriptPath: _scriptPath);

        bool ok = false;
        try {
            await System.Threading.Tasks.Task.Run(() => lua.DoString(code), cancellationToken).ConfigureAwait(false);
            ok = true;
        } finally {
            // Always signal end; GUI will jump to 100% and close the indicator.
            Core.Utils.EngineSdk.ScriptActiveEnd(success: ok, exitCode: ok ? 0 : 1);
        }
    }

    private void SetupSafeLuaEnvironment(Script lua) {
        // Remove dangerous standard library functions but preserve package/require system
        lua.Globals["loadfile"] = DynValue.Nil;     // Remove ability to load arbitrary files
        lua.Globals["dofile"] = DynValue.Nil;       // Remove ability to execute arbitrary files

        // Remove dangerous io functions but keep basic ones
        if (lua.Globals.Get("io").Type == DataType.Table) {
            Table ioTable = lua.Globals.Get("io").Table;
            ioTable["popen"] = DynValue.Nil;        // Remove io.popen (command execution)
        }

        // Create safe os table with limited functionality
        CreateSafeOsTable(lua);

        // Create safe io table for basic file operations within workspace
        CreateSafeIoTable(lua);
    }

    private void CreateSafeOsTable(Script lua) {
        Table safeOs = new Table(lua);
        safeOs["date"] = (System.Func<string?, DynValue>)((format) => {
            if (string.IsNullOrEmpty(format)) return DynValue.NewNumber(System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            // Only allow safe date formats, no os.date("!%c", os.execute(...)) exploits
            if (format.StartsWith("*t") || format.StartsWith("!*t")) {
                // Return table format - safe
                var dt = System.DateTimeOffset.Now;
                Table dateTable = new Table(lua);
                dateTable["year"] = dt.Year;
                dateTable["month"] = dt.Month;
                dateTable["day"] = dt.Day;
                dateTable["hour"] = dt.Hour;
                dateTable["min"] = dt.Minute;
                dateTable["sec"] = dt.Second;
                dateTable["wday"] = (int)dt.DayOfWeek + 1;
                dateTable["yday"] = dt.DayOfYear;
                return DynValue.NewTable(dateTable);
            }
            return DynValue.NewString(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        });
        safeOs["time"] = (System.Func<DynValue?, double>)((timeTable) => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        safeOs["clock"] = () => System.Environment.TickCount / 1000.0;
        safeOs["getenv"] = (System.Func<string, string?>)(env => {
            // Only allow reading specific safe environment variables
            string[] allowedEnvVars = { "PATH", "HOME", "USERNAME", "USER", "TEMP", "TMP", "COMPUTERNAME" };
            foreach (string allowed in allowedEnvVars) {
                if (string.Equals(allowed, env, System.StringComparison.OrdinalIgnoreCase)) {
                    return System.Environment.GetEnvironmentVariable(env);
                }
            }
            return null;
        });
        // temporarily disable os.execute for testing better alternatives via sdk.exec/run_process etc
        /*safeOs["execute"] = (System.Func<string, DynValue>)((command) => {
            // SECURITY: Limited os.execute - only allow safe directory operations
            if (string.IsNullOrWhiteSpace(command)) {
                return DynValue.NewNumber(1); // Error
            }

            string lower = command.Trim().ToLowerInvariant();

            // Detect mkdir
            if (lower.StartsWith("cmd /c mkdir ") || lower.StartsWith("mkdir -p ") || lower.StartsWith("mkdir ")) {
                // naive extraction of the last quoted or last token
                string path = command;
                int lastQuote = command.LastIndexOf('"');
                if (lastQuote >= 0) {
                    int firstQuote = command.LastIndexOf('"', lastQuote - 1);
                    if (firstQuote >= 0) {
                        path = command.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                    }
                } else {
                    string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1) path = parts[^1];
                }
                if (LuaModules.LuaSecurity.IsAllowedPath(path)) {
                    try { System.IO.Directory.CreateDirectory(path); return DynValue.NewNumber(0); } catch { return DynValue.NewNumber(1); }
                }
                return DynValue.NewNumber(1);
            }

            // Detect hardlink creation (Windows mklink /H or ln)
            if (lower.Contains("mklink /h") || lower.StartsWith("ln ")) {
                Core.Utils.EngineSdk.Error($"os.execute blocked for security: '{command}'. Detected file linking. Use sdk.create_hardlink(src, dst) or sdk.create_symlink(src, dst, is_dir).");
                return DynValue.NewNumber(1);
            }

            // Detect copy operations
            if (lower.StartsWith("copy ") || lower.StartsWith("xcopy ") || lower.StartsWith("cp ")) {
                Core.Utils.EngineSdk.Error($"os.execute blocked for security: '{command}'. Detected copy operation. Use sdk.copy_file(src, dst, overwrite) or sdk.copy_dir(src, dst, overwrite).");
                return DynValue.NewNumber(1);
            }

            // Detect move/rename operations
            if (lower.StartsWith("move ") || lower.StartsWith("ren ") || lower.StartsWith("rename ") || lower.StartsWith("mv ")) {
                Core.Utils.EngineSdk.Error($"os.execute blocked for security: '{command}'. Detected move/rename. Use sdk.rename_file(oldPath, newPath) or sdk.move_dir(src, dst, overwrite).");
                return DynValue.NewNumber(1);
            }

            // Detect delete operations
            if (lower.StartsWith("del ") || lower.StartsWith("rm ") || lower.StartsWith("rmdir ") || lower.StartsWith("rd ")) {
                Core.Utils.EngineSdk.Error($"os.execute blocked for security: '{command}'. Detected delete. Use sdk.remove_file(path) or sdk.remove_dir(path).");
                return DynValue.NewNumber(1);
            }

            // Block all other commands
            Core.Utils.EngineSdk.Error($"os.execute blocked for security: '{command}'. Use sdk.exec or sdk.run_process for approved external tools.");
            return DynValue.NewNumber(1); // Error
        });*/
        lua.Globals["os"] = safeOs;
    }

    private void CreateSafeIoTable(Script lua) {
        Table safeIo = new Table(lua);
        safeIo["open"] = (System.Func<string, string?, DynValue>)((path, mode) => {
            // Security: Validate file path with user approval if outside workspace
            if (!LuaModules.LuaSecurity.EnsurePathAllowedWithPrompt(path)) {
                return DynValue.Nil;
            }

            try {
                mode = mode ?? "r";
                bool binaryMode = mode.Contains("b");
                System.IO.FileStream? fs = null;
                if (mode.Contains("r")) {
                    fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                } else if (mode.Contains("w")) {
                    fs = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write);
                } else if (mode.Contains("a")) {
                    fs = new System.IO.FileStream(path, System.IO.FileMode.Append, System.IO.FileAccess.Write);
                }

                if (fs != null) {
                    Table fileHandle = new Table(lua);
                    
                    // Implement file:read() with support for both text and binary modes
                    fileHandle["read"] = (System.Func<DynValue, string?>)((readMode) => {
                        try {
                            // Handle numeric argument: read N bytes (standard Lua behavior)
                            if (readMode.Type == DataType.Number) {
                                int count = (int)readMode.Number;
                                if (count <= 0) return string.Empty;
                                
                                byte[] buffer = new byte[count];
                                int bytesRead = fs.Read(buffer, 0, count);
                                if (bytesRead == 0) return null; // EOF
                                
                                // Return as string with bytes preserved (Lua convention for binary data)
                                return System.Text.Encoding.Latin1.GetString(buffer, 0, bytesRead);
                            }
                            
                            // Handle string format specifiers
                            string? format = readMode.Type == DataType.String ? readMode.String : readMode.ToPrintString();
                            
                            if (binaryMode) {
                                // Binary mode: read operations return raw bytes as Latin1 strings
                                if (format == "*a" || format == "*all") {
                                    long remaining = fs.Length - fs.Position;
                                    if (remaining == 0) return null;
                                    byte[] buffer = new byte[remaining];
                                    int bytesRead = fs.Read(buffer, 0, (int)remaining);
                                    return System.Text.Encoding.Latin1.GetString(buffer, 0, bytesRead);
                                } else if (format == "*l" || format == "*line") {
                                    // Read until newline in binary mode
                                    System.Collections.Generic.List<byte> lineBytes = new System.Collections.Generic.List<byte>();
                                    int b;
                                    while ((b = fs.ReadByte()) != -1) {
                                        if (b == '\n') break;
                                        if (b != '\r') lineBytes.Add((byte)b);
                                    }
                                    return lineBytes.Count == 0 && b == -1 ? null : System.Text.Encoding.Latin1.GetString(lineBytes.ToArray());
                                }
                            } else {
                                // Text mode: use StreamReader for proper text handling
                                if (format == "*a" || format == "*all") {
                                    using System.IO.StreamReader reader = new System.IO.StreamReader(fs, leaveOpen: true);
                                    return reader.ReadToEnd();
                                } else if (format == "*l" || format == "*line") {
                                    using System.IO.StreamReader reader = new System.IO.StreamReader(fs, leaveOpen: true);
                                    return reader.ReadLine();
                                }
                            }
                            return null;
                        } catch { return null; }
                    });
                    
                    // Implement file:seek() for binary file navigation
                    fileHandle["seek"] = (System.Func<string?, long?, long?>)((whence, offset) => {
                        try {
                            whence = whence ?? "cur";
                            offset = offset ?? 0;
                            
                            System.IO.SeekOrigin origin = whence switch {
                                "set" => System.IO.SeekOrigin.Begin,
                                "end" => System.IO.SeekOrigin.End,
                                _ => System.IO.SeekOrigin.Current
                            };
                            
                            return fs.Seek(offset.Value, origin);
                        } catch { return null; }
                    });
                    
                    fileHandle["write"] = (System.Action<string>)((content) => {
                        try {
                            if (binaryMode) {
                                // Binary mode: write raw bytes
                                byte[] bytes = System.Text.Encoding.Latin1.GetBytes(content);
                                fs.Write(bytes, 0, bytes.Length);
                                fs.Flush();
                            } else {
                                // Text mode: use StreamWriter
                                using System.IO.StreamWriter writer = new System.IO.StreamWriter(fs, leaveOpen: true);
                                writer.Write(content);
                                writer.Flush();
                            }
                        } catch {
                            Core.Diagnostics.Bug("io.write failed");
                            /* ignore */
                        }
                    });
                    fileHandle["close"] = (System.Action)(() => {
                        try { fs?.Dispose(); } catch {
                            Core.Diagnostics.Bug("io.close failed");
                            /* ignore */
                        }
                    });
                    fileHandle["flush"] = (System.Action)(() => {
                        try { fs?.Flush(); } catch {
                            Core.Diagnostics.Bug("io.flush failed");
                            /* ignore */
                        }
                    });
                    return DynValue.NewTable(fileHandle);
                }
            } catch {
                return DynValue.Nil;
            }
            return DynValue.Nil;
        });
        // redirect write to EngineSdk.Print for any UI integration
        safeIo["write"] = (System.Action<string>)((content) => Core.Utils.EngineSdk.Print(content));
        //safeIo["flush"] = () => System.Console.Out.Flush(); // removed for now, maybe add later as an event that can be optionally handled by active UI System
        /*safeIo["read"] = (System.Func<string?, string?>)((mode) => { // removed for now, maybe add later with user prompt integration
            try {
                if (mode == "*l" || mode == "*line") {
                    return System.Console.ReadLine();
                }
                return System.Console.In.ReadToEnd();
            } catch { return null; }
        });*/
        // SECURITY: io.popen removed - use sdk.exec/run_process instead
        lua.Globals["io"] = safeIo;
    }

    private void SetupCoreFunctions(Script lua, Core.Tools.IToolResolver tools) {
        // Expose a simple function to resolve tool paths from Lua:
        lua.Globals["tool"] = (System.Func<string, string>)tools.ResolveToolPath;
        Table argvTable = new Table(lua);
        for (int index = 0; index < _args.Length; index++) {
            argvTable[index + 1] = DynValue.NewString(_args[index]);
        }
        lua.Globals["argv"] = argvTable; // array of arguments
        lua.Globals["argc"] = _args.Length; // number of arguments

        // EngineSdk wrappers
        lua.Globals["warn"] = (System.Action<string>)Core.Utils.EngineSdk.Warn;
        lua.Globals["error"] = (System.Action<string>)Core.Utils.EngineSdk.Error;

        // prompt(message, id?, secret?) -> string
        lua.Globals["prompt"] = (System.Func<DynValue, DynValue, DynValue, string>)((message, id, secret) => {
            string msg = message.Type == DataType.String ? message.String : message.ToPrintString();
            string pid = id.Type == DataType.Nil || id.Type == DataType.Void ? "q1" : id.Type == DataType.String ? id.String : id.ToPrintString();
            bool sec = secret.Type == DataType.Boolean && secret.Boolean;
            return Core.Utils.EngineSdk.Prompt(msg, pid, sec);
        });
        lua.Globals["color_prompt"] = (System.Func<DynValue, DynValue, DynValue, DynValue, string>)((message, color, id, secret) => {
            string msg = message.Type == DataType.String ? message.String : message.ToPrintString();
            string col = color.Type == DataType.String ? color.String : color.ToPrintString();
            string pid = id.Type == DataType.Nil || id.Type == DataType.Void ? "q1" : id.Type == DataType.String ? id.String : id.ToPrintString();
            bool sec = secret.Type == DataType.Boolean && secret.Boolean;
            return Core.Utils.EngineSdk.color_prompt(msg, col, pid, sec);
        });
        lua.Globals["colour_prompt"] = lua.Globals["color_prompt"]; // (Correct) AU spelling

        // integrate #if DEBUG checks into lua to allow debug-only code paths when running engine with debugger attached
        lua.Globals["DEBUG"] = System.Diagnostics.Debugger.IsAttached;

        // Diagnostics logging pass-through for Lua scripts
        Table DiagnosticsMethods = new Table(lua);
        // Diagnostics.Log(message) -> logs to lua.log and trace.log
        DiagnosticsMethods["Log"] = (System.Action<string>)Core.Diagnostics.LuaLogger.LuaLog;
        // TODO: (in Core.Diagnostics) create a sub class for lua logging that mirrors Diagnostics class, to log all types (Info, Bug, etc) to logs/lua.log
        // then expose those methods here as well

        lua.Globals["Diagnostics"] = DiagnosticsMethods;

        // progress(total, id?, label?) -> Core.Utils.EngineSdk.PanelProgress userdata
        lua.Globals["progress"] = (System.Func<int, string?, string?, Core.Utils.EngineSdk.PanelProgress>)((total, id, label) => {
            string pid = string.IsNullOrEmpty(id) ? "p1" : id!;
            return new Core.Utils.EngineSdk.PanelProgress(total, pid, label);
        });

        // script_progress(total, id?, label?) -> Core.Utils.EngineSdk.ScriptProgress userdata
        // Usage: local s = script_progress(5, 'setup', 'Initialization'); s:Update()
        lua.Globals["script_progress"] = (System.Func<int, string?, string?, Core.Utils.EngineSdk.ScriptProgress>)((total, id, label) => {
            string pid = string.IsNullOrEmpty(id) ? "s1" : id!;
            return new Core.Utils.EngineSdk.ScriptProgress(total, pid, label);
        });

    }
}
