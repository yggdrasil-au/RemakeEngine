#pragma warning disable CS0162 // Unreachable code detected // IsTraceEnabled block is reachable however ide analyzers tend to treat it as always true

namespace EngineNet.Shared.IO;

using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;

// Diagnostic logging utility
[SuppressMessage(category: "Compiler", checkId: "CS0162:Unreachable code detected")]
[SuppressMessage(category: "ReSharper", checkId: "HeuristicUnreachableCode")] // caused by the IsTraceEnabled compile-time constant
public static class Diagnostics {
    private static string _rootPath = string.Empty;

    // assign trace enabled as a compile-time constant to allow dead code elimination of trace logging in
    private const bool IsTraceEnabled =
    #if DEBUG
        true;
    #else
        false;
    #endif

    // Trace writer only exists in Debug builds
    private static StreamWriter? _traceWriter; // Master log (Everything)

    // These writers exist in all builds so logging always works
    private static StreamWriter? _debugWriter; // Just Shared.IO.Diagnostics.Log / Info
    private static StreamWriter? _luaLogWriter; // Just Shared.IO.Diagnostics.LuaLog
    private static StreamWriter? _jsLogWriter; // Just Shared.IO.Diagnostics.JsLog
    private static StreamWriter? _pythonLogWriter; // Just Shared.IO.Diagnostics.PythonLog
    private static StreamWriter? _bugWriter; // Just Shared.IO.Diagnostics.Bug
    private static StreamWriter? _tuiLogWriter; // Scrollback history
    private static readonly object _lock = new object();

    internal static void Initialize(string rootPath, bool isGui, bool isTui) {
        _rootPath = string.IsNullOrWhiteSpace(rootPath) ? System.IO.Directory.GetCurrentDirectory() : rootPath;

        string logDirectory = string.Empty;
        try {
            string logDir = System.IO.Path.Combine(path1: _rootPath, path2: "logs");
            if (isGui) {
                logDir = System.IO.Path.Combine(path1: logDir, path2: "gui");
            } else if (isTui) {
                logDir = System.IO.Path.Combine(path1: logDir, path2: "tui");
            } else {
                logDir = System.IO.Path.Combine(path1: logDir, path2: "cli");
            }

            // 1. Cleanup old logs (keep last 24 hours)
            CleanLogDirectory(logDir: logDir, retentionHours: 24);

            // 2. Create the new log subdirectory for this session
            logDirectory = CreateSessionLogDirectory(logDir: logDir);

            // 3. Define Paths
            string debugPath = System.IO.Path.Combine(path1: logDirectory, path2: "debug.log");
            string luaLogPath = System.IO.Path.Combine(path1: logDirectory, path2: "lua.log");
            string jsLogPath = System.IO.Path.Combine(path1: logDirectory, path2: "js.log");
            string pythonLogPath = System.IO.Path.Combine(path1: logDirectory, path2: "python.log");
            string bugPath = System.IO.Path.Combine(path1: logDirectory, path2: "exception.log");

            // 4. Open Streams (Shared access allowed)
            // Trace Writer (Master) - Debug builds only
            if (IsTraceEnabled) {
                FileStream fsTrace = new FileStream(path: System.IO.Path.Combine(path1: logDirectory, path2: "trace.log"), mode: FileMode.Append,
                    access: FileAccess.Write, share: FileShare.ReadWrite);
                _traceWriter = new StreamWriter(stream: fsTrace) { AutoFlush = true };
            }

            // Debug Writer (all builds)
            FileStream fsDebug = new FileStream(path: debugPath, mode: FileMode.Append, access: FileAccess.Write, share: FileShare.ReadWrite);
            _debugWriter = new StreamWriter(stream: fsDebug) { AutoFlush = true };

            // Lua Log Writer (all builds)
            FileStream fsLuaLog = new FileStream(path: luaLogPath, mode: FileMode.Append, access: FileAccess.Write, share: FileShare.ReadWrite);
            _luaLogWriter = new StreamWriter(stream: fsLuaLog) { AutoFlush = true };

            // JS Log Writer (all builds)
            FileStream fsJsLog = new FileStream(path: jsLogPath, mode: FileMode.Append, access: FileAccess.Write, share: FileShare.ReadWrite);
            _jsLogWriter = new StreamWriter(stream: fsJsLog) { AutoFlush = true };

            // Python Log Writer (all builds)
            FileStream fsPythonLog = new FileStream(path: pythonLogPath, mode: FileMode.Append, access: FileAccess.Write, share: FileShare.ReadWrite);
            _pythonLogWriter = new StreamWriter(stream: fsPythonLog) { AutoFlush = true };

            // Bug Writer (all builds)
            FileStream fsBug = new FileStream(path: bugPath, mode: FileMode.Append, access: FileAccess.Write, share: FileShare.ReadWrite);
            _bugWriter = new StreamWriter(stream: fsBug) { AutoFlush = true };

        #if DEBUG
            if (isTui) {
                FileStream fsTui = new FileStream(path: System.IO.Path.Combine(path1: logDirectory, path2: "tui_history.log"), mode: FileMode.Append,
                    access: FileAccess.Write, share: FileShare.ReadWrite);
                _tuiLogWriter = new StreamWriter(stream: fsTui) { AutoFlush = true };
            }
        #endif

            // 5. Hook System.Diagnostics.Trace to the Master Trace Log (Debug only)
            // This ensures public .NET traces go to trace.log when debugging
            if (IsTraceEnabled) {
                System.Diagnostics.Trace.Listeners.Add(listener: new TextWriterTraceListener(writer: _traceWriter));
                System.Diagnostics.Trace.AutoFlush = true;
            }

            Log($"[System] Logging initialized at {DateTime.Now}");
        }
        catch (Exception ex) {
            Bug("[Diagnostics::Initialize()] Failed to initialize logging subsystem.", ex: ex);
            Console.Error.WriteLine($"CRITICAL: Failed to init loggers. {ex.Message}");
            Console.WriteLine(logDirectory);
        }
    }

