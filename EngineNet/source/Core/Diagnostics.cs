using System;
using System.IO;
using System.Diagnostics;

namespace EngineNet.Core;


// Diagnostic logging utility
internal static class Diagnostics {

    // Trace writer only exists in Debug builds
#if DEBUG
    private static StreamWriter? _traceWriter; // Master log (Everything)
#endif

    // These writers exist in all builds so logging always works
    private static StreamWriter? _debugWriter; // Just Diagnostics.Log / Info
    private static StreamWriter? _luaLogWriter; // Just Diagnostics.LuaLog
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
            string bugPath = Path.Combine(logDirectory, "exception.log");

            // 3. Open Streams (Shared access allowed)
            // Trace Writer (Master) - Debug builds only
#if DEBUG
            var fsTrace = new FileStream(tracePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _traceWriter = new StreamWriter(fsTrace) { AutoFlush = true };
#endif

            // Debug Writer (all builds)
            var fsDebug = new FileStream(debugPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _debugWriter = new StreamWriter(fsDebug) { AutoFlush = true };

            // Lua Log Writer (all builds)
            var fsLuaLog = new FileStream(luaLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _luaLogWriter = new StreamWriter(fsLuaLog) { AutoFlush = true };

            // Bug Writer (all builds)
            var fsBug = new FileStream(bugPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _bugWriter = new StreamWriter(fsBug) { AutoFlush = true };

            // 4. Hook System.Diagnostics.Trace to the Master Trace Log (Debug only)
            // This ensures internal .NET traces go to trace.log when debugging
#if DEBUG
            System.Diagnostics.Trace.Listeners.Add(new TextWriterTraceListener(_traceWriter));
            System.Diagnostics.Trace.AutoFlush = true;
#endif

            Log($"[System] Logging initialized at {DateTime.Now}");

        } catch (Exception ex) {
            Console.Error.WriteLine($"CRITICAL: Failed to init loggers. {ex.Message}");
            Console.WriteLine(logDirectory);
        }
    }

    /// <summary>
    /// Log a trace message to trace.log, only in Debug builds, use anywhere for excessively verbose tracing
    /// </summary>
    /// <param name="message"></param>
    internal static void Trace(string message) {
#if DEBUG
        if (_traceWriter == null) return;

        lock (_lock) {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string formattedMsg = $"[{timestamp}] [TRACE] {message}";
            // Write to trace.log in Debug builds
            _traceWriter.WriteLine(formattedMsg);
        }
#endif
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

#if DEBUG
            // 2. Write to master trace.log in Debug builds
            if (_traceWriter != null) {
                _traceWriter.WriteLine(formattedMsg);
            }
#endif
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

#if DEBUG
            // 2. Write to master trace.log in Debug builds
            if (_traceWriter != null) {
                _traceWriter.WriteLine(formattedMsg);
            }
#endif
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

            // 1. Write to specific exception.log
            _bugWriter.WriteLine(header);
            if (ex != null) _bugWriter.WriteLine($"[{timestamp}] [STACK] {ex}");

#if DEBUG
            // 2. Write to master trace.log in Debug builds
            if (_traceWriter != null) {
                _traceWriter.WriteLine(header);
                if (ex != null) _traceWriter.WriteLine($"[{timestamp}] [STACK] {ex}");
            }
#endif
        }
    }

    internal static void Close() {
        _debugWriter?.Close();
        _bugWriter?.Close();
        _luaLogWriter?.Close();
#if DEBUG
        _traceWriter?.Close();
        System.Diagnostics.Trace.Listeners.Clear();
#endif
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

#if DEBUG
                // 2. Write to master trace.log in Debug builds
                if (Diagnostics._traceWriter != null) {
                    Diagnostics._traceWriter.WriteLine(formattedMsg);
                }
#endif
            }
        }

        /// <summary>
        /// trace into main trace log from Lua scripts, only in Debug builds, use lua.log in release builds
        /// </summary>
        /// <param name="message"></param>
        internal static void LuaTrace(string message) {
#if DEBUG
            if (Diagnostics._traceWriter == null) return;

            lock (Diagnostics._lock) {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string formattedMsg = $"[{timestamp}] [LUA_TRACE] {message}";
                // Write to trace.log in Debug builds
                Diagnostics._traceWriter.WriteLine(formattedMsg);
            }
#else
            // use lua.log
            LuaLog(message);
#endif
        }
    }

}