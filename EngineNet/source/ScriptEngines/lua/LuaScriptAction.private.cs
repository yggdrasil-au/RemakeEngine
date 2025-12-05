using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.lua;

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
internal sealed partial class LuaScriptAction : ScriptEngines.Helpers.IAction {
    private readonly string _scriptPath;
    private readonly string[] _args;

    // called by LuaScriptAction.ExecuteAsync()
    /// <summary>
    /// setup a Lua environment with restricted access to built-in libraries for sandboxing
    /// </summary>
    /// <param name="LuaEnvObj"></param>
    private void SetupSafeLuaEnvironment(LuaWorld LuaEnvObj) {
        // Remove dangerous standard library functions but preserve package/require system
        LuaEnvObj.LuaScript.Globals["loadfile"] = DynValue.Nil;     // Remove ability to load arbitrary files
        LuaEnvObj.LuaScript.Globals["dofile"] = DynValue.Nil;       // Remove ability to execute arbitrary files

        // Remove dangerous io functions but keep basic ones
        if (LuaEnvObj.LuaScript.Globals.Get("io").Type == DataType.Table) {
            Table ioTable = LuaEnvObj.LuaScript.Globals.Get("io").Table;
            ioTable["popen"] = DynValue.Nil;        // Remove io.popen (command execution)
        }

        // Create safe os table with limited functionality
        CreateSafeOsTable(LuaEnvObj);

        // Create safe io table for basic file operations within workspace
        CreateSafeIoTable(LuaEnvObj);
    }

