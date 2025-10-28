using MoonSharp.Interpreter;

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
internal sealed class LuaScriptAction : Helpers.IAction {
    private readonly string _scriptPath;
    private readonly string[] _args;

    public LuaScriptAction(string scriptPath) : this(scriptPath, System.Array.Empty<string>()) { }

    public LuaScriptAction(string scriptPath, IEnumerable<string>? args) {
        _scriptPath = scriptPath;
        _args = args is null ? System.Array.Empty<string>() : args as string[] ?? new List<string>(args).ToArray();
    }

    public async System.Threading.Tasks.Task ExecuteAsync(Tools.IToolResolver tools, System.Threading.CancellationToken cancellationToken = default) {
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
        UserData.RegisterType<Helpers.EngineSdk.Progress>();
        UserData.RegisterType<LuaModules.SqliteHandle>();

        // Setup SDK and modules
        lua.Globals["sdk"] = LuaModules.LuaSdkModule.CreateSdkModule(lua, tools);
        lua.Globals["sqlite"] = LuaModules.LuaSqliteModule.CreateSqliteModule(lua);

        // Preload minimal shims for LuaFileSystem (lfs) and dkjson used by game modules
        LuaModules.LuaShimModules.PreloadShimModules(lua, _scriptPath);

        // TODO use sdk print
        //Console.WriteLine($"Running lua script '{_scriptPath}' with {_args.Length} args...");
        //Console.WriteLine($"input args: {string.Join(", ", _args)}");
        await System.Threading.Tasks.Task.Run(() => lua.DoString(code), cancellationToken);
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
        safeOs["execute"] = (System.Func<string, DynValue>)((command) => {
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
                Helpers.EngineSdk.Error($"os.execute blocked for security: '{command}'. Detected file linking. Use sdk.create_hardlink(src, dst) or sdk.create_symlink(src, dst, is_dir).");
                return DynValue.NewNumber(1);
            }

            // Detect copy operations
            if (lower.StartsWith("copy ") || lower.StartsWith("xcopy ") || lower.StartsWith("cp ")) {
                Helpers.EngineSdk.Error($"os.execute blocked for security: '{command}'. Detected copy operation. Use sdk.copy_file(src, dst, overwrite) or sdk.copy_dir(src, dst, overwrite).");
                return DynValue.NewNumber(1);
            }

            // Detect move/rename operations
            if (lower.StartsWith("move ") || lower.StartsWith("ren ") || lower.StartsWith("rename ") || lower.StartsWith("mv ")) {
                Helpers.EngineSdk.Error($"os.execute blocked for security: '{command}'. Detected move/rename. Use sdk.rename_file(oldPath, newPath) or sdk.move_dir(src, dst, overwrite).");
                return DynValue.NewNumber(1);
            }

            // Detect delete operations
            if (lower.StartsWith("del ") || lower.StartsWith("rm ") || lower.StartsWith("rmdir ") || lower.StartsWith("rd ")) {
                Helpers.EngineSdk.Error($"os.execute blocked for security: '{command}'. Detected delete. Use sdk.remove_file(path) or sdk.remove_dir(path).");
                return DynValue.NewNumber(1);
            }

            // Block all other commands
            Helpers.EngineSdk.Error($"os.execute blocked for security: '{command}'. Use sdk.exec or sdk.run_process for approved external tools.");
            return DynValue.NewNumber(1); // Error
        });
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
                    fileHandle["read"] = (System.Func<string?, string?>)((readMode) => {
                        try {
                            if (readMode == "*a" || readMode == "*all") {
                                using System.IO.StreamReader reader = new System.IO.StreamReader(fs, leaveOpen: true);
                                return reader.ReadToEnd();
                            } else if (readMode == "*l" || readMode == "*line") {
                                using System.IO.StreamReader reader = new System.IO.StreamReader(fs, leaveOpen: true);
                                return reader.ReadLine();
                            }
                            return null;
                        } catch { return null; }
                    });
                    fileHandle["write"] = (System.Action<string>)((content) => {
                        try {
                            using System.IO.StreamWriter writer = new System.IO.StreamWriter(fs, leaveOpen: true);
                            writer.Write(content);
                            writer.Flush();
                        } catch { /* ignore */ }
                    });
                    fileHandle["close"] = (System.Action)(() => {
                        try { fs?.Dispose(); } catch { /* ignore */ }
                    });
                    fileHandle["flush"] = (System.Action)(() => {
                        try { fs?.Flush(); } catch { /* ignore */ }
                    });
                    return DynValue.NewTable(fileHandle);
                }
            } catch {
                return DynValue.Nil;
            }
            return DynValue.Nil;
        });
        safeIo["write"] = (System.Action<string>)((content) => Program.Direct.Console.Write(content));
        safeIo["flush"] = () => Program.Direct.Console.Out.Flush();
        safeIo["read"] = (System.Func<string?, string?>)((mode) => {
            try {
                if (mode == "*l" || mode == "*line") {
                    return Program.Direct.Console.ReadLine();
                }
                return Program.Direct.Console.In.ReadToEnd();
            } catch { return null; }
        });
        // SECURITY: io.popen removed - use sdk.exec/run_process instead
        lua.Globals["io"] = safeIo;
    }

    private void SetupCoreFunctions(Script lua, Tools.IToolResolver tools) {
        // Expose a simple function to resolve tool paths from Lua:
        lua.Globals["tool"] = (System.Func<string, string>)tools.ResolveToolPath;
        Table argvTable = new Table(lua);
        for (int index = 0; index < _args.Length; index++) {
            argvTable[index + 1] = DynValue.NewString(_args[index]);
        }
        lua.Globals["argv"] = argvTable;
        lua.Globals["argc"] = _args.Length;

        // EngineSdk wrappers
        lua.Globals["warn"] = (System.Action<string>)Helpers.EngineSdk.Warn;
        lua.Globals["error"] = (System.Action<string>)Helpers.EngineSdk.Error;

        // emit(event, data?) where data is an optional Lua table
        lua.Globals["emit"] = (System.Action<DynValue, DynValue>)((ev, data) => {
            string evName = ev.Type == DataType.String ? ev.String : ev.ToPrintString();
            IDictionary<string, object?>? dict = data.Type == DataType.Nil || data.Type == DataType.Void ? null : LuaModules.LuaUtilities.TableToDictionary(data.Table);
            Helpers.EngineSdk.Emit(evName, dict);
        });

        // prompt(message, id?, secret?) -> string
        lua.Globals["prompt"] = (System.Func<DynValue, DynValue, DynValue, string>)((message, id, secret) => {
            string msg = message.Type == DataType.String ? message.String : message.ToPrintString();
            string pid = id.Type == DataType.Nil || id.Type == DataType.Void ? "q1" : id.Type == DataType.String ? id.String : id.ToPrintString();
            bool sec = secret.Type == DataType.Boolean && secret.Boolean;
            return Helpers.EngineSdk.Prompt(msg, pid, sec);
        });

        // progress(total, id?, label?) -> Helpers.EngineSdk.Progress userdata
        lua.Globals["progress"] = (System.Func<int, string?, string?, Helpers.EngineSdk.Progress>)((total, id, label) => {
            string pid = string.IsNullOrEmpty(id) ? "p1" : id!;
            return new Helpers.EngineSdk.Progress(total, pid, label);
        });
    }
}