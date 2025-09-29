using MoonSharp.Interpreter;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using EngineNet.Core.ScriptEngines.Helpers;
using EngineNet.Tools;
using EngineNet.Core.ScriptEngines.LuaModules;

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
public sealed class LuaScriptAction : IAction {
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
        
        // Security: Use controlled Lua environment (not full sandbox as it blocks too much)
        Script lua = new Script(CoreModules.Preset_Default);
        
        // Setup safe Lua environment
        SetupSafeLuaEnvironment(lua);

        // Expose core functions
        SetupCoreFunctions(lua, tools);

        // Register UserData types
        UserData.RegisterType<EngineSdk.Progress>();
        UserData.RegisterType<SqliteHandle>();

        // Setup SDK and modules
        lua.Globals["sdk"] = LuaSdkModule.CreateSdkModule(lua, tools);
        lua.Globals["sqlite"] = LuaSqliteModule.CreateSqliteModule(lua);

        // Preload minimal shims for LuaFileSystem (lfs) and dkjson used by game modules
        LuaShimModules.PreloadShimModules(lua, _scriptPath);
        
        Console.WriteLine($"Running lua script '{_scriptPath}' with {_args.Length} args...");
        Console.WriteLine($"input args: {String.Join(", ", _args)}");
        await Task.Run(() => lua.DoString(code), cancellationToken);
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
        safeOs["date"] = (Func<String?, DynValue>)((format) => {
            if (String.IsNullOrEmpty(format)) return DynValue.NewNumber(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            // Only allow safe date formats, no os.date("!%c", os.execute(...)) exploits
            if (format.StartsWith("*t") || format.StartsWith("!*t")) {
                // Return table format - safe
                var dt = DateTimeOffset.Now;
                Table dateTable = new Table(lua);
                dateTable["year"] = dt.Year;
                dateTable["month"] = dt.Month; 
                dateTable["day"] = dt.Day;
                dateTable["hour"] = dt.Hour;
                dateTable["min"] = dt.Minute;
                dateTable["sec"] = dt.Second;
                dateTable["wday"] = (Int32)dt.DayOfWeek + 1;
                dateTable["yday"] = dt.DayOfYear;
                return DynValue.NewTable(dateTable);
            }
            return DynValue.NewString(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        });
        safeOs["time"] = (Func<DynValue?, Double>)((timeTable) => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        safeOs["clock"] = (Func<Double>)(() => (Double)Environment.TickCount / 1000.0);
        safeOs["getenv"] = (Func<String, String?>)(env => {
            // Only allow reading specific safe environment variables
            String[] allowedEnvVars = { "PATH", "HOME", "USERNAME", "USER", "TEMP", "TMP", "COMPUTERNAME" };
            foreach (String allowed in allowedEnvVars) {
                if (String.Equals(allowed, env, StringComparison.OrdinalIgnoreCase)) {
                    return Environment.GetEnvironmentVariable(env);
                }
            }
            return null;
        });
        safeOs["execute"] = (Func<String, DynValue>)((command) => {
            // SECURITY: Limited os.execute - only allow safe directory operations
            if (String.IsNullOrWhiteSpace(command)) {
                return DynValue.NewNumber(1); // Error
            }

            String lower = command.Trim().ToLowerInvariant();

            // Detect mkdir
            if (lower.StartsWith("cmd /c mkdir ") || lower.StartsWith("mkdir -p ") || lower.StartsWith("mkdir ")) {
                // naive extraction of the last quoted or last token
                String path = command;
                Int32 lastQuote = command.LastIndexOf('"');
                if (lastQuote >= 0) {
                    Int32 firstQuote = command.LastIndexOf('"', lastQuote - 1);
                    if (firstQuote >= 0) {
                        path = command.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                    }
                } else {
                    String[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1) path = parts[^1];
                }
                if (LuaSecurity.IsAllowedPath(path)) {
                    try { Directory.CreateDirectory(path); return DynValue.NewNumber(0); } catch { return DynValue.NewNumber(1); }
                }
                return DynValue.NewNumber(1);
            }

            // Detect hardlink creation (Windows mklink /H or ln)
            if (lower.Contains("mklink /h") || lower.StartsWith("ln ")) {
                EngineSdk.Error($"os.execute blocked for security: '{command}'. Detected file linking. Use sdk.create_hardlink(src, dst) or sdk.create_symlink(src, dst, is_dir).");
                return DynValue.NewNumber(1);
            }

            // Detect copy operations
            if (lower.StartsWith("copy ") || lower.StartsWith("xcopy ") || lower.StartsWith("cp ")) {
                EngineSdk.Error($"os.execute blocked for security: '{command}'. Detected copy operation. Use sdk.copy_file(src, dst, overwrite) or sdk.copy_dir(src, dst, overwrite).");
                return DynValue.NewNumber(1);
            }

            // Detect move/rename operations
            if (lower.StartsWith("move ") || lower.StartsWith("ren ") || lower.StartsWith("rename ") || lower.StartsWith("mv ")) {
                EngineSdk.Error($"os.execute blocked for security: '{command}'. Detected move/rename. Use sdk.rename_file(oldPath, newPath) or sdk.move_dir(src, dst, overwrite).");
                return DynValue.NewNumber(1);
            }

            // Detect delete operations
            if (lower.StartsWith("del ") || lower.StartsWith("rm ") || lower.StartsWith("rmdir ") || lower.StartsWith("rd ")) {
                EngineSdk.Error($"os.execute blocked for security: '{command}'. Detected delete. Use sdk.remove_file(path) or sdk.remove_dir(path).");
                return DynValue.NewNumber(1);
            }
            
            // Block all other commands
            EngineSdk.Error($"os.execute blocked for security: '{command}'. Use sdk.exec or sdk.run_process for approved external tools.");
            return DynValue.NewNumber(1); // Error
        });
        lua.Globals["os"] = safeOs;
    }

    private void CreateSafeIoTable(Script lua) {
        Table safeIo = new Table(lua);
        safeIo["open"] = (Func<String, String?, DynValue>)((path, mode) => {
            // Security: Validate file path with user approval if outside workspace
            if (!LuaSecurity.EnsurePathAllowedWithPrompt(path)) {
                return DynValue.Nil;
            }
            
            try {
                mode = mode ?? "r";
                FileStream? fs = null;
                if (mode.Contains("r")) {
                    fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                } else if (mode.Contains("w")) {
                    fs = new FileStream(path, FileMode.Create, FileAccess.Write);
                } else if (mode.Contains("a")) {
                    fs = new FileStream(path, FileMode.Append, FileAccess.Write);
                }
                
                if (fs != null) {
                    Table fileHandle = new Table(lua);
                    fileHandle["read"] = (Func<String?, String?>)((readMode) => {
                        try {
                            if (readMode == "*a" || readMode == "*all") {
                                using StreamReader reader = new StreamReader(fs, leaveOpen: true);
                                return reader.ReadToEnd();
                            } else if (readMode == "*l" || readMode == "*line") {
                                using StreamReader reader = new StreamReader(fs, leaveOpen: true);
                                return reader.ReadLine();
                            }
                            return null;
                        } catch { return null; }
                    });
                    fileHandle["write"] = (Action<String>)((content) => {
                        try {
                            using StreamWriter writer = new StreamWriter(fs, leaveOpen: true);
                            writer.Write(content);
                            writer.Flush();
                        } catch { /* ignore */ }
                    });
                    fileHandle["close"] = (Action)(() => {
                        try { fs?.Dispose(); } catch { /* ignore */ }
                    });
                    fileHandle["flush"] = (Action)(() => {
                        try { fs?.Flush(); } catch { /* ignore */ }
                    });
                    return DynValue.NewTable(fileHandle);
                }
            } catch {
                return DynValue.Nil;
            }
            return DynValue.Nil;
        });
        safeIo["write"] = (Action<String>)((content) => Console.Write(content));
        safeIo["flush"] = (Action)(() => Console.Out.Flush());
        safeIo["read"] = (Func<String?, String?>)((mode) => {
            try {
                if (mode == "*l" || mode == "*line") {
                    return Console.ReadLine();
                }
                return Console.In.ReadToEnd();
            } catch { return null; }
        });
        // SECURITY: io.popen removed - use sdk.exec/run_process instead
        lua.Globals["io"] = safeIo;
    }

    private void SetupCoreFunctions(Script lua, IToolResolver tools) {
        // Expose a simple function to resolve tool paths from Lua:
        lua.Globals["tool"] = (Func<String, String>)tools.ResolveToolPath;
        lua.Globals["argv"] = _args;

        // EngineSdk wrappers
        lua.Globals["warn"] = (Action<String>)EngineSdk.Warn;
        lua.Globals["error"] = (Action<String>)EngineSdk.Error;

        // emit(event, data?) where data is an optional Lua table
        lua.Globals["emit"] = (Action<DynValue, DynValue>)((ev, data) => {
            String evName = ev.Type == DataType.String ? ev.String : ev.ToPrintString();
            IDictionary<String, Object?>? dict = data.Type == DataType.Nil || data.Type == DataType.Void ? null : LuaUtilities.TableToDictionary(data.Table);
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
    }
}