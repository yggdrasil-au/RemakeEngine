using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua;

// custom exception to signal script exit without treating it as an error
public class ScriptExitException : Exception { }

public static class SetupSafeEnvironment {

    /// <summary>
    /// setup a Lua environment with restricted access to built-in libraries for sandboxing
    /// </summary>
    /// <param name="_LuaWorld"></param>
    public static void LuaEnvironment(LuaWorld _LuaWorld) {
        // Remove dangerous standard library functions but preserve package/require system
        _LuaWorld.LuaScript.Globals["loadfile"] = DynValue.Nil;     // Remove ability to load arbitrary files
        _LuaWorld.LuaScript.Globals["dofile"] = DynValue.Nil;       // Remove ability to execute arbitrary files

        // Remove io
        if (_LuaWorld.LuaScript.Globals.Get("io").Type == DataType.Table) {
            Table ioTable = _LuaWorld.LuaScript.Globals.Get("io").Table;
            ioTable["popen"] = DynValue.Nil;        // Remove io.popen (command execution)
        }

        CreateSafeOsTable(_LuaWorld);

        CreateSafeIoTable(_LuaWorld);
    }

    public static void CreateSafeOsTable(LuaWorld _LuaWorld) {

        // date and time functions

        _LuaWorld.os["date"] = (System.Func<string?, DynValue>)((format) => {
            if (string.IsNullOrEmpty(format)) return DynValue.NewNumber(System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            // Only allow safe date formats, no os.date("!%c", os.execute(...)) exploits
            if (format.StartsWith("*t") || format.StartsWith("!*t")) {
                // Return table format - safe
                var dt = System.DateTimeOffset.Now;
                //Table dateTable = new Table(_LuaWorld.LuaScript);
                _LuaWorld.dateTable["year"] = dt.Year;
                _LuaWorld.dateTable["month"] = dt.Month;
                _LuaWorld.dateTable["day"] = dt.Day;
                _LuaWorld.dateTable["hour"] = dt.Hour;
                _LuaWorld.dateTable["min"] = dt.Minute;
                _LuaWorld.dateTable["sec"] = dt.Second;
                _LuaWorld.dateTable["wday"] = (int)dt.DayOfWeek + 1;
                _LuaWorld.dateTable["yday"] = dt.DayOfYear;
                return DynValue.NewTable(_LuaWorld.dateTable);
            }
            return DynValue.NewString(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        });
        _LuaWorld.os["time"] = (System.Func<DynValue?, double>)((DynValue? timeTable) => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        _LuaWorld.os["clock"] = () => System.Environment.TickCount / 1000.0;


        // getenv - only allow access to a specific set of safe environment variables, and return null for anything else to prevent information leaks
        _LuaWorld.os["getenv"] = (string env) => {
            // Only allow reading specific safe environment variables
            string[] allowedEnvVars = { "PATH", "HOME", "USERNAME", "USER", "TEMP", "TMP", "COMPUTERNAME" };
            foreach (string allowed in allowedEnvVars) {
                if (string.Equals(allowed, env, System.StringComparison.OrdinalIgnoreCase)) {
                    return System.Environment.GetEnvironmentVariable(env);
                }
            }
            return null;
        };
        // removed os.execute for better alternatives via sdk.exec/run_process etc
        _LuaWorld.os["execute"] = DynValue.Nil;

        _LuaWorld.os["exit"] = (System.Action<int?>)(code => {
            throw new ScriptExitException();
        });

        _LuaWorld.LuaScript.Globals["os"] = _LuaWorld.os;
    }


    public static void CreateSafeIoTable(LuaWorld _LuaWorld) {
        _LuaWorld.io["open"] = (string path, string? mode) => {
            // Security: Validate file path with user approval if outside workspace
            if (!Security.EnsurePathAllowedWithPrompt(path)) {
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
                    _LuaWorld.fileHandle["read"] = (DynValue readMode) => {
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
                        } catch (Exception ex) {
                            Core.Diagnostics.luaInternalCatch("io.read failed with exception: " + ex);
                            return null;
                        }
                    };
                    // Implement file:seek() for binary file navigation
                    _LuaWorld.fileHandle["seek"] = (System.Func<string?, long?, long?>)((whence, offset) => {
                        try {
                            whence = whence ?? "cur";
                            offset = offset ?? 0;

                            System.IO.SeekOrigin origin = whence switch {
                                "set" => System.IO.SeekOrigin.Begin,
                                "end" => System.IO.SeekOrigin.End,
                                _ => System.IO.SeekOrigin.Current
                            };

                            return fs.Seek(offset.Value, origin);
                        } catch(Exception ex) {
                            Core.Diagnostics.luaInternalCatch("io.seek failed with exception: " + ex);
                            return null;
                        }
                    });
                    _LuaWorld.fileHandle["write"] = (string content) => {
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
                        } catch (Exception ex) {
                            Core.Diagnostics.luaInternalCatch("io.write failed with exception: " + ex);
                        }
                    };
                    _LuaWorld.fileHandle["close"] =() => {
                        try {
                            fs?.Dispose();
                        } catch (Exception ex) {
                            Core.Diagnostics.luaInternalCatch("io.close failed with exception: " + ex);
                        }
                    };
                    _LuaWorld.fileHandle["flush"] = () => {
                        try {
                            fs?.Flush();
                        } catch (Exception ex) {
                            Core.Diagnostics.luaInternalCatch("io.flush failed with exception: " + ex);
                        }
                    };
                    return DynValue.NewTable(_LuaWorld.fileHandle);
                }
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("io.open failed with exception: " + ex);
                return DynValue.Nil;
            }
            return DynValue.Nil;
        };

        _LuaWorld.io["write"] = (string content) => Core.UI.EngineSdk.Print(content);
        _LuaWorld.io["flush"] = DynValue.Nil; // removed for now, maybe add later as an event that can be optionally handled by active UI System
        _LuaWorld.io["read"] = DynValue.Nil; // removed for now,
        _LuaWorld.io["popen"] = DynValue.Nil; //  io.popen removed - use sdk.exec/run_process instead

        _LuaWorld.LuaScript.Globals["io"] = _LuaWorld.io;
    }
    // :: end helpers for SetupSafeLuaEnvironment()
    //
    //

}
