// js interpreter
using Jint;
using Jint.Native;

using System.IO;
using System.Collections.Generic;
using EngineNet.Core.Utils;

namespace EngineNet.ScriptEngines;
/// <summary>
/// Executes a JavaScript file using the embedded Jint interpreter.
/// Exposes a host API that mirrors the Lua js_engine capabilities.
/// </summary>
internal sealed class JsScriptAction:EngineNet.ScriptEngines.Helpers.IAction {
    private readonly string _scriptPath;
    private readonly string[] _args;

    /// <summary>
    /// Creates a new action that runs the specified JavaScript file with no arguments.
    /// </summary>
    /// <param name="scriptPath">Absolute or relative path to the JavaScript file to execute.</param>
    //internal JsScriptAction(string scriptPath) : this(scriptPath, System.Array.Empty<string>()) { }

    /// <summary>
    /// Creates a new action that runs the specified JavaScript file with arguments.
    /// </summary>
    /// <param name="scriptPath">Absolute or relative path to the JavaScript file to execute.</param>
    /// <param name="args">Optional list of arguments to expose to the script via the global <c>argv</c> array.</param>
    internal JsScriptAction(string scriptPath, IEnumerable<string>? args) {
        _scriptPath = scriptPath;
        _args = args is null ? System.Array.Empty<string>() : args as string[] ?? new List<string>(args).ToArray();
    }

    /// <summary>
    /// Executes the JavaScript file in an embedded Jint js_engine.
    /// </summary>
    /// <param name="tools">Resolver used by the <c>tool(name)</c> global to locate external executables.</param>
    /// <param name="cancellationToken">Token used to cancel script execution.</param>
    /// <exception cref="FileNotFoundException">Thrown when the script file cannot be found.</exception>
    public async System.Threading.Tasks.Task ExecuteAsync(Core.Tools.IToolResolver tools, System.Threading.CancellationToken cancellationToken = default) {
        if (!System.IO.File.Exists(_scriptPath)) {
            throw new System.IO.FileNotFoundException("JavaScript file not found", _scriptPath);
        }

        string code = await System.IO.File.ReadAllTextAsync(_scriptPath, cancellationToken);
        Jint.Engine js_engine = new Jint.Engine(options => options.CancellationToken(cancellationToken));
        RegisterGlobals(js_engine, tools);
        PreloadShimModules(js_engine, _scriptPath);
#if DEBUG
        EngineSdk.PrintLine($"Running js script '{_scriptPath}' with {_args.Length} args...");
        EngineSdk.PrintLine($"input args: {string.Join(", ", _args)}");
#endif
        await System.Threading.Tasks.Task.Run(() => js_engine.Execute(code), cancellationToken);
    }

    /// <summary>
    /// Registers host functions and shims exposed to JavaScript, such as <c>emit</c>, <c>prompt</c>,
    /// <c>progress</c>, <c>sqlite</c>, and filesystem helpers.
    /// </summary>
    /// <param name="js_engine">The Jint js_engine instance.</param>
    /// <param name="tools">Tool resolver exposed via the <c>tool</c> global.</param>
    private void RegisterGlobals(Jint.Engine js_engine, Core.Tools.IToolResolver tools) {
        js_engine.SetValue("tool", new System.Func<string, string>(tools.ResolveToolPath));
        js_engine.SetValue("argv", _args);
        js_engine.SetValue("warn", new System.Action<string>(Core.Utils.EngineSdk.Warn));
        js_engine.SetValue("error", new System.Action<string>(Core.Utils.EngineSdk.Error));
        js_engine.SetValue("prompt", new System.Func<JsValue, JsValue, JsValue, string>((message, id, secret) => {
            string msg = message.IsString() ? message.AsString() : message.ToString();
            string promptId = id.IsNull() || id.IsUndefined() ? "q1" : id.IsString() ? id.AsString() : id.ToString();
            bool hide = secret.IsBoolean() && secret.AsBoolean();
            return Core.Utils.EngineSdk.Prompt(msg, promptId, hide);
        }));
        js_engine.SetValue("progress", new System.Func<JsValue, JsValue, JsValue, Core.Utils.EngineSdk.PanelProgress>((total, id, label) => {
            double totalNumber = JsInterop.ToNumber(total, 1);
            int totalSteps = (int)System.Math.Max(1, System.Math.Round(totalNumber));
            string progressId = id.IsNull() || id.IsUndefined() ? "p1" : id.IsString() ? id.AsString() : id.ToString();
            string? labelText = label.IsNull() || label.IsUndefined() ? null : label.IsString() ? label.AsString() : label.ToString();
            return new Core.Utils.EngineSdk.PanelProgress(totalSteps, progressId, labelText);
        }));
        js_engine.SetValue("sdk", new SdkModule(js_engine));
        js_engine.SetValue("sqlite", new SqliteModule(js_engine));
    }

