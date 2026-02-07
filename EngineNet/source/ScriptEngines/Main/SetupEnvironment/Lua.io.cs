using System.Text;
using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua;

public static partial class SetupEnvironment {
    public static void CreateIoTable(LuaWorld _LuaWorld) {
        _LuaWorld.io["open"] = (string path, string? mode) => {
            // Security: Validate file path with user approval if outside workspace
            if (!Security.EnsurePathAllowedWithPrompt(path)) {
                //return DynValue.Nil;
                return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("Access denied to path: " + path));
            }

            System.IO.FileStream? fs = null;
            bool registered = false;
            try {
                mode = mode ?? "r";
                bool binaryMode = mode.Contains("b");
                if (mode.Contains("r")) {
                    fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                } else if (mode.Contains("w")) {
                    fs = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write);
                } else if (mode.Contains("a")) {
                    fs = new System.IO.FileStream(path, System.IO.FileMode.Append, System.IO.FileAccess.Write);
                }

                if (fs != null) {
                    _LuaWorld.RegisterDisposable(fs);
                    registered = true;
                    // Create a per-open handle table so concurrent files do not share state.
                    Table InstanceHandle = new Table(_LuaWorld.LuaScript);
                    // Implement file:read() with support for both text and binary modes
                    InstanceHandle["read"] = (DynValue readMode) => {
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
                    InstanceHandle["seek"] = (System.Func<string?, long?, long?>)((whence, offset) => {
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
                    InstanceHandle["write"] = (string content) => {
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
                    InstanceHandle["close"] =() => {
                        try {
                            if (fs != null) {
                                _LuaWorld.UnregisterDisposable(fs);
                                fs.Dispose();
                            }
                        } catch (Exception ex) {
                            Core.Diagnostics.luaInternalCatch("io.close failed with exception: " + ex);
                        }
                    };
                    InstanceHandle["flush"] = () => {
                        try {
                            fs?.Flush();
                        } catch (Exception ex) {
                            Core.Diagnostics.luaInternalCatch("io.flush failed with exception: " + ex);
                        }
                    };
                    return DynValue.NewTable(InstanceHandle);
                }
                return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("io.open failed to open path: " + path));
            } catch (Exception ex) {
                if (fs != null && registered) {
                    try {
                        _LuaWorld.UnregisterDisposable(fs);
                        fs.Dispose();
                    } catch (Exception disposeEx) {
                        Core.Diagnostics.luaInternalCatch("io.open cleanup failed with exception: " + disposeEx);
                    }
                }
                Core.Diagnostics.luaInternalCatch("io.open failed with exception: " + ex);
                return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("io.open failed with exception: " + ex.Message));
            }
        };

        _LuaWorld.io["write"] = (string content) => Core.UI.EngineSdk.Print(content);

        _LuaWorld.io["flush"] = DynValue.Nil; // removed for now, maybe add later as an event that can be optionally handled by active UI System
        _LuaWorld.io["read"] = DynValue.Nil; // removed for now,
        _LuaWorld.io["popen"] = DynValue.Nil; //  io.popen removed - use sdk.exec/run_process instead

        // Expose the custom io table to the Lua environment
        _LuaWorld.LuaScript.Globals["io"] = _LuaWorld.io;
    }
}
