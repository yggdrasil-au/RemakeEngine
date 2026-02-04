using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Jint;
using Jint.Native;
using EngineNet.ScriptEngines.Js;

namespace EngineNet.ScriptEngines.Js;

internal sealed partial class JsScriptAction : ScriptEngines.Helpers.IAction {
    private readonly string _scriptPath;
    private readonly string[] _args;
    private readonly string _gameRoot;
    private readonly string _projectRoot;

    // called by JsScriptAction.ExecuteAsync()
    /// <summary>
    /// setup a JS environment with restricted access to built-in libraries for sandboxing
    /// </summary>
    /// <param name="JSEnvObj"></param>
    private void SetupSafeJSEnvironment(JsWorld JSEnvObj) {
        // Remove dangerous standard library functions
        // Jint is secure by default, but we explicitly nullify common Lua-like globals if they were to exist
        JSEnvObj.JsEngineScript.SetValue("loadfile", JsValue.Null);
        JSEnvObj.JsEngineScript.SetValue("dofile", JsValue.Null);

        // Remove dangerous io functions
        // Note: We are building the io object from scratch below, so we don't need to remove 'popen'
        // from an existing object, but we ensure the global 'io' is controlled.

        // Create safe os table with limited functionality
        CreateSafeOsTable(JSEnvObj);

        // Create safe io table for basic file operations within workspace
        CreateSafeIoTable(JSEnvObj);
    }

    // :: helpers for SetupSafeJSEnvironment()
    /// <summary>
    /// Wrapper for IToolResolver that injects module-specific tool versions
    /// </summary>
    private class ContextualToolResolver : Core.ExternalTools.IToolResolver {
        private readonly Core.ExternalTools.IToolResolver _base;
        private readonly Dictionary<string, string> _contextVersions;
        public ContextualToolResolver(Core.ExternalTools.IToolResolver baseResolver, Dictionary<string, string> contextVersions) {
            _base = baseResolver;
            _contextVersions = contextVersions;
        }
        public string ResolveToolPath(string toolId, string? version = null) {
            if (version == null && _contextVersions.TryGetValue(toolId, out var v)) {
                version = v;
            }
            return _base.ResolveToolPath(toolId, version);
        }
    }

    private Dictionary<string, string> LoadModuleToolVersions() {
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string toolsTomlPath = System.IO.Path.Combine(_gameRoot, "Tools.toml");
        if (System.IO.File.Exists(toolsTomlPath)) {
            var toolsList = Core.ExternalTools.SimpleToml.ReadTools(toolsTomlPath);
            foreach (var tool in toolsList) {
                if (tool.TryGetValue("name", out object? name) && name is not null && 
                    tool.TryGetValue("version", out object? version) && version is not null) {
                    versions[name.ToString()!] = version.ToString()!;
                }
            }
        }
        return versions;
    }