    /// <summary>
    /// Ensures the log directory exists and removes subdirectories older than the retention window.
    /// Uses LastWriteTime to avoid NTFS tunneling edge cases with CreationTime.
    /// </summary>
    /// <param name="logDir"></param>
    /// <param name="retentionHours"></param>
    private static void CleanLogDirectory(string logDir, int retentionHours) {
        if (!Directory.Exists(path: logDir)) {
            Directory.CreateDirectory(path: logDir);
            return;
        }

        try {
            DateTime threshold = DateTime.Now.AddHours(-retentionHours);
            DirectoryInfo directoryInfo = new DirectoryInfo(path: logDir);

            foreach (DirectoryInfo subDir in directoryInfo.GetDirectories()) {
                if (subDir.LastWriteTime >= threshold) continue;
                try {
                    subDir.Delete(recursive: true);
                } catch (System.IO.IOException ex) {
                    Bug($"[Diagnostics::CleanLogDirectory()] Failed to delete log folder '{subDir.FullName}'.", ex: ex);
                    // Folder might be locked by another process; skip it for now.
                } catch (System.UnauthorizedAccessException ex) {
                    Bug($"[Diagnostics::CleanLogDirectory()] Access denied deleting log folder '{subDir.FullName}'.",
                        ex: ex);
                    // Folder might be locked by another process; skip it for now.
                }
            }
        }
        catch (System.IO.IOException ex) {
            Bug($"[Diagnostics::CleanLogDirectory()] IO error while cleaning '{logDir}'.", ex: ex);
            // continue
        }
        catch (System.UnauthorizedAccessException ex) {
            Bug($"[Diagnostics::CleanLogDirectory()] Access denied while cleaning '{logDir}'.", ex: ex);
            // continue
        }
    }