    /// <summary>
    /// Preloads a minimal set of shim modules to emulate some Lua ecosystem pieces
    /// expected by existing module scripts (e.g. <c>lfs</c>, <c>dkjson</c>, <c>debug</c>).
    /// </summary>
    /// <param name="js_engine">The js_engine to extend.</param>
    /// <param name="scriptPath">Path of the currently executing script (used for debug info).</param>
    private static void PreloadShimModules(Jint.Engine js_engine, string scriptPath) {
        Dictionary<string, object?> modules = new Dictionary<string, object?>(System.StringComparer.Ordinal);
        LfsModule lfs = new LfsModule();
        DkJsonModule dkjson = new DkJsonModule(js_engine);
        DebugModule debug = new DebugModule(scriptPath);
        modules["lfs"] = lfs;
        modules["dkjson"] = dkjson;
        modules["debug"] = debug;
        js_engine.SetValue("debug", debug);
        js_engine.SetValue("package", new PackageModule(modules));
        js_engine.SetValue("require", new System.Func<string, object>(name => {
            return modules.TryGetValue(name, out object? module) && module != null
                ? module
                : throw JsInterop.CreateJsException(js_engine, "Error", $"module '{name}' not found (only preloaded modules available)");
        }));
    }
    private sealed class SdkModule {
        private readonly Jint.Engine js_engine;

        internal SdkModule(Jint.Engine js_engine) {
            this.js_engine = js_engine;
        }

        internal JsValue color_print(object? arg1, object? arg2 = null, object? arg3 = null) {
            string? color = null;
            string message = string.Empty;
            bool newline = true;
            JsValue first = JsInterop.AsJsValue(js_engine, arg1);
            if (first.IsObject() && !first.IsNull() && !first.IsUndefined() && !first.IsArray() && !first.IsString()) {
                IDictionary<string, object?> dict = JsInterop.ToDictionary(js_engine, first.AsObject());
                if (!dict.TryGetValue("color", out object? colorObj) && !dict.TryGetValue("colour", out colorObj)) {
                    colorObj = null;
                }

                if (colorObj != null) {
                    color = colorObj.ToString();
                }

                if (dict.TryGetValue("message", out object? msgObj) && msgObj != null) {
                    message = msgObj.ToString() ?? string.Empty;
                }

                if (dict.TryGetValue("newline", out object? newlineObj) && newlineObj is bool b) {
                    newline = b;
                }
            } else {
                if (!first.IsNull() && !first.IsUndefined()) {
                    color = JsInterop.ToString(first);
                }

                JsValue messageValue = JsInterop.AsJsValue(js_engine, arg2);
                if (!messageValue.IsNull() && !messageValue.IsUndefined()) {
                    message = JsInterop.ToString(messageValue);
                }

                JsValue newlineValue = JsInterop.AsJsValue(js_engine, arg3);
                if (newlineValue.IsBoolean()) {
                    newline = newlineValue.AsBoolean();
                }
            }
            Core.Utils.EngineSdk.Print(message, color, newline);
            return JsValue.Undefined;
        }

