using System;
using System.IO;
using System.Diagnostics;

namespace EngineNet.Core;


// Diagnostic logging utility
internal static class Diagnostics {

    private static readonly bool IsTraceEnabled =
#if DEBUG
        true;
#else
        false;
#endif

    // Trace writer only exists in Debug builds
    private static StreamWriter? _traceWriter; // Master log (Everything)

    // These writers exist in all builds so logging always works
    private static StreamWriter? _debugWriter; // Just Diagnostics.Log / Info
    private static StreamWriter? _luaLogWriter; // Just Diagnostics.LuaLog
    private static StreamWriter? _jsLogWriter; // Just Diagnostics.JsLog
    private static StreamWriter? _pythonLogWriter; // Just Diagnostics.PythonLog
    private static StreamWriter? _bugWriter;   // Just Diagnostics.Bug
    private static readonly object _lock = new();

    internal static void Initialize(bool isGui, bool isTui) {
        string logDirectory = string.Empty;
        try {
            string logDir = Path.Combine(Program.rootPath, "logs");
            if (isGui) {
                logDir = Path.Combine(logDir, "gui");
            } else if (isTui) {
                logDir = Path.Combine(logDir, "tui");
            } else {
                logDir = Path.Combine(logDir, "cli");
            }

            string logsubdir = DateTime.Now.ToString("dd_HH-mm-ss");

            // delete logdir if exists else create it
            if (Directory.Exists(logDir)) {
                try {
                    Directory.Delete(logDir, true);
                } catch {
                    // if failed, logs may be in use by another process,
                }
            } else {
                Directory.CreateDirectory(logDir);
            }

            logDirectory = Path.Combine(logDir, logsubdir);

            // 1. Cleanup old logs
            try {
                // delete entire log directory on startup
                if (Directory.Exists(logDirectory)) {
                    Directory.Delete(logDirectory, true);
                } else {
                    Directory.CreateDirectory(logDirectory);
                }
            } catch {
                // if failed, logs may be in use by another process,
                // we need a new log directory
                string newLogDirectory = logDirectory + "_" + DateTime.Now.ToString("mmss");
                Directory.CreateDirectory(newLogDirectory);
                logDirectory = newLogDirectory;
            }

            // 2. Define Paths
            string tracePath = Path.Combine(logDirectory, "trace.log");
            string debugPath = Path.Combine(logDirectory, "debug.log");
            string luaLogPath = Path.Combine(logDirectory, "lua.log");
            string jsLogPath = Path.Combine(logDirectory, "js.log");
            string pythonLogPath = Path.Combine(logDirectory, "python.log");
            string bugPath = Path.Combine(logDirectory, "exception.log");

            // 3. Open Streams (Shared access allowed)
            // Trace Writer (Master) - Debug builds only
            if (IsTraceEnabled) {
                var fsTrace = new FileStream(tracePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _traceWriter = new StreamWriter(fsTrace) { AutoFlush = true };
            }

            // Debug Writer (all builds)
            var fsDebug = new FileStream(debugPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _debugWriter = new StreamWriter(fsDebug) { AutoFlush = true };

            // Lua Log Writer (all builds)
            var fsLuaLog = new FileStream(luaLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _luaLogWriter = new StreamWriter(fsLuaLog) { AutoFlush = true };

            // JS Log Writer (all builds)
            var fsJsLog = new FileStream(jsLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _jsLogWriter = new StreamWriter(fsJsLog) { AutoFlush = true };

            // Python Log Writer (all builds)
            var fsPythonLog = new FileStream(pythonLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _pythonLogWriter = new StreamWriter(fsPythonLog) { AutoFlush = true };

            // Bug Writer (all builds)
            var fsBug = new FileStream(bugPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _bugWriter = new StreamWriter(fsBug) { AutoFlush = true };

            // 4. Hook System.Diagnostics.Trace to the Master Trace Log (Debug only)
            // This ensures internal .NET traces go to trace.log when debugging
            if (IsTraceEnabled && _traceWriter != null) {
                System.Diagnostics.Trace.Listeners.Add(new TextWriterTraceListener(_traceWriter));
                System.Diagnostics.Trace.AutoFlush = true;
            }

            Log($"[System] Logging initialized at {DateTime.Now}");

        } catch (Exception ex) {
            Console.Error.WriteLine($"CRITICAL: Failed to init loggers. {ex.Message}");
            Console.WriteLine(logDirectory);
        }
    }

    /// <summary>
    /// Helper to write to trace log if enabled
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void WriteTraceInternal(string message, string? stack = null) {
        if (!IsTraceEnabled || _traceWriter == null) {
            return;
        }

        _traceWriter.WriteLine(message);
        if (stack != null) {
            _traceWriter.WriteLine(stack);
        }
    }

    /// <summary>
    /// Log a trace message to trace.log, only in Debug builds, use anywhere for excessively verbose tracing
    /// </summary>
    /// <param name="message"></param>
    internal static void Trace(string message) {
        if (!IsTraceEnabled || _traceWriter == null) {
            return;
        }

        lock (_lock) {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string formattedMsg = $"[{timestamp}] [TRACE] {message}";
            // Write to trace.log in Debug builds
            _traceWriter.WriteLine(formattedMsg);
        }
    }

    /// <summary>
    /// Logs an informational message to debug.log and trace.log
    /// </summary>
    /// <param name="message"></param>
    internal static void Info(string message) {
        if (_debugWriter == null) return;

        lock (_lock) {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string formattedMsg = $"[{timestamp}] [INFO] {message}";

            // 1. Write to specific debug.log
            _debugWriter.WriteLine(formattedMsg);

            // 2. Write to master trace.log in Debug builds
            WriteTraceInternal(formattedMsg);
        }

    }

    /// <summary>
    /// Logs a general log message to debug.log and trace.log
    /// </summary>
    /// <param name="message"></param>
    internal static void Log(string message) {
        if (_debugWriter == null) return;

        lock (_lock) {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string formattedMsg = $"[{timestamp}] [Log] {message}";

            // 1. Write to specific debug.log
            _debugWriter.WriteLine(formattedMsg);

            // 2. Write to master trace.log in Debug builds
            WriteTraceInternal(formattedMsg);
        }
    }

    /// <summary>
    /// Logs a bug message and optional exception to exception.log and trace.log
    /// Use within catch blocks, it doesnt need to be an actual bug just an exception
    /// </summary>
    /// <param name="message"></param>
    /// <param name="ex"></param>
    internal static void Bug(string message, Exception? ex = null) {
        if (_bugWriter == null) return;

        lock (_lock) {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");

            // Format the header
            string header = $"[{timestamp}] [BUG] {message}";
            string? stack = ex != null ? $"[{timestamp}] [STACK] {ex}" : null;

            // 1. Write to specific exception.log
            _bugWriter.WriteLine(header);
            if (stack != null) _bugWriter.WriteLine(stack);

            // 2. Write to master trace.log in Debug builds
            WriteTraceInternal(header, stack);
        }
    }

    /// <summary>
    /// Like the Bug method but specifically for logging exceptions from C# invoked by Lua scripts.
    /// eg when a Lua script calls a C# function that throws an exception, this method can be used to log that exception from C# into lua.log and trace.log.
    /// </summary>
    /// <param name="ex"></param>
    internal static void luaInternalCatch(string ex) {
        if (Diagnostics._bugWriter == null) return;

        lock (Diagnostics._lock) {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");

            // Format the header
            string header = $"[{timestamp}] [LUA_BUG] Caught exception from Lua-invoked C# code.";
            string stack = $"[{timestamp}] [STACK] {ex}";

            // 1. Write to specific exception.log
            Diagnostics._bugWriter.WriteLine(header);
            Diagnostics._bugWriter.WriteLine(stack);

            // 2. Write to master trace.log in Debug builds
            WriteTraceInternal(header, stack);
        }
    }

    /// <summary>
    /// Like the Bug method but specifically for logging exceptions from C# invoked by JS scripts.
    /// eg when a JS script calls a C# function that throws an exception, this method can be used to log that exception from C# into js.log and trace.log.
    /// </summary>
    /// <param name="ex"></param>
    internal static void JsInternalCatch(string ex) {
        if (Diagnostics._bugWriter == null) return;

        lock (Diagnostics._lock) {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");

            // Format the header
            string header = $"[{timestamp}] [JS_BUG] Caught exception from JS-invoked C# code.";
            string stack = $"[{timestamp}] [STACK] {ex}";

            // 1. Write to specific exception.log
            Diagnostics._bugWriter.WriteLine(header);
            Diagnostics._bugWriter.WriteLine(stack);

            // 2. Write to master trace.log in Debug builds
            WriteTraceInternal(header, stack);
        }
    }


    internal static void Close() {
        _debugWriter?.Close();
        _bugWriter?.Close();
        _luaLogWriter?.Close();
        _jsLogWriter?.Close();

        if (IsTraceEnabled) {
            _traceWriter?.Close();
            System.Diagnostics.Trace.Listeners.Clear();
        }
    }


    // mirrors Diagnostics but for loggin via Lua scripts
    // find todo in LuaScriptAction.cs for next steps
    internal static class LuaLogger {
        /// <summary>
        /// Like the Log method but specifically for logging messages from Lua scripts, using the moonsharp Global Diagnostics.Log function.
        /// </summary>
        /// <param name="message"></param>
        internal static void LuaLog(string message) {
            if (Diagnostics._luaLogWriter == null) return;

            lock (Diagnostics._lock) {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string formattedMsg = $"[{timestamp}] [LUA_LOG] {message}";

                // 1. Write to specific lua.log
                Diagnostics._luaLogWriter.WriteLine(formattedMsg);

                // 2. Write to master trace.log in Debug builds
                WriteTraceInternal(formattedMsg);
            }
        }

        /// <summary>
        /// trace into main trace log from Lua scripts, only in Debug builds, use lua.log in release builds
        /// </summary>
        /// <param name="message"></param>
        internal static void LuaTrace(string message) {
            if (IsTraceEnabled) {
                if (Diagnostics._traceWriter == null) return;

                lock (Diagnostics._lock) {
                    string timestamp = DateTime.Now.ToString("HH:mm:ss");
                    string formattedMsg = $"[{timestamp}] [LUA_TRACE] {message}";
                    // Write to trace.log in Debug builds
                    Diagnostics._traceWriter.WriteLine(formattedMsg);
                }
            } else {
                // use lua.log
                LuaLog(message);
            }
        }
    }

    // JS Logger, exactly like LuaLogger but for JS scripts
    internal static class JsLogger {
        /// <summary>
        /// Like the Log method but specifically for logging messages from JS scripts, using the Global Diagnostics.Log function.
        /// </summary>
        /// <param name="message"></param>
        internal static void JsLog(string message) {
            if (Diagnostics._jsLogWriter == null) return;

            lock (Diagnostics._lock) {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string formattedMsg = $"[{timestamp}] [JS_LOG] {message}";

                // 1. Write to specific js.log
                Diagnostics._jsLogWriter.WriteLine(formattedMsg);

                // 2. Write to master trace.log in Debug builds
                WriteTraceInternal(formattedMsg);
            }
        }

        internal static void JsTrace(string message) {
            if (IsTraceEnabled) {
                if (Diagnostics._traceWriter == null) return;

                lock (Diagnostics._lock) {
                    string timestamp = DateTime.Now.ToString("HH:mm:ss");
                    string formattedMsg = $"[{timestamp}] [JS_TRACE] {message}";
                    // Write to trace.log in Debug builds
                    Diagnostics._traceWriter.WriteLine(formattedMsg);
                }
            } else {
                // use js.log
                JsLog(message);
            }
        }
    }

    // Python Logger, exactly like LuaLogger but for Python scripts
    internal static class PythonLogger {
        /// <summary>
        /// Like the Log method but specifically for logging messages from Python scripts, using the Global Diagnostics.Log function.
        /// </summary>
        /// <param name="message"></param>
        internal static void PythonLog(string message) {
            if (Diagnostics._pythonLogWriter == null) return;

            lock (Diagnostics._lock) {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string formattedMsg = $"[{timestamp}] [PYTHON_LOG] {message}";

                // 1. Write to specific python.log
                Diagnostics._pythonLogWriter.WriteLine(formattedMsg);

                // 2. Write to master trace.log in Debug builds
                WriteTraceInternal(formattedMsg);
            }
        }

        internal static void PythonTrace(string message) {
            if (IsTraceEnabled) {
                if (Diagnostics._traceWriter == null) return;

                lock (Diagnostics._lock) {
                    string timestamp = DateTime.Now.ToString("HH:mm:ss");
                    string formattedMsg = $"[{timestamp}] [PYTHON_TRACE] {message}";
                    // Write to trace.log in Debug builds
                    Diagnostics._traceWriter.WriteLine(formattedMsg);
                }
            } else {
                // use python.log
                PythonLog(message);
            }
        }
    }
}