    /// <summary>
    /// Replace the JS built-in os table with a sandboxed version
    /// </summary>
    /// <param name="JSEnvObj"></param>
    private void CreateSafeOsTable(JsWorld JSEnvObj) {
        JSEnvObj.os["date"] = (Func<string?, JsValue>)((format) => {
            if (string.IsNullOrEmpty(format)) return JsValue.FromObject(JSEnvObj.JsEngineScript, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            // Only allow safe date formats
            if (format.StartsWith("*t") || format.StartsWith("!*t")) {
                var dt = DateTimeOffset.Now;
                var dateObj = new Dictionary<string, object> {
                    { "year", dt.Year },
                    { "month", dt.Month },
                    { "day", dt.Day },
                    { "hour", dt.Hour },
                    { "min", dt.Minute },
                    { "sec", dt.Second },
                    { "wday", (int)dt.DayOfWeek + 1 },
                    { "yday", dt.DayOfYear }
                };
                return JsValue.FromObject(JSEnvObj.JsEngineScript, dateObj);
            }
            return JsValue.FromObject(JSEnvObj.JsEngineScript, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        });

        JSEnvObj.os["time"] = (Func<JsValue, double>)((timeTable) => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        JSEnvObj.os["clock"] = (Func<double>)(() => Environment.TickCount / 1000.0);

        JSEnvObj.os["getenv"] = (Func<string, string?>)(env => {
            // Only allow reading specific safe environment variables
            string[] allowedEnvVars = { "PATH", "HOME", "USERNAME", "USER", "TEMP", "TMP", "COMPUTERNAME" };
            foreach (string allowed in allowedEnvVars) {
                if (string.Equals(allowed, env, StringComparison.OrdinalIgnoreCase)) {
                    return Environment.GetEnvironmentVariable(env);
                }
            }
            return null;
        });

        // os.exit - allow script to call it without error, but do nothing
        JSEnvObj.os["exit"] = (Action<int?>)(code => { });

        JSEnvObj.JsEngineScript.SetValue("os", JSEnvObj.os);
    }

    /// <summary>
    /// Replace the JS built-in io table with a sandboxed version
    /// </summary>
    /// <param name="JSEnvObj"></param>
    private void CreateSafeIoTable(JsWorld JSEnvObj) {
        JSEnvObj.io["open"] = (Func<string, string?, JsValue>)((path, mode) => {

            // TODO: add JsSecurity.cs mimicking LuaSecurity.cs
            // Security: Validate file path with user approval if outside workspace
            /*if (!JSModules.JSSecurity.EnsurePathAllowedWithPrompt(path)) {
                return JsValue.Null;
            }*/

            try {
                mode = mode ?? "r";
                bool binaryMode = mode.Contains("b");
                FileStream? fs = null;

                if (mode.Contains("r")) {
                    fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                } else if (mode.Contains("w")) {
                    fs = new FileStream(path, FileMode.Create, FileAccess.Write);
                } else if (mode.Contains("a")) {
                    fs = new FileStream(path, FileMode.Append, FileAccess.Write);
                }

                if (fs != null) {
                    // Create a specific object for this file handle
                    var fileHandle = new Dictionary<string, object>();

                    // Implement file:read()
                    fileHandle["read"] = (Func<JsValue, string?>)((readMode) => {
                        try {
                            // Handle numeric argument: read N bytes
                            if (readMode.IsNumber()) {
                                int count = (int)readMode.AsNumber();
                                if (count <= 0) return string.Empty;

                                byte[] buffer = new byte[count];
                                int bytesRead = fs.Read(buffer, 0, count);
                                if (bytesRead == 0) return null; // EOF

                                // Return as string with bytes preserved (Binary data)
                                return Encoding.Latin1.GetString(buffer, 0, bytesRead);
                            }

                            // Handle string format specifiers
                            string format = readMode.IsString() ? readMode.AsString() : readMode.ToString();

                            if (binaryMode) {
                                if (format == "*a" || format == "*all") {
                                    long remaining = fs.Length - fs.Position;
                                    if (remaining == 0) return null;
                                    byte[] buffer = new byte[remaining];
                                    int bytesRead = fs.Read(buffer, 0, (int)remaining);
                                    return Encoding.Latin1.GetString(buffer, 0, bytesRead);
                                } else if (format == "*l" || format == "*line") {
                                    List<byte> lineBytes = new List<byte>();
                                    int b;
                                    while ((b = fs.ReadByte()) != -1) {
                                        if (b == '\n') break;
                                        if (b != '\r') lineBytes.Add((byte)b);
                                    }
                                    return lineBytes.Count == 0 && b == -1 ? null : Encoding.Latin1.GetString(lineBytes.ToArray());
                                }
                            } else {
                                // Text mode
                                if (format == "*a" || format == "*all") {
                                    using StreamReader reader = new StreamReader(fs, leaveOpen: true);
                                    return reader.ReadToEnd();
                                } else if (format == "*l" || format == "*line") {
                                    using StreamReader reader = new StreamReader(fs, leaveOpen: true);
                                    return reader.ReadLine();
                                }
                            }
                            return null;
                        } catch (Exception ex) {
                            Core.Diagnostics.JsInternalCatch("io.read failed with exception: " + ex);
                            return null;
                        }
                    });

                    // Implement file:seek()
                    fileHandle["seek"] = (Func<string?, long?, long?>)((whence, offset) => {
                        try {
                            whence = whence ?? "cur";
                            offset = offset ?? 0;

                            SeekOrigin origin = whence switch {
                                "set" => SeekOrigin.Begin,
                                "end" => SeekOrigin.End,
                                _ => SeekOrigin.Current
                            };

                            return fs.Seek(offset.Value, origin);
                        } catch (Exception ex) {
                            Core.Diagnostics.JsInternalCatch("io.seek failed with exception: " + ex);
                            return null;
                        }
                    });

                    fileHandle["write"] = (Action<string>)((content) => {
                        try {
                            if (binaryMode) {
                                byte[] bytes = Encoding.Latin1.GetBytes(content);
                                fs.Write(bytes, 0, bytes.Length);
                                fs.Flush();
                            } else {
                                using StreamWriter writer = new StreamWriter(fs, leaveOpen: true);
                                writer.Write(content);
                                writer.Flush();
                            }
                        } catch (Exception ex) {
                            Core.Diagnostics.JsInternalCatch("io.write failed with exception: " + ex);
                        }
                    });

                    fileHandle["close"] = (Action)(() => {
                        try {
                            fs?.Dispose();
                        } catch (Exception ex) {
                            Core.Diagnostics.JsInternalCatch("io.close failed with exception: " + ex);
                        }
                    });

                    fileHandle["flush"] = (Action)(() => {
                        try {
                            fs?.Flush();
                        } catch (Exception ex) {
                            Core.Diagnostics.JsInternalCatch("io.flush failed with exception: " + ex);
                        }
                    });

                    return JsValue.FromObject(JSEnvObj.JsEngineScript, fileHandle);
                }
            } catch (Exception ex) {
                Core.Diagnostics.JsInternalCatch("io.open failed with exception: " + ex);
                return JsValue.Null;
            }
            return JsValue.Null;
        });

        // Wrap io.write to EngineSdk.Print
        JSEnvObj.io["write"] = (Action<string>)((content) => Core.UI.EngineSdk.Print(content));

        JSEnvObj.JsEngineScript.SetValue("io", JSEnvObj.io);
    }
    // :: end helpers for SetupSafeJSEnvironment()
    //
    //

    // :: called by JsScriptAction.ExecuteAsync()
    /// <summary>
    /// Define important core functions as JS globals
    /// </summary>
    /// <param name="JSEnvObj"></param>
    /// <param name="tools"></param>
    private void SetupCoreFunctions(JsWorld JSEnvObj, Core.ExternalTools.IToolResolver tools) {

        // Setup SDK and modules, TODO: implement these modules
        //JSEnvObj.JsEngineScript.SetValue("sdk", JSModules.JSSdkModule.CreateSdkModule(JSEnvObj, tools));
        //JSEnvObj.JsEngineScript.SetValue("sqlite", JSModules.JSSqliteModule.CreateSqliteModule(JSEnvObj));

        // expose a console object for logging, mapped to EngineSdk.PrintLine
        JSEnvObj.console["log"] = (Action<string>)((message) => Core.UI.EngineSdk.PrintLine(message));
        JSEnvObj.console["warn"] = (Action<string>)((message) => Core.UI.EngineSdk.Warn(message));
        JSEnvObj.console["error"] = (Action<string>)((message) => Core.UI.EngineSdk.Error(message));
        JSEnvObj.JsEngineScript.SetValue("console", JSEnvObj.console);

        // Expose a function to resolve tool path
        JSEnvObj.JsEngineScript.SetValue("tool", (Func<string, string?, string>)((id, ver) => tools.ResolveToolPath(id, ver)));
        JSEnvObj.JsEngineScript.SetValue("ResolveToolPath", (Func<string, string?, string>)((id, ver) => tools.ResolveToolPath(id, ver)));

        // Expose script arguments as argv array and argc count
        // Jint maps string[] directly to a JS Array
        JSEnvObj.JsEngineScript.SetValue("argv", _args);
        JSEnvObj.JsEngineScript.SetValue("argc", _args.Length);

        // get gameroot and projectroot paths
        JSEnvObj.JsEngineScript.SetValue("Game_Root", _gameRoot);
        Core.Diagnostics.Log($"[JsScriptAction.cs::SetupCoreFunctions()] Set Game_Root to '{_gameRoot}'");
        JSEnvObj.JsEngineScript.SetValue("Project_Root", _projectRoot);
        Core.Diagnostics.Log($"[JsScriptAction.cs::SetupCoreFunctions()] Set Project_Root to '{_projectRoot}'");

        // script_dir constant - directory containing the executing script
        string scriptDir = Path.GetDirectoryName(_scriptPath)?.Replace("\\", "/") ?? "";
        JSEnvObj.JsEngineScript.SetValue("script_dir", scriptDir);


        // :: start :: methods for emitting engineSDK events from JS scripts ::

        // basic outputs for warning and error events
        JSEnvObj.JsEngineScript.SetValue("warn", (Action<string>)Core.UI.EngineSdk.Warn);
        JSEnvObj.JsEngineScript.SetValue("error", (Action<string>)Core.UI.EngineSdk.Error);

        // emits the prompt query to the engine/ui and returns the user input
        JSEnvObj.JsEngineScript.SetValue("prompt", (Func<JsValue, JsValue, JsValue, string>)((message, id, secret) => {
            string msg = message.IsString() ? message.AsString() : message.ToString();
            string pid = (id.IsNull() || id.IsUndefined()) ? "q1" : (id.IsString() ? id.AsString() : id.ToString());
            bool sec = secret.IsBoolean() && secret.AsBoolean();
            return Core.UI.EngineSdk.Prompt(msg, pid, sec);
        }));

        JSEnvObj.JsEngineScript.SetValue("color_prompt", (Func<JsValue, JsValue, JsValue, JsValue, string>)((message, color, id, secret) => {
            string msg = message.IsString() ? message.AsString() : message.ToString();
            string col = color.IsString() ? color.AsString() : color.ToString();
            string pid = (id.IsNull() || id.IsUndefined()) ? "q1" : (id.IsString() ? id.AsString() : id.ToString());
            bool sec = secret.IsBoolean() && secret.AsBoolean();
            return Core.UI.EngineSdk.color_prompt(msg, col, pid, sec);
        }));

        // Alias for AU/UK spelling
        JSEnvObj.JsEngineScript.SetValue("colour_prompt", JSEnvObj.JsEngineScript.GetValue("color_prompt"));

        // :: Progress System ::
        Core.UI.EngineSdk.ScriptProgress? activeScriptProgress = null;

        // progress.new(total, id, label) -> Core.UI.EngineSdk.PanelProgress userdata
        JSEnvObj.Progress["new"] = (Func<int, string?, string?, Core.UI.EngineSdk.PanelProgress>)((total, id, label) => {
            string pid = string.IsNullOrEmpty(id) ? "p1" : id!;
            return new Core.UI.EngineSdk.PanelProgress(total, pid, label);
        });

        // progress.start(total, label) -> Core.UI.EngineSdk.ScriptProgress userdata
        JSEnvObj.Progress["start"] = (Func<int, string?, Core.UI.EngineSdk.ScriptProgress>)((total, label) => {
            activeScriptProgress = new Core.UI.EngineSdk.ScriptProgress(total, "s1", label);
            return activeScriptProgress;
        });

        // progress.step(label?)
        JSEnvObj.Progress["step"] = (Action<string?>)((label) => {
            if (activeScriptProgress != null) {
                activeScriptProgress.Update(1, label);
                if (!string.IsNullOrEmpty(label)) {
                    Core.UI.EngineSdk.PrintLine($"[Step {activeScriptProgress.Current}/{activeScriptProgress.Total}] {label}", ConsoleColor.Magenta);
                }
            }
        });

        // progress.add_steps(count)
        JSEnvObj.Progress["add_steps"] = (Action<int>)((count) => {
            if (activeScriptProgress != null) {
                activeScriptProgress.SetTotal(activeScriptProgress.Total + count);
            }
        });

        // progress.finish()
        JSEnvObj.Progress["finish"] = (Action)(() => {
            if (activeScriptProgress != null) {
                activeScriptProgress.Complete();
            }
        });

        JSEnvObj.JsEngineScript.SetValue("progress", JSEnvObj.Progress);

        // :: end ::
        //
        // :: start :: Debugging features ::

#if DEBUG
        JSEnvObj.JsEngineScript.SetValue("DEBUG", true);
#else
        JSEnvObj.JsEngineScript.SetValue("DEBUG", false);
#endif

        // :: JS Diagnostics logging ::
        JSEnvObj.DiagnosticsMethods["Log"] = (Action<string>)Core.Diagnostics.JsLogger.JsLog;
        JSEnvObj.DiagnosticsMethods["Trace"] = (Action<string>)Core.Diagnostics.JsLogger.JsTrace;

        JSEnvObj.JsEngineScript.SetValue("Diagnostics", JSEnvObj.DiagnosticsMethods);

        // :: end ::
    }
}