    /// <summary>
    /// Creates a session log directory with collision handling and returns its path.
    /// </summary>
    /// <param name="logDir"></param>
    private static string CreateSessionLogDirectory(string logDir) {
        string logSubdir = DateTime.Now.ToString(format: "dd-MM-HH-mm");
        string logDirectory = System.IO.Path.Combine(path1: logDir, path2: logSubdir);

        if (!Directory.Exists(path: logDirectory)) {
            Directory.CreateDirectory(path: logDirectory);
            return logDirectory;
        }

        string collisionSuffix = DateTime.Now.ToString(format: "ss");
        string collisionDirectory = System.IO.Path.Join(path1: logDir, path2: $"{logSubdir}_{collisionSuffix}");
        if (!Directory.Exists(path: collisionDirectory)) {
            Directory.CreateDirectory(path: collisionDirectory);
            return collisionDirectory;
        }

        string fallbackDirectory = System.IO.Path.Combine(path1: logDir, path2: $"{logSubdir}_{collisionSuffix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path: fallbackDirectory);
        return fallbackDirectory;
    }

    /// <summary>
    /// Helper to write to trace log if enabled
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(methodImplOptions: System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void WriteTrace(string message, string? stack = null) {
        if (!IsTraceEnabled || _traceWriter == null) {
            return;
        }

        _traceWriter.WriteLine(message);
        if (stack != null) {
            _traceWriter.WriteLine(stack);
        }
    }

    /// <summary>
    /// Logs a message to the TUI scrollback history file.
    /// Only active in TUI mode and Debug builds.
    /// </summary>
    /// <param name="message"></param>
    public static void TuiLog(string message) {
    #if DEBUG
        if (_tuiLogWriter == null) return;
        lock (_lock) {
            string timestamp = DateTime.Now.ToString(format: "HH:mm:ss");
            // Strip out newlines for clean logging
            string cleanMsg = message.Replace(oldValue: "\r", newValue: "").Replace(oldValue: "\n", newValue: " ");
            _tuiLogWriter.WriteLine($"[{timestamp}] {cleanMsg}");
        }
    #endif
    }

    /// <summary>
    /// Log a trace message to trace.log, only in Debug builds, use anywhere for excessively verbose tracing
    /// </summary>
    public static void Trace(string message, [CallerFilePath] string filePath = "",
        [CallerMemberName] string memberName = "") {
        if (!IsTraceEnabled || _traceWriter == null) {
            return;
        }

        lock (_lock) {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string className = System.IO.Path.GetFileNameWithoutExtension(filePath);
            string formattedMsg = $"[{timestamp}] [TRACE] [{filePath}::{className}::{memberName}] {message}";
            // Write to trace.log in Debug builds
            _traceWriter.WriteLine(formattedMsg);
        }
    }

    /// <summary>
    /// Logs an informational message to debug.log and trace.log
    /// </summary>
    public static void Info(string message, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "") {
        if (_debugWriter == null) return;

        lock (_lock) {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string className = System.IO.Path.GetFileNameWithoutExtension(filePath);
            string formattedMsg = $"[{timestamp}] [INFO] [{filePath}::{className}::{memberName}] {message}";

            // 1. Write to specific debug.log
            _debugWriter.WriteLine(formattedMsg);

            // 2. Write to master trace.log in Debug builds
            WriteTrace(formattedMsg);
        }
    }

    /// <summary>
    /// Logs a general log message to debug.log and trace.log
    /// </summary>
    public static void Log(string message, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "") {
        if (_debugWriter == null) return;

        lock (_lock) {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string className = System.IO.Path.GetFileNameWithoutExtension(filePath);
            string formattedMsg = $"[{timestamp}] [Log] [{filePath}::{className}::{memberName}] {message}";

            // 1. Write to specific debug.log
            _debugWriter.WriteLine(formattedMsg);

            // 2. Write to master trace.log in Debug builds
            WriteTrace(formattedMsg);
        }
    }

    /// <summary>
    /// Logs a bug message and optional exception to exception.log and trace.log
    /// </summary>
    public static void Bug(string message, Exception? ex = null, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "") {
        if (_bugWriter == null) return;

        lock (_lock) {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string className = System.IO.Path.GetFileNameWithoutExtension(filePath);

            // Format the header
            string header = $"[{timestamp}] [BUG] [{filePath}::{className}::{memberName}] {message}";
            string? stack = ex != null ? $"[{timestamp}] [STACK] {ex}" : null;

            // 1. Write to specific exception.log
            _bugWriter.WriteLine(header);
            if (stack != null) _bugWriter.WriteLine(stack);

            // 2. Write to master trace.log in Debug builds
            WriteTrace(header, stack);
        }
    }

    /// <summary>
    /// Like the Bug method but specifically for logging exceptions from C# invoked by Lua scripts.
    /// e.g. when a Lua script calls a C# function that throws an exception, this method can be used to log that exception from C# into lua.log and trace.log.
    /// not to be used inside a OperationCanceledException catch block, use Trace or Log instead.
    /// </summary>
    /// <param name="ex"></param>
    public static void LuaInternalCatch(string ex) {
        if (Diagnostics._bugWriter == null) return;

        lock (Diagnostics._lock) {
            string timestamp = DateTime.Now.ToString(format: "HH:mm:ss");

            // Format the header
            string header = $"[{timestamp}] [LUA_BUG] Caught exception from Lua-invoked C# code.";
            string stack = $"[{timestamp}] [STACK] {ex}";

            // 1. Write to specific exception.log
            Shared.IO.Diagnostics._bugWriter.WriteLine(header);
            Shared.IO.Diagnostics._bugWriter.WriteLine(stack);

            // 2. Write to master trace.log in Debug builds
            WriteTrace(header, stack: stack);
        }
    }

    /// <summary>
    /// Like the Bug method but specifically for logging exceptions from C# invoked by JS scripts.
    /// e.g. when a JS script calls a C# function that throws an exception, this method can be used to log that exception from C# into js.log and trace.log.
    /// </summary>
    /// <param name="ex"></param>
    public static void JsInternalCatch(string ex) {
        if (Diagnostics._bugWriter == null) return;

        lock (Diagnostics._lock) {
            string timestamp = DateTime.Now.ToString(format: "HH:mm:ss");

            // Format the header
            string header = $"[{timestamp}] [JS_BUG] Caught exception from JS-invoked C# code.";
            string stack = $"[{timestamp}] [STACK] {ex}";

            // 1. Write to specific exception.log
            Shared.IO.Diagnostics._bugWriter.WriteLine(header);
            Shared.IO.Diagnostics._bugWriter.WriteLine(stack);

            // 2. Write to master trace.log in Debug builds
            WriteTrace(header, stack: stack);
        }
    }


    internal static void Close() {
        _debugWriter?.Close();
        _bugWriter?.Close();
        _luaLogWriter?.Close();
        _jsLogWriter?.Close();

        if (!IsTraceEnabled) return;
        _traceWriter?.Close();
        System.Diagnostics.Trace.Listeners.Clear();
    }


    // mirrors Diagnostics but for logging via Lua scripts
    // find todo in LuaScriptAction.cs for next steps
    public static class LuaLogger {
        /// <summary>
        /// Like the Log method but specifically for logging messages from Lua scripts, using the moonsharp Global Shared.IO.Diagnostics.Log function.
        /// </summary>
        /// <param name="message"></param>
        public static void LuaLog(string message) {
            if (Diagnostics._luaLogWriter == null) return;

            lock (Diagnostics._lock) {
                string timestamp = DateTime.Now.ToString(format: "HH:mm:ss");
                string formattedMsg = $"[{timestamp}] [LUA_LOG] {message}";

                // 1. Write to specific lua.log
                Shared.IO.Diagnostics._luaLogWriter.WriteLine(formattedMsg);

                // 2. Write to master trace.log in Debug builds
                WriteTrace(formattedMsg);
            }
        }

        /// <summary>
        /// trace into main trace log from Lua scripts, only in Debug builds, use lua.log in release builds
        /// </summary>
        /// <param name="message"></param>
        public static void LuaTrace(string message) {
            if (IsTraceEnabled) {
                if (Diagnostics._traceWriter == null) return;

                lock (Diagnostics._lock) {
                    string timestamp = DateTime.Now.ToString(format: "HH:mm:ss");
                    string formattedMsg = $"[{timestamp}] [LUA_TRACE] {message}";
                    // Write to trace.log in Debug builds
                    Shared.IO.Diagnostics._traceWriter.WriteLine(formattedMsg);
                }
            } else {
                // use lua.log
                LuaLog(message);
            }
        }
    }

    // JS Logger, exactly like LuaLogger but for JS scripts
    public static class JsLogger {
        /// <summary>
        /// Like the Log method but specifically for logging messages from JS scripts, using the Global Shared.IO.Diagnostics.Log function.
        /// </summary>
        /// <param name="message"></param>
        public static void JsLog(string message) {
            if (Diagnostics._jsLogWriter == null) return;

            lock (Diagnostics._lock) {
                string timestamp = DateTime.Now.ToString(format: "HH:mm:ss");
                string formattedMsg = $"[{timestamp}] [JS_LOG] {message}";

                // 1. Write to specific js.log
                Shared.IO.Diagnostics._jsLogWriter.WriteLine(formattedMsg);

                // 2. Write to master trace.log in Debug builds
                WriteTrace(formattedMsg);
            }
        }

        public static void JsTrace(string message) {
            if (IsTraceEnabled) {
                if (Diagnostics._traceWriter == null) return;

                lock (Diagnostics._lock) {
                    string timestamp = DateTime.Now.ToString(format: "HH:mm:ss");
                    string formattedMsg = $"[{timestamp}] [JS_TRACE] {message}";
                    // Write to trace.log in Debug builds
                    Shared.IO.Diagnostics._traceWriter.WriteLine(formattedMsg);
                }
            } else {
                // use js.log
                JsLog(message);
            }
        }
    }

    // Python Logger, exactly like LuaLogger but for Python scripts
    public static class PythonLogger {
        /// <summary>
        /// Like the Log method but specifically for logging messages from Python scripts, using the Global Shared.IO.Diagnostics.Log function.
        /// </summary>
        /// <param name="message"></param>
        public static void PythonLog(string message) {
            if (Diagnostics._pythonLogWriter == null) return;

            lock (Diagnostics._lock) {
                string timestamp = DateTime.Now.ToString(format: "HH:mm:ss");
                string formattedMsg = $"[{timestamp}] [PYTHON_LOG] {message}";

                // 1. Write to specific python.log
                Shared.IO.Diagnostics._pythonLogWriter.WriteLine(formattedMsg);

                // 2. Write to master trace.log in Debug builds
                WriteTrace(formattedMsg);
            }
        }

        public static void PythonTrace(string message) {
            if (IsTraceEnabled) {
                if (Diagnostics._traceWriter == null) return;

                lock (Diagnostics._lock) {
                    string timestamp = DateTime.Now.ToString(format: "HH:mm:ss");
                    string formattedMsg = $"[{timestamp}] [PYTHON_TRACE] {message}";
                    // Write to trace.log in Debug builds
                    Shared.IO.Diagnostics._traceWriter.WriteLine(formattedMsg);
                }
            } else {
                // use python.log
                PythonLog(message);
            }
        }
    }
}