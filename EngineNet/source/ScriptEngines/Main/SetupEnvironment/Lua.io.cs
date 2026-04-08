using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua;

internal static partial class SetupEnvironment {
    internal static void CreateIoTable(LuaWorld _LuaWorld) {
        _LuaWorld.Sdk.IO["open"] = (string path, string? mode) => {
            // Security: Validate file path with user approval if outside workspace
            if (!Security.TryGetAllowedCanonicalPathWithPrompt(path, out string safePath)) {
                //return DynValue.Nil;
                return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("Access denied to path: " + path));
            }

            System.IO.FileStream? fs = null;
            bool registered = false;
            try {
                mode ??= "r";
                bool binaryMode = mode.Contains("b");
                if (mode.Contains('r')) {
                    fs = new System.IO.FileStream(safePath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                } else if (mode.Contains('w')) {
                    fs = new System.IO.FileStream(safePath, System.IO.FileMode.Create, System.IO.FileAccess.Write);
                } else if (mode.Contains('a')) {
                    fs = new System.IO.FileStream(safePath, System.IO.FileMode.Append, System.IO.FileAccess.Write);
                }

                if (fs == null)
                    return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("io.open failed to open path: " + safePath));

                var activeStream = fs;

                _LuaWorld.RegisterDisposable(activeStream);
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
                            int bytesRead = activeStream.Read(buffer, 0, count);
                            if (bytesRead == 0) return null; // EOF

                            // Return as string with bytes preserved (Lua convention for binary data)
                            return System.Text.Encoding.Latin1.GetString(buffer, 0, bytesRead);
                        }

                        // Handle string format specifiers
                        string? format = readMode.Type == DataType.String ? readMode.String : readMode.ToPrintString();

                        switch (format) {
                            case "*a":
                            case "*all": {
                                if (binaryMode) {
                                    // Binary mode: read operations return raw bytes as Latin1 strings
                                    long remaining = activeStream.Length - activeStream.Position;
                                    if (remaining == 0) return null;
                                    byte[] buffer = new byte[remaining];
                                    int bytesRead = activeStream.Read(buffer, 0, (int)remaining);
                                    return System.Text.Encoding.Latin1.GetString(buffer, 0, bytesRead);
                                } else {
                                    // Text mode: use StreamReader for proper text handling
                                    using var reader = new System.IO.StreamReader(activeStream, leaveOpen: true);
                                    return reader.ReadToEnd();
                                }
                            }

                            case "*l":
                            case "*line": {
                                if (binaryMode) {
                                    // Read until newline in binary mode
                                    var lineBytes = new System.Collections.Generic.List<byte>();
                                    int b;
                                    while ((b = activeStream.ReadByte()) != -1) {
                                        if (b == '\n') break;
                                        if (b != '\r') lineBytes.Add((byte)b);
                                    }

                                    return lineBytes.Count == 0 && b == -1 ? null : System.Text.Encoding.Latin1.GetString(lineBytes.ToArray());
                                } else {
                                    using var reader = new System.IO.StreamReader(activeStream, leaveOpen: true);
                                    return reader.ReadLine();
                                }
                            }

                            default:
                                return null;
                        }
                    } catch (Exception ex) {
                        Shared.IO.Diagnostics.LuaInternalCatch("io.read failed with exception: " + ex);
                        return null;
                    }
                };
                // Implement file:seek() for binary file navigation
                InstanceHandle["seek"] = (System.Func<string?, long?, long?>)((whence, offset) => {
                    try {
                        whence ??= "cur";
                        offset ??= 0;

                        System.IO.SeekOrigin origin = whence switch {
                            "set" => System.IO.SeekOrigin.Begin,
                            "end" => System.IO.SeekOrigin.End,
                            _ => System.IO.SeekOrigin.Current
                        };

                        return activeStream.Seek(offset.Value, origin);
                    } catch(Exception ex) {
                        Shared.IO.Diagnostics.LuaInternalCatch("io.seek failed with exception: " + ex);
                        return null;
                    }
                });
                InstanceHandle["write"] = (string content) => {
                    try {
                        if (binaryMode) {
                            // Binary mode: write raw bytes
                            byte[] bytes = System.Text.Encoding.Latin1.GetBytes(content);
                            activeStream.Write(bytes, 0, bytes.Length);
                            activeStream.Flush();
                        } else {
                            // Text mode: use StreamWriter
                            using System.IO.StreamWriter writer = new System.IO.StreamWriter(activeStream, leaveOpen: true);
                            writer.Write(content);
                            writer.Flush();
                        }
                    } catch (Exception ex) {
                        Shared.IO.Diagnostics.LuaInternalCatch("io.write failed with exception: " + ex);
                    }
                };
                InstanceHandle["close"] =() => {
                    try {
                        _LuaWorld.UnregisterDisposable(activeStream);
                        activeStream.Dispose();
                    } catch (Exception ex) {
                        Shared.IO.Diagnostics.LuaInternalCatch("io.close failed with exception: " + ex);
                    }
                };
                InstanceHandle["flush"] = () => {
                    try {
                        activeStream.Flush();
                    } catch (Exception ex) {
                        Shared.IO.Diagnostics.LuaInternalCatch("io.flush failed with exception: " + ex);
                    }
                };
                return DynValue.NewTable(InstanceHandle);
            } catch (Exception ex) {
                if (fs != null && registered) {
                    try {
                        _LuaWorld.UnregisterDisposable(fs);
                        fs.Dispose();
                    } catch (Exception disposeEx) {
                        Shared.IO.Diagnostics.LuaInternalCatch("io.open cleanup failed with exception: " + disposeEx);
                    }
                }
                Shared.IO.Diagnostics.LuaInternalCatch("io.open failed with exception: " + ex);
                return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("io.open failed with exception: " + ex.Message));
            }
        };

        _LuaWorld.Sdk.IO["write"] = (string content) => Shared.IO.UI.EngineSdk.Print(content);

        _LuaWorld.Sdk.IO["flush"] = DynValue.Nil; // removed for now, maybe add later as an event that can be optionally handled by active UI System
        _LuaWorld.Sdk.IO["read"] = DynValue.Nil; // removed for now,
        _LuaWorld.Sdk.IO["popen"] = DynValue.Nil; //  io.popen removed - use sdk.exec/run_process instead

        // Expose the custom io table to the Lua environment
        _LuaWorld.LuaScript.Globals["io"] = _LuaWorld.Sdk.IO;
    }
}
