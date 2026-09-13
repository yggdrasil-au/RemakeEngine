using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua;

internal static partial class SetupEnvironment {
    internal static void CreateIoTable(LuaWorld _LuaWorld) {
        _LuaWorld.Sdk.IO[key: "open"] = (string path, string? mode) => {
            // Security: Validate file path with user approval if outside workspace
            if (!Security.TryGetAllowedCanonicalPathWithPrompt(path: path, canonicalPath: out string safePath)) {
                //return DynValue.Nil;
                return DynValue.NewTuple(values: [DynValue.Nil, DynValue.NewString(str: "Access denied to path: " + path)]);
            }

            System.IO.FileStream? fs = null;
            bool registered = false;
            try {
                mode ??= "r";
                bool binaryMode = mode.Contains("b");
                if (mode.Contains('r')) {
                    fs = new System.IO.FileStream(path: safePath, mode: System.IO.FileMode.Open, access: System.IO.FileAccess.Read);
                } else if (mode.Contains('w')) {
                    fs = new System.IO.FileStream(path: safePath, mode: System.IO.FileMode.Create, access: System.IO.FileAccess.Write);
                } else if (mode.Contains('a')) {
                    fs = new System.IO.FileStream(path: safePath, mode: System.IO.FileMode.Append, access: System.IO.FileAccess.Write);
                }

                if (fs == null)
                    return DynValue.NewTuple(values: [DynValue.Nil, DynValue.NewString(str: "io.open failed to open path: " + safePath)]);

                FileStream activeStream = fs;

                _LuaWorld.RegisterDisposable(disposable: activeStream);
                registered = true;
                // Create a per-open handle table so concurrent files do not share state.
                Table InstanceHandle = new Table(owner: _LuaWorld.LuaScript);
                // Implement file:read() with support for both text and binary modes
                InstanceHandle[key: "read"] = (DynValue readMode) => {
                    try {
                        // Handle numeric argument: read N bytes (standard Lua behavior)
                        if (readMode.Type == DataType.Number) {
                            int count = (int)readMode.Number;
                            if (count <= 0) return string.Empty;

                            byte[] buffer = new byte[count];
                            int bytesRead = activeStream.Read(buffer: buffer, offset: 0, count: count);
                            if (bytesRead == 0) return null; // EOF

                            // Return as string with bytes preserved (Lua convention for binary data)
                            return System.Text.Encoding.Latin1.GetString(bytes: buffer, index: 0, count: bytesRead);
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
                                    int bytesRead = activeStream.Read(buffer: buffer, offset: 0, count: (int)remaining);
                                    return System.Text.Encoding.Latin1.GetString(bytes: buffer, index: 0, count: bytesRead);
                                } else {
                                    // Text mode: use StreamReader for proper text handling
                                    using StreamReader reader = new System.IO.StreamReader(stream: activeStream, leaveOpen: true);
                                    return reader.ReadToEnd();
                                }
                            }

                            case "*l":
                            case "*line": {
                                if (binaryMode) {
                                    // Read until newline in binary mode
                                    List<byte> lineBytes = new System.Collections.Generic.List<byte>();
                                    int b;
                                    while ((b = activeStream.ReadByte()) != -1) {
                                        if (b == '\n') break;
                                        if (b != '\r') lineBytes.Add(item: (byte)b);
                                    }

                                    return lineBytes.Count == 0 && b == -1 ? null : System.Text.Encoding.Latin1.GetString(bytes: lineBytes.ToArray());
                                } else {
                                    using StreamReader reader = new System.IO.StreamReader(stream: activeStream, leaveOpen: true);
                                    return reader.ReadLine();
                                }
                            }

                            default:
                                return null;
                        }
                    } catch (Exception ex) {
                        Shared.IO.Diagnostics.LuaInternalCatch(ex: "io.read failed with exception: " + ex);
                        return null;
                    }
                };
                // Implement file:seek() for binary file navigation
                InstanceHandle[key: "seek"] = (System.Func<string?, long?, long?>)((whence, offset) => {
                    try {
                        whence ??= "cur";
                        offset ??= 0;

                        System.IO.SeekOrigin origin = whence switch {
                            "set" => System.IO.SeekOrigin.Begin,
                            "end" => System.IO.SeekOrigin.End,
                            _ => System.IO.SeekOrigin.Current
                        };

                        return activeStream.Seek(offset: offset.Value, origin: origin);
                    } catch(Exception ex) {
                        Shared.IO.Diagnostics.LuaInternalCatch(ex: "io.seek failed with exception: " + ex);
                        return null;
                    }
                });
                InstanceHandle[key: "write"] = (string content) => {
                    try {
                        if (binaryMode) {
                            // Binary mode: write raw bytes
                            byte[] bytes = System.Text.Encoding.Latin1.GetBytes(s: content);
                            activeStream.Write(buffer: bytes, offset: 0, count: bytes.Length);
                            activeStream.Flush();
                        } else {
                            // Text mode: use StreamWriter
                            using System.IO.StreamWriter writer = new System.IO.StreamWriter(stream: activeStream, leaveOpen: true);
                            writer.Write(content);
                            writer.Flush();
                        }
                    } catch (Exception ex) {
                        Shared.IO.Diagnostics.LuaInternalCatch(ex: "io.write failed with exception: " + ex);
                    }
                };
                InstanceHandle[key: "close"] =() => {
                    try {
                        _LuaWorld.UnregisterDisposable(disposable: activeStream);
                        activeStream.Dispose();
                    } catch (Exception ex) {
                        Shared.IO.Diagnostics.LuaInternalCatch(ex: "io.close failed with exception: " + ex);
                    }
                };
                InstanceHandle[key: "flush"] = () => {
                    try {
                        activeStream.Flush();
                    } catch (Exception ex) {
                        Shared.IO.Diagnostics.LuaInternalCatch(ex: "io.flush failed with exception: " + ex);
                    }
                };
                return DynValue.NewTable(table: InstanceHandle);
            } catch (Exception ex) {
                if (fs != null && registered) {
                    try {
                        _LuaWorld.UnregisterDisposable(disposable: fs);
                        fs.Dispose();
                    } catch (Exception disposeEx) {
                        Shared.IO.Diagnostics.LuaInternalCatch(ex: "io.open cleanup failed with exception: " + disposeEx);
                    }
                }
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "io.open failed with exception: " + ex);
                return DynValue.NewTuple(values: [DynValue.Nil, DynValue.NewString(str: "io.open failed with exception: " + ex.Message)]);
            }
        };

        // simply print to output log for now
        _LuaWorld.Sdk.IO[key: "write"] = (string content) => Shared.IO.UI.EngineSdk.Print(content);

        _LuaWorld.Sdk.IO[key: "flush"] = DynValue.Nil; // removed for now, maybe add later as an event that can be optionally handled by active UI System
        _LuaWorld.Sdk.IO[key: "read"] = DynValue.Nil; // removed for now,
        _LuaWorld.Sdk.IO[key: "popen"] = DynValue.Nil; //  io.popen removed - use sdk.exec/run_process instead

        // Expose the custom io table to the Lua environment
        _LuaWorld.LuaScript.Globals[key: "io"] = _LuaWorld.Sdk.IO;
    }
}