        internal JsValue colour_print(object? arg1, object? arg2 = null, object? arg3 = null) => color_print(arg1, arg2, arg3);

        internal bool validate_source_dir(string dir) {
            try {
                Helpers.ConfigHelpers.ValidateSourceDir(dir);
                return true;
            } catch {
                return false;
            }
        }

        internal bool copy_dir(string src, string dst, object? overwrite = null) {
            try {
                bool ow = JsInterop.ToBoolean(js_engine, overwrite);
                Helpers.ConfigHelpers.CopyDirectory(src, dst, ow);
                return true;
            } catch {
                return false;
            }
        }

        internal bool move_dir(string src, string dst, object? overwrite = null) {
            try {
                bool ow = JsInterop.ToBoolean(js_engine, overwrite);
                Helpers.ConfigHelpers.MoveDirectory(src, dst, ow);
                return true;
            } catch {
                return false;
            }
        }

        internal string? find_subdir(string baseDir, string name) => Helpers.ConfigHelpers.FindSubdir(baseDir, name);

        internal bool has_all_subdirs(string baseDir, object? names) {
            try {
                List<string> list = JsInterop.ToStringList(js_engine, names);
                return Helpers.ConfigHelpers.HasAllSubdirs(baseDir, list);
            } catch {
                return false;
            }
        }

        internal bool ensure_dir(string path) {
            try {
                System.IO.Directory.CreateDirectory(path);
                return true;
            } catch {
                return false;
            }
        }

        internal bool path_exists(string path) => FsUtils.PathExists(path);

        internal bool lexists(string path) => FsUtils.PathExistsIncludingLinks(path);

        internal bool is_dir(string path) => System.IO.Directory.Exists(path);

        internal bool is_file(string path) => System.IO.File.Exists(path);

        internal bool remove_dir(string path) {
            try {
                if (System.IO.Directory.Exists(path)) {
                    System.IO.Directory.Delete(path, true);
                }

                return true;
            } catch {
                return false;
            }
        }

        internal bool remove_file(string path) {
            try {
                if (FsUtils.IsSymlink(path) || System.IO.File.Exists(path)) {
                    System.IO.File.Delete(path);
                }

                return true;
            } catch {
                return false;
            }
        }

        internal bool copy_file(string src, string dst, object? overwrite = null) {
            try {
                bool ow = JsInterop.ToBoolean(js_engine, overwrite);
                System.IO.File.Copy(src, dst, ow);
                return true;
            } catch {
                return false;
            }
        }

        internal bool create_symlink(string source, string destination, bool isDirectory) => FsUtils.CreateSymlink(source, destination, isDirectory);

        internal bool is_symlink(string path) => FsUtils.IsSymlink(path);

        internal string? realpath(string path) => FsUtils.RealPath(path);

        internal string? readlink(string path) => FsUtils.ReadLink(path);

        internal void sleep(double seconds) {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) {
                return;
            }

            try {
                System.Threading.Thread.Sleep(System.TimeSpan.FromSeconds(seconds));
            } catch {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($"[JsScriptAction.cs] sleep interrupted");
#endif
                // ignore
            }
        }