    // :: helpers for SetupSafeLuaEnvironment()
    /// <summary>
    /// Replace the lua built-in os table with a sandboxed version
    /// </summary>
    /// <param name="lua"></param>
    private void CreateSafeOsTable(LuaWorld LuaEnvObj) {
        LuaEnvObj.os["date"] = (System.Func<string?, DynValue>)((format) => {
            if (string.IsNullOrEmpty(format)) return DynValue.NewNumber(System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            // Only allow safe date formats, no os.date("!%c", os.execute(...)) exploits
            if (format.StartsWith("*t") || format.StartsWith("!*t")) {
                // Return table format - safe
                var dt = System.DateTimeOffset.Now;
                //Table dateTable = new Table(LuaEnvObj.LuaScript);
                LuaEnvObj.dateTable["year"] = dt.Year;
                LuaEnvObj.dateTable["month"] = dt.Month;
                LuaEnvObj.dateTable["day"] = dt.Day;
                LuaEnvObj.dateTable["hour"] = dt.Hour;
                LuaEnvObj.dateTable["min"] = dt.Minute;
                LuaEnvObj.dateTable["sec"] = dt.Second;
                LuaEnvObj.dateTable["wday"] = (int)dt.DayOfWeek + 1;
                LuaEnvObj.dateTable["yday"] = dt.DayOfYear;
                return DynValue.NewTable(LuaEnvObj.dateTable);
            }
            return DynValue.NewString(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        });
        LuaEnvObj.os["time"] = (System.Func<DynValue?, double>)((timeTable) => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        LuaEnvObj.os["clock"] = () => System.Environment.TickCount / 1000.0;
        LuaEnvObj.os["getenv"] = (System.Func<string, string?>)(env => {
            // Only allow reading specific safe environment variables
            string[] allowedEnvVars = { "PATH", "HOME", "USERNAME", "USER", "TEMP", "TMP", "COMPUTERNAME" };
            foreach (string allowed in allowedEnvVars) {
                if (string.Equals(allowed, env, System.StringComparison.OrdinalIgnoreCase)) {
                    return System.Environment.GetEnvironmentVariable(env);
                }
            }
            return null;
        });
        // removed os.execute for better alternatives via sdk.exec/run_process etc
        //safeOs["execute"]

        LuaEnvObj.LuaScript.Globals["os"] = LuaEnvObj.os;
    }

    /// <summary>
    /// Replace the lua built-in io table with a sandboxed version
    /// </summary>
    /// <param name="lua"></param>
    private void CreateSafeIoTable(LuaWorld LuaEnvObj) {
        LuaEnvObj.io["open"] = (System.Func<string, string?, DynValue>)((path, mode) => {
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
                    // Implement file:read() with support for both text and binary modes
                    LuaEnvObj.fileHandle["read"] = (System.Func<DynValue, string?>)((readMode) => {
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
                    LuaEnvObj.fileHandle["seek"] = (System.Func<string?, long?, long?>)((whence, offset) => {
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

                    LuaEnvObj.fileHandle["write"] = (System.Action<string>)((content) => {
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
                    LuaEnvObj.fileHandle["close"] = (System.Action)(() => {
                        try { fs?.Dispose(); } catch {
                            Core.Diagnostics.Bug("io.close failed");
                            /* ignore */
                        }
                    });
                    LuaEnvObj.fileHandle["flush"] = (System.Action)(() => {
                        try { fs?.Flush(); } catch {
                            Core.Diagnostics.Bug("io.flush failed");
                            /* ignore */
                        }
                    });
                    return DynValue.NewTable(LuaEnvObj.fileHandle);
                }
            } catch {
                return DynValue.Nil;
            }
            return DynValue.Nil;
        });
        // Wrap io.write to EngineSdk.Print
        LuaEnvObj.io["write"] = (System.Action<string>)((content) => Core.Utils.EngineSdk.Print(content));
        //safeIo["flush"] = // removed for now, maybe add later as an event that can be optionally handled by active UI System
        //safeIo["read"] =  // removed for now,

        // SECURITY: io.popen removed - use sdk.exec/run_process instead
        LuaEnvObj.LuaScript.Globals["io"] = LuaEnvObj.io;
    }
    // :: end helpers for SetupSafeLuaEnvironment()
    //
    //

    // :: called by LuaScriptAction.ExecuteAsync()
    /// <summary>
    /// Define important core functions as Lua globals
    /// </summary>
    /// <param name="lua"></param>
    /// <param name="tools"></param>
    private void SetupCoreFunctions(LuaWorld LuaEnvObj, Core.Tools.IToolResolver tools) {

        // Setup SDK and modules
        LuaEnvObj.LuaScript.Globals["sdk"] = LuaModules.LuaSdkModule.CreateSdkModule(LuaEnvObj, tools);
        LuaEnvObj.LuaScript.Globals["sqlite"] = LuaModules.LuaSqliteModule.CreateSqliteModule(LuaEnvObj);

        // Expose a function to resolve tool path
        LuaEnvObj.LuaScript.Globals["tool"] = (System.Func<string, string>)tools.ResolveToolPath;
        LuaEnvObj.LuaScript.Globals["ResolveToolPath"] = (System.Func<string, string>)tools.ResolveToolPath; // more internally accurate name

        // Expose script arguments as argv array and argc count
        Table argvTable = new Table(LuaEnvObj.LuaScript);
        for (int index = 0; index < _args.Length; index++) {
            argvTable[index + 1] = DynValue.NewString(_args[index]);
        }
        LuaEnvObj.LuaScript.Globals["argv"] = argvTable; // array of arguments
        LuaEnvObj.LuaScript.Globals["argc"] = _args.Length; // number of arguments


        // :: start :: methods for emitting engineSDK events from Lua scripts ::

        // basic outputs for warning and error events
        LuaEnvObj.LuaScript.Globals["warn"] = (System.Action<string>)Core.Utils.EngineSdk.Warn;
        LuaEnvObj.LuaScript.Globals["error"] = (System.Action<string>)Core.Utils.EngineSdk.Error;

        // emits the prompt query to the engine/ui and returns the user input
        LuaEnvObj.LuaScript.Globals["prompt"] = (System.Func<DynValue, DynValue, DynValue, string>)((message, id, secret) => {
            string msg = message.Type == DataType.String ? message.String : message.ToPrintString();
            string pid = id.Type == DataType.Nil || id.Type == DataType.Void ? "q1" : id.Type == DataType.String ? id.String : id.ToPrintString();
            bool sec = secret.Type == DataType.Boolean && secret.Boolean;
            return Core.Utils.EngineSdk.Prompt(msg, pid, sec);
        });
        LuaEnvObj.LuaScript.Globals["color_prompt"] = (System.Func<DynValue, DynValue, DynValue, DynValue, string>)((message, color, id, secret) => {
            string msg = message.Type == DataType.String ? message.String : message.ToPrintString();
            string col = color.Type == DataType.String ? color.String : color.ToPrintString();
            string pid = id.Type == DataType.Nil || id.Type == DataType.Void ? "q1" : id.Type == DataType.String ? id.String : id.ToPrintString();
            bool sec = secret.Type == DataType.Boolean && secret.Boolean;
            return Core.Utils.EngineSdk.color_prompt(msg, col, pid, sec);
        });
        LuaEnvObj.LuaScript.Globals["colour_prompt"] = LuaEnvObj.LuaScript.Globals["color_prompt"]; // (Correct) AU spelling

        // progress(total, id?, label?) -> Core.Utils.EngineSdk.PanelProgress userdata
        LuaEnvObj.LuaScript.Globals["progress"] = (System.Func<int, string?, string?, Core.Utils.EngineSdk.PanelProgress>)((total, id, label) => {
            string pid = string.IsNullOrEmpty(id) ? "p1" : id!;
            return new Core.Utils.EngineSdk.PanelProgress(total, pid, label);
        });

        // script_progress(total, id?, label?) -> Core.Utils.EngineSdk.ScriptProgress userdata
        // Usage: local s = script_progress(5, 'setup', 'Initialization'); s:Update()
        LuaEnvObj.LuaScript.Globals["script_progress"] = (System.Func<int, string?, string?, Core.Utils.EngineSdk.ScriptProgress>)((total, id, label) => {
            string pid = string.IsNullOrEmpty(id) ? "s1" : id!;
            return new Core.Utils.EngineSdk.ScriptProgress(total, pid, label);
        });

        // :: end ::
        //
        // :: start :: Debugging features ::

        // integrate #if DEBUG checks into lua to allow debug-only code paths when running engine with debugger attached
#if DEBUG
        LuaEnvObj.LuaScript.Globals["DEBUG"] = true;
#else
        LuaEnvObj.LuaScript.Globals["DEBUG"] = false;
#endif

        // :: Lua Diagnostics logging ::
        //Table DiagnosticsMethods = new Table(lua); // Diagnostics table of methods
        LuaEnvObj.DiagnosticsMethods["Log"] = (System.Action<string>)Core.Diagnostics.LuaLogger.LuaLog;
        LuaEnvObj.DiagnosticsMethods["Trace"] = (System.Action<string>)Core.Diagnostics.LuaLogger.LuaTrace;

        LuaEnvObj.LuaScript.Globals["Diagnostics"] = LuaEnvObj.DiagnosticsMethods;

        // :: end ::

    }

}