        internal string md5(string text) {
            try {
                byte[] data = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty));
                return System.Convert.ToHexString(data).ToLowerInvariant();
            } catch {
                return string.Empty;
            }
        }

        internal JsValue run_process(object commandArgs, object? options = null) {
            Jint.Native.Array.ArrayInstance array = JsInterop.EnsureArray(js_engine, commandArgs, "run_process arguments");
            List<string> arguments = JsInterop.ToStringList(js_engine, array);
            if (arguments.Count == 0) {
                throw JsInterop.CreateJsException(js_engine, "TypeError", "run_process requires at least one argument (executable path)");
            }

            string fileName = arguments[0];
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            for (int i = 1; i < arguments.Count; i++) {
                psi.ArgumentList.Add(arguments[i]);
            }

            bool captureStdout = true;
            bool captureStderr = true;
            int? timeoutMs = null;
            Jint.Native.Object.ObjectInstance? opts = JsInterop.TryGetPlainObject(js_engine, options);
            if (opts != null) {
                JsValue cwdValue = JsInterop.Get(opts, js_engine, "cwd");
                if (cwdValue.IsString()) {
                    psi.WorkingDirectory = cwdValue.AsString();
                }

                JsValue stdoutValue = JsInterop.Get(opts, js_engine, "capture_stdout");
                if (!stdoutValue.IsUndefined()) {
                    captureStdout = JsInterop.ToBoolean(stdoutValue);
                }

                JsValue stderrValue = JsInterop.Get(opts, js_engine, "capture_stderr");
                if (!stderrValue.IsUndefined()) {
                    captureStderr = JsInterop.ToBoolean(stderrValue);
                }

                JsValue timeoutValue = JsInterop.Get(opts, js_engine, "timeout_ms");
                if (!timeoutValue.IsUndefined()) {
                    timeoutMs = JsInterop.ToNullableInt(timeoutValue);
                }

                JsValue envValue = JsInterop.Get(opts, js_engine, "env");
                if (!envValue.IsUndefined() && envValue.IsObject()) {
                    IDictionary<string, object?> dict = JsInterop.ToDictionary(js_engine, envValue.AsObject());
                    foreach (KeyValuePair<string, object?> kv in dict) {
                        if (kv.Value is string s) {
                            psi.Environment[kv.Key] = s;
                        }
                    }
                }
            }
            if (!captureStdout) {
                psi.RedirectStandardOutput = false;
            }

            if (!captureStderr) {
                psi.RedirectStandardError = false;
            }

            System.Text.StringBuilder stdoutBuilder = new System.Text.StringBuilder();
            System.Text.StringBuilder stderrBuilder = new System.Text.StringBuilder();
            using System.Diagnostics.Process process = new System.Diagnostics.Process();
            process.StartInfo = psi;
            if (captureStdout) {
                process.OutputDataReceived += (_, e) => { if (e.Data != null) { stdoutBuilder.AppendLine(e.Data); } };
            }

            if (captureStderr) {
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) { stderrBuilder.AppendLine(e.Data); } };
            }

            try {
                process.Start();
                if (captureStdout) {
                    process.BeginOutputReadLine();
                }

                if (captureStderr) {
                    process.BeginErrorReadLine();
                }

                if (timeoutMs.HasValue) {
                    if (!process.WaitForExit(timeoutMs.Value)) {
                        try {
                            process.Kill(entireProcessTree: true);
                        } catch {
#if DEBUG
                            System.Diagnostics.Trace.WriteLine($"[JsScriptAction.cs] Failed to kill timed-out process '{fileName}'");
#endif
                            // ignore
                        }
                        throw JsInterop.CreateJsException(js_engine, "Error", $"Process '{fileName}' timed out after {timeoutMs.Value} ms");
                    }
                } else {
                    process.WaitForExit();
                }
            } catch (System.Exception ex) {
                throw JsInterop.CreateJsException(js_engine, "Error", $"Failed to run process '{fileName}': {ex.Message}");
            }
            Dictionary<string, object?> result = new Dictionary<string, object?>(System.StringComparer.Ordinal) {
                ["exit_code"] = process.ExitCode,
                ["success"] = process.ExitCode == 0
            };
            if (captureStdout) {
                result["stdout"] = stdoutBuilder.ToString();
            }

            if (captureStderr) {
                result["stderr"] = stderrBuilder.ToString();
            }

            return JsInterop.FromObject(js_engine, result);
        }
    }
    private sealed class SqliteModule {
        private readonly Jint.Engine js_engine;

        internal SqliteModule(Jint.Engine js_engine) {
            this.js_engine = js_engine;
        }

        internal SqliteHandle open(string path) => new SqliteHandle(js_engine, path);
    }
    private sealed class SqliteHandle:System.IDisposable {
        private readonly Jint.Engine js_engine;
        private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
        private Microsoft.Data.Sqlite.SqliteTransaction? _transaction;
        private bool _disposed;

        public SqliteHandle(Jint.Engine js_engine, string path) {
            this.js_engine = js_engine;
            string fullPath = System.IO.Path.GetFullPath(path);
            Microsoft.Data.Sqlite.SqliteConnectionStringBuilder builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder {
                DataSource = fullPath
            };
            _connection = new Microsoft.Data.Sqlite.SqliteConnection(builder.ConnectionString);
            _connection.Open();
        }

        public int exec(string sql, object? parameters = null) {
            EnsureNotDisposed();
            using Microsoft.Data.Sqlite.SqliteCommand command = _connection.CreateCommand();
            command.CommandText = sql;
            if (_transaction != null) {
                command.Transaction = _transaction;
            }

            BindParameters(command, parameters);
            return command.ExecuteNonQuery();
        }

        public JsValue query(string sql, object? parameters = null) {
            EnsureNotDisposed();
            using Microsoft.Data.Sqlite.SqliteCommand command = _connection.CreateCommand();
            command.CommandText = sql;
            if (_transaction != null) {
                command.Transaction = _transaction;
            }

            BindParameters(command, parameters);
            using Microsoft.Data.Sqlite.SqliteDataReader reader = command.ExecuteReader();
            List<Dictionary<string, object?>> rows = new List<Dictionary<string, object?>>();
            while (reader.Read()) {
                Dictionary<string, object?> row = new Dictionary<string, object?>(System.StringComparer.Ordinal);
                for (int i = 0; i < reader.FieldCount; i++) {
                    string column = reader.GetName(i);
                    row[column] = ConvertValue(reader.GetValue(i));
                }
                rows.Add(row);
            }
            return JsInterop.FromObject(js_engine, rows);
        }

        public void begin() => BeginTransaction();

        public void BeginTransaction() {
            EnsureNotDisposed();
            _transaction ??= _connection.BeginTransaction();
        }

        public void commit() => Commit();

        public void Commit() {
            if (_disposed) {
                return;
            }

            if (_transaction != null) {
                _transaction.Commit();
                _transaction.Dispose();
                _transaction = null;
            }
        }

        public void rollback() => Rollback();

        public void Rollback() {
            if (_disposed) {
                return;
            }

            if (_transaction != null) {
                _transaction.Rollback();
                _transaction.Dispose();
                _transaction = null;
            }
        }

        public void close() => Dispose();

        public void dispose() => Dispose();

        public void Dispose() {
            if (_disposed) {
                return;
            }

            try {
                _transaction?.Dispose();
                _connection.Dispose();
            } finally {
                _transaction = null;
                _disposed = true;
            }
        }

        private void EnsureNotDisposed() {
            if (_disposed) {
                System.ObjectDisposedException.ThrowIf(_disposed, nameof(SqliteHandle));
            }
        }

        private void BindParameters(Microsoft.Data.Sqlite.SqliteCommand command, object? parameters) {
            if (parameters is null) {
                return;
            }

            IDictionary<string, object?> dict = JsInterop.ToDictionary(js_engine, parameters);
            foreach (KeyValuePair<string, object?> kv in dict) {
                Microsoft.Data.Sqlite.SqliteParameter parameter = command.CreateParameter();
                string name = kv.Key;
                if (!name.StartsWith(":", System.StringComparison.Ordinal) && !name.StartsWith("@", System.StringComparison.Ordinal) && !name.StartsWith("$", System.StringComparison.Ordinal)) {
                    name = ":" + name;
                }

                parameter.ParameterName = name;
                parameter.Value = kv.Value ?? System.DBNull.Value;
                command.Parameters.Add(parameter);
            }
        }

        private static object? ConvertValue(object? value) {
            return value is null || value is System.DBNull
                ? null
                : value switch {
                    System.DateTime dt => dt.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                    byte[] bytes => System.Convert.ToHexString(bytes),
                    _ => value
                };
        }
    }
    private sealed class LfsModule {

        internal string currentdir() => System.Environment.CurrentDirectory;

        internal object? mkdir(string path) {
            try {
                System.IO.Directory.CreateDirectory(path);
                return true;
            } catch {
                return null;
            }
        }

        internal object? attributes(string path) {
            if (System.IO.Directory.Exists(path)) {
                return new Dictionary<string, object?>(System.StringComparer.Ordinal) {
                    ["mode"] = "directory"
                };
            }
            if (System.IO.File.Exists(path)) {
                System.IO.FileInfo info = new System.IO.FileInfo(path);
                return new Dictionary<string, object?>(System.StringComparer.Ordinal) {
                    ["mode"] = "file",
                    ["size"] = info.Length,
                    ["modtime"] = info.LastWriteTimeUtc.ToString("o", System.Globalization.CultureInfo.InvariantCulture)
                };
            }
            return null;
        }

        internal System.Func<object?> dir(string path) {
            IEnumerable<string> Enumerate() {
                yield return ".";
                yield return "..";
                if (System.IO.Directory.Exists(path)) {
                    foreach (string entry in System.IO.Directory.EnumerateFileSystemEntries(path)) {
                        yield return System.IO.Path.GetFileName(entry);
                    }
                }
            }
            IEnumerator<string> enumerator = Enumerate().GetEnumerator();
            return () => enumerator.MoveNext() ? enumerator.Current : null;
        }
    }
    private sealed class DkJsonModule {
        private readonly Jint.Engine js_engine;

        internal DkJsonModule(Jint.Engine js_engine) {
            this.js_engine = js_engine;
        }

        internal string encode(JsValue value, JsValue options) {
            bool indent = false;
            if (!options.IsUndefined() && !options.IsNull() && options.IsObject()) {
                JsValue indentVal = JsInterop.Get(options.AsObject(), js_engine, "indent");
                if (!indentVal.IsUndefined()) {
                    indent = JsInterop.ToBoolean(indentVal);
                }
            }
            object? plain = JsInterop.ToPlainObject(js_engine, value);
            System.Text.Json.JsonSerializerOptions jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = indent };
            return System.Text.Json.JsonSerializer.Serialize(plain, jsonOptions);
        }

        internal JsValue encode(JsValue value) => encode(value, JsValue.Undefined);

        internal JsValue decode(string json) {
            try {
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
                return JsInterop.FromJsonElement(js_engine, doc.RootElement);
            } catch {
                return JsValue.Null;
            }
        }
    }
    private sealed class DebugModule {
        private readonly string _scriptPath;

        internal DebugModule(string scriptPath) {
            _scriptPath = scriptPath;
        }

        internal object getinfo(object? level = null, object? what = null) {
            return new Dictionary<string, object?>(System.StringComparer.Ordinal) {
                ["source"] = "@" + _scriptPath
            };
        }
    }
    private sealed class PackageModule {
        internal IDictionary<string, object?> loaded {
            get;
        }

        internal PackageModule(IDictionary<string, object?> modules) {
            loaded = modules;
        }
    }
    private static class FsUtils {

        internal static bool PathExists(string path) => System.IO.Directory.Exists(path) || System.IO.File.Exists(path);

        internal static bool PathExistsIncludingLinks(string path) {
            if (PathExists(path)) {
                return true;
            }

            try {
                System.IO.FileSystemInfo info = GetInfo(path);
                return info.Exists ? true : info.LinkTarget != null;
            } catch {
                return false;
            }
        }

        internal static bool IsSymlink(string path) {
            try {
                System.IO.FileSystemInfo info = GetInfo(path);
                return info.LinkTarget != null || info.Attributes.HasFlag(System.IO.FileAttributes.ReparsePoint);
            } catch {
                return false;
            }
        }

        internal static bool CreateSymlink(string source, string destination, bool isDirectory) {
            try {
                string destFull = System.IO.Path.GetFullPath(destination);
                string srcFull = System.IO.Path.GetFullPath(source);
                string? parent = System.IO.Path.GetDirectoryName(destFull);
                if (!string.IsNullOrEmpty(parent)) {
                    System.IO.Directory.CreateDirectory(parent);
                }

                if (isDirectory) {
                    System.IO.Directory.CreateSymbolicLink(destFull, srcFull);
                } else {
                    System.IO.File.CreateSymbolicLink(destFull, srcFull);
                }

                return true;
            } catch {
                return false;
            }
        }

        internal static string? RealPath(string path) {
            try {
                return System.IO.Path.GetFullPath(path);
            } catch {
                return null;
            }
        }

        internal static string? ReadLink(string path) {
            try {
                System.IO.FileSystemInfo info = GetInfo(path);
                return info.LinkTarget;
            } catch {
                return null;
            }
        }

        private static System.IO.FileSystemInfo GetInfo(string path) {
            string full = System.IO.Path.GetFullPath(path);
            System.IO.DirectoryInfo dirInfo = new System.IO.DirectoryInfo(full);
            if (dirInfo.Exists) {
                return dirInfo;
            }

            System.IO.FileInfo fileInfo = new System.IO.FileInfo(full);
            return fileInfo.Exists
                ? fileInfo
                : full.EndsWith(System.IO.Path.DirectorySeparatorChar) || full.EndsWith(System.IO.Path.AltDirectorySeparatorChar)
                ? new System.IO.DirectoryInfo(full.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))
                : fileInfo;
        }
    }
    private static class JsInterop {

        internal static JsValue AsJsValue(Jint.Engine js_engine, object? value) {
            return value is null ? JsValue.Null : value is JsValue js ? js : JsValue.FromObject(js_engine, value);
        }

        internal static bool ToBoolean(Jint.Engine js_engine, object? value, bool defaultValue = false) => ToBoolean(AsJsValue(js_engine, value), defaultValue);

        internal static bool ToBoolean(JsValue value, bool defaultValue = false) {
            return value.IsNull() || value.IsUndefined() ? defaultValue : Jint.Runtime.TypeConverter.ToBoolean(value);
        }

        internal static double ToNumber(JsValue value, double defaultValue = 0) {
            return value.IsNull() || value.IsUndefined() ? defaultValue : Jint.Runtime.TypeConverter.ToNumber(value);
        }

        internal static Jint.Native.Array.ArrayInstance EnsureArray(Jint.Engine js_engine, object? value, string paramName) {
            JsValue js = AsJsValue(js_engine, value);
            return !js.IsArray() ? throw CreateJsException(js_engine, "TypeError", $"{paramName} must be an array") : (Jint.Native.Array.ArrayInstance)js.AsArray();
        }

        internal static List<string> ToStringList(Jint.Engine js_engine, object? value) => ToStringList(EnsureArray(js_engine, value, "value"));

        internal static List<string> ToStringList(Jint.Native.Array.ArrayInstance array) {
            List<string> list = new List<string>();
            foreach (JsValue item in array) {
                if (item.IsNull() || item.IsUndefined()) {
                    break;
                }

                list.Add(ToString(item));
            }
            return list;
        }

        internal static IDictionary<string, object?> ToDictionary(Jint.Engine js_engine, object? value) {
            JsValue js = AsJsValue(js_engine, value);

            // Case 1: null or undefined => return empty dictionary
            if (js.IsNull() || js.IsUndefined()) {
                return new Dictionary<string, object?>(System.StringComparer.Ordinal);
            }

            // Case 2: not an object => throw
            if (!js.IsObject()) {
                throw CreateJsException(js_engine, constructorName: "TypeError", message: "Expected object value");
            }

            // Case 3: valid object => recurse
            return ToDictionary(js_engine, js.AsObject());
        }

        internal static IDictionary<string, object?> ToDictionary(Jint.Engine js_engine, Jint.Native.Object.ObjectInstance obj) {
            Dictionary<string, object?> dict = new Dictionary<string, object?>(System.StringComparer.Ordinal);
            foreach (JsValue key in obj.GetOwnPropertyKeys()) {
                JsValue value = obj.Get(key, obj);
                dict[key.IsString() ? key.AsString() : key.ToString()] = ToPlainObject(js_engine, value);
            }
            return dict;
        }

        internal static object? ToPlainObject(Jint.Engine js_engine, JsValue value) {
            if (value.IsNull() || value.IsUndefined()) {
                return null;
            }

            if (value.IsBoolean()) {
                return value.AsBoolean();
            }

            if (value.IsString()) {
                return value.AsString();
            }

            if (value.IsNumber()) {
                return value.AsNumber();
            }

            if (value.IsArray()) {
                List<object?> list = new List<object?>();
                foreach (JsValue item in value.AsArray()) {
                    list.Add(ToPlainObject(js_engine, item));
                }

                return list;
            }
            if (value.IsObject()) {
                Dictionary<string, object?> dict = new Dictionary<string, object?>(System.StringComparer.Ordinal);
                Jint.Native.Object.ObjectInstance obj = value.AsObject();
                foreach (JsValue key in obj.GetOwnPropertyKeys()) {
                    dict[key.IsString() ? key.AsString() : key.ToString()] = ToPlainObject(js_engine, obj.Get(key, obj));
                }

                return dict;
            }
            return value.ToString();
        }

        internal static Jint.Runtime.JavaScriptException CreateJsException(Jint.Engine js_engine, string constructorName, string message) {
            try {
                JsValue ctorValue = js_engine.GetValue(constructorName);
                JsValue errorInstance = js_engine.Invoke(ctorValue, message);
                return new Jint.Runtime.JavaScriptException(errorInstance);
            } catch {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($"[JsScriptAction.cs] Failed to create JS exception of type '{constructorName}', falling back to generic Error");
#endif
                // ignore
            }
            return new Jint.Runtime.JavaScriptException(JsValue.FromObject(js_engine, message));
        }

        internal static JsValue FromObject(Jint.Engine js_engine, object? value) => JsValue.FromObject(js_engine, value);

        internal static JsValue FromJsonElement(Jint.Engine js_engine, System.Text.Json.JsonElement element) => FromObject(js_engine, FromJsonElement(element));

        internal static object? FromJsonElement(System.Text.Json.JsonElement element) => element.ValueKind switch {
            System.Text.Json.JsonValueKind.Object => ToDictionary(element),
            System.Text.Json.JsonValueKind.Array => ToList(element),
            System.Text.Json.JsonValueKind.String => element.GetString(),
            System.Text.Json.JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.GetDouble(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            _ => null
        };

        internal static Jint.Native.Object.ObjectInstance? TryGetPlainObject(Jint.Engine js_engine, object? value) {
            JsValue js = AsJsValue(js_engine, value);
            return js.IsObject() && !js.IsArray() ? js.AsObject() : null;
        }

        internal static JsValue Get(Jint.Native.Object.ObjectInstance obj, Jint.Engine js_engine, string name) => obj.Get(JsValue.FromObject(js_engine, name), obj);

        internal static string ToString(JsValue value) => value.IsString() ? value.AsString() : value.ToString();

        internal static bool ToBoolean(JsValue value) => ToBoolean(value, false);

        internal static int? ToNullableInt(JsValue value) {
            return value.IsNull() || value.IsUndefined() ? null : (int)System.Math.Max(0, System.Math.Round(Jint.Runtime.TypeConverter.ToNumber(value)));
        }

        private static Dictionary<string, object?> ToDictionary(System.Text.Json.JsonElement element) {
            Dictionary<string, object?> dict = new Dictionary<string, object?>(System.StringComparer.Ordinal);
            foreach (System.Text.Json.JsonProperty property in element.EnumerateObject()) {
                dict[property.Name] = FromJsonElement(property.Value);
            }

            return dict;
        }

        private static List<object?> ToList(System.Text.Json.JsonElement element) {
            List<object?> list = new List<object?>();
            foreach (System.Text.Json.JsonElement item in element.EnumerateArray()) {
                list.Add(FromJsonElement(item));
            }

            return list;
        }
    }
}
