using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using System.Globalization;
// js interpreter
using Jint;
using Jint.Native;
using Jint.Native.Array;
using Jint.Native.Object;
using Jint.Runtime;
//
using Microsoft.Data.Sqlite;
// internal usings
using EngineNet.Core.ScriptEngines.Helpers;
using EngineNet.Tools;

namespace EngineNet.Core.ScriptEngines;
/// <summary>
/// Executes a JavaScript file using the embedded Jint interpreter.
/// Exposes a host API that mirrors the Lua engine capabilities.
/// </summary>
public sealed class JsScriptAction:IAction {
    private readonly String _scriptPath;
    private readonly String[] _args;

    /// <summary>
    /// Creates a new action that runs the specified JavaScript file with no arguments.
    /// </summary>
    /// <param name="scriptPath">Absolute or relative path to the JavaScript file to execute.</param>
    public JsScriptAction(String scriptPath) : this(scriptPath, Array.Empty<String>()) { }

    /// <summary>
    /// Creates a new action that runs the specified JavaScript file with arguments.
    /// </summary>
    /// <param name="scriptPath">Absolute or relative path to the JavaScript file to execute.</param>
    /// <param name="args">Optional list of arguments to expose to the script via the global <c>argv</c> array.</param>
    public JsScriptAction(String scriptPath, IEnumerable<String>? args) {
        _scriptPath = scriptPath;
        _args = args is null ? Array.Empty<String>() : args as String[] ?? new List<String>(args).ToArray();
    }

    /// <summary>
    /// Executes the JavaScript file in an embedded Jint engine.
    /// </summary>
    /// <param name="tools">Resolver used by the <c>tool(name)</c> global to locate external executables.</param>
    /// <param name="cancellationToken">Token used to cancel script execution.</param>
    /// <exception cref="FileNotFoundException">Thrown when the script file cannot be found.</exception>
    public async Task ExecuteAsync(IToolResolver tools, CancellationToken cancellationToken = default) {
        if (!File.Exists(_scriptPath)) {
            throw new FileNotFoundException("JavaScript file not found", _scriptPath);
        }

        String code = await File.ReadAllTextAsync(_scriptPath, cancellationToken);
        Engine engine = new Engine(options => options.CancellationToken(cancellationToken));
        RegisterGlobals(engine, tools);
        PreloadShimModules(engine, _scriptPath);
        Console.WriteLine($"Running js script '{_scriptPath}' with {_args.Length} args...");
        Console.WriteLine($"input args: {String.Join(", ", _args)}");
        await Task.Run(() => engine.Execute(code), cancellationToken);
    }

    /// <summary>
    /// Registers host functions and shims exposed to JavaScript, such as <c>emit</c>, <c>prompt</c>,
    /// <c>progress</c>, <c>sqlite</c>, and filesystem helpers.
    /// </summary>
    /// <param name="engine">The Jint engine instance.</param>
    /// <param name="tools">Tool resolver exposed via the <c>tool</c> global.</param>
    private void RegisterGlobals(Engine engine, IToolResolver tools) {
        engine.SetValue("tool", new Func<String, String>(tools.ResolveToolPath));
        engine.SetValue("argv", _args);
        engine.SetValue("emit", new Action<JsValue, JsValue>((ev, data) => {
            String eventName = ev.IsString() ? ev.AsString() : ev.ToString();
            IDictionary<String, Object?>? payload = null;
            if (!data.IsUndefined() && !data.IsNull()) {
                payload = JsInterop.ToDictionary(engine, data);
            }

            EngineSdk.Emit(eventName, payload);
        }));
        engine.SetValue("warn", new Action<String>(EngineSdk.Warn));
        engine.SetValue("error", new Action<String>(EngineSdk.Error));
        engine.SetValue("prompt", new Func<JsValue, JsValue, JsValue, String>((message, id, secret) => {
            String msg = message.IsString() ? message.AsString() : message.ToString();
            String promptId = id.IsNull() || id.IsUndefined() ? "q1" : id.IsString() ? id.AsString() : id.ToString();
            Boolean hide = secret.IsBoolean() && secret.AsBoolean();
            return EngineSdk.Prompt(msg, promptId, hide);
        }));
        engine.SetValue("progress", new Func<JsValue, JsValue, JsValue, EngineSdk.Progress>((total, id, label) => {
            Double totalNumber = JsInterop.ToNumber(total, 1);
            Int32 totalSteps = (Int32)Math.Max(1, Math.Round(totalNumber));
            String progressId = id.IsNull() || id.IsUndefined() ? "p1" : id.IsString() ? id.AsString() : id.ToString();
            String? labelText = label.IsNull() || label.IsUndefined() ? null : label.IsString() ? label.AsString() : label.ToString();
            return new EngineSdk.Progress(totalSteps, progressId, labelText);
        }));
        engine.SetValue("sdk", new SdkModule(engine));
        engine.SetValue("sqlite", new SqliteModule(engine));
    }

    /// <summary>
    /// Preloads a minimal set of shim modules to emulate some Lua ecosystem pieces
    /// expected by existing module scripts (e.g. <c>lfs</c>, <c>dkjson</c>, <c>debug</c>).
    /// </summary>
    /// <param name="engine">The engine to extend.</param>
    /// <param name="scriptPath">Path of the currently executing script (used for debug info).</param>
    private static void PreloadShimModules(Engine engine, String scriptPath) {
        Dictionary<String, Object?> modules = new Dictionary<String, Object?>(StringComparer.Ordinal);
        LfsModule lfs = new LfsModule();
        DkJsonModule dkjson = new DkJsonModule(engine);
        DebugModule debug = new DebugModule(scriptPath);
        modules["lfs"] = lfs;
        modules["dkjson"] = dkjson;
        modules["debug"] = debug;
        engine.SetValue("debug", debug);
        engine.SetValue("package", new PackageModule(modules));
        engine.SetValue("require", new Func<String, Object>(name => {
            return modules.TryGetValue(name, out Object? module) && module != null
                ? module
                : throw JsInterop.CreateJsException(engine, "Error", $"module '{name}' not found (only preloaded modules available)");
        }));
    }
    private sealed class SdkModule {
        private readonly Engine _engine;

        internal SdkModule(Engine engine) {
            _engine = engine;
        }

        public JsValue color_print(Object? arg1, Object? arg2 = null, Object? arg3 = null) {
            String? color = null;
            String message = String.Empty;
            Boolean newline = true;
            JsValue first = JsInterop.AsJsValue(_engine, arg1);
            if (first.IsObject() && !first.IsNull() && !first.IsUndefined() && !first.IsArray() && !first.IsString()) {
                IDictionary<String, Object?> dict = JsInterop.ToDictionary(_engine, first.AsObject());
                if (!dict.TryGetValue("color", out Object? colorObj) && !dict.TryGetValue("colour", out colorObj)) {
                    colorObj = null;
                }

                if (colorObj != null) {
                    color = colorObj.ToString();
                }

                if (dict.TryGetValue("message", out Object? msgObj) && msgObj != null) {
                    message = msgObj.ToString() ?? String.Empty;
                }

                if (dict.TryGetValue("newline", out Object? newlineObj) && newlineObj is Boolean b) {
                    newline = b;
                }
            } else {
                if (!first.IsNull() && !first.IsUndefined()) {
                    color = JsInterop.ToString(first);
                }

                JsValue messageValue = JsInterop.AsJsValue(_engine, arg2);
                if (!messageValue.IsNull() && !messageValue.IsUndefined()) {
                    message = JsInterop.ToString(messageValue);
                }

                JsValue newlineValue = JsInterop.AsJsValue(_engine, arg3);
                if (newlineValue.IsBoolean()) {
                    newline = newlineValue.AsBoolean();
                }
            }
            EngineSdk.Print(message, color, newline);
            return JsValue.Undefined;
        }

        public JsValue colour_print(Object? arg1, Object? arg2 = null, Object? arg3 = null) => color_print(arg1, arg2, arg3);

        public String ensure_project_config(String root) => ConfigHelpers.EnsureProjectConfig(root);

        public Boolean validate_source_dir(String dir) {
            try {
                ConfigHelpers.ValidateSourceDir(dir);
                return true;
            } catch {
                return false;
            }
        }

        public Boolean copy_dir(String src, String dst, Object? overwrite = null) {
            try {
                Boolean ow = JsInterop.ToBoolean(_engine, overwrite);
                ConfigHelpers.CopyDirectory(src, dst, ow);
                return true;
            } catch {
                return false;
            }
        }

        public Boolean move_dir(String src, String dst, Object? overwrite = null) {
            try {
                Boolean ow = JsInterop.ToBoolean(_engine, overwrite);
                ConfigHelpers.MoveDirectory(src, dst, ow);
                return true;
            } catch {
                return false;
            }
        }

        public String? find_subdir(String baseDir, String name) => ConfigHelpers.FindSubdir(baseDir, name);

        public Boolean has_all_subdirs(String baseDir, Object? names) {
            try {
                List<String> list = JsInterop.ToStringList(_engine, names);
                return ConfigHelpers.HasAllSubdirs(baseDir, list);
            } catch {
                return false;
            }
        }

        public Boolean ensure_dir(String path) {
            try {
                Directory.CreateDirectory(path);
                return true;
            } catch {
                return false;
            }
        }

        public Boolean path_exists(String path) => FsUtils.PathExists(path);

        public Boolean lexists(String path) => FsUtils.PathExistsIncludingLinks(path);

        public Boolean is_dir(String path) => Directory.Exists(path);

        public Boolean is_file(String path) => File.Exists(path);

        public Boolean remove_dir(String path) {
            try {
                if (Directory.Exists(path)) {
                    Directory.Delete(path, true);
                }

                return true;
            } catch {
                return false;
            }
        }

        public Boolean remove_file(String path) {
            try {
                if (FsUtils.IsSymlink(path) || File.Exists(path)) {
                    File.Delete(path);
                }

                return true;
            } catch {
                return false;
            }
        }

        public Boolean copy_file(String src, String dst, Object? overwrite = null) {
            try {
                Boolean ow = JsInterop.ToBoolean(_engine, overwrite);
                File.Copy(src, dst, ow);
                return true;
            } catch {
                return false;
            }
        }

        public Boolean create_symlink(String source, String destination, Boolean isDirectory) => FsUtils.CreateSymlink(source, destination, isDirectory);

        public Boolean is_symlink(String path) => FsUtils.IsSymlink(path);

        public String? realpath(String path) => FsUtils.RealPath(path);

        public String? readlink(String path) => FsUtils.ReadLink(path);

        public void sleep(Double seconds) {
            if (Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds <= 0) {
                return;
            }

            try {
                Thread.Sleep(TimeSpan.FromSeconds(seconds));
            } catch { }
        }

        public String md5(String text) {
            try {
                Byte[] data = MD5.HashData(Encoding.UTF8.GetBytes(text ?? String.Empty));
                return Convert.ToHexString(data).ToLowerInvariant();
            } catch {
                return String.Empty;
            }
        }

        public JsValue run_process(Object commandArgs, Object? options = null) {
            ArrayInstance array = JsInterop.EnsureArray(_engine, commandArgs, "run_process arguments");
            List<String> arguments = JsInterop.ToStringList(_engine, array);
            if (arguments.Count == 0) {
                throw JsInterop.CreateJsException(_engine, "TypeError", "run_process requires at least one argument (executable path)");
            }

            String fileName = arguments[0];
            ProcessStartInfo psi = new ProcessStartInfo {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            for (Int32 i = 1; i < arguments.Count; i++) {
                psi.ArgumentList.Add(arguments[i]);
            }

            Boolean captureStdout = true;
            Boolean captureStderr = true;
            Int32? timeoutMs = null;
            ObjectInstance? opts = JsInterop.TryGetPlainObject(_engine, options);
            if (opts != null) {
                JsValue cwdValue = JsInterop.Get(opts, _engine, "cwd");
                if (cwdValue.IsString()) {
                    psi.WorkingDirectory = cwdValue.AsString();
                }

                JsValue stdoutValue = JsInterop.Get(opts, _engine, "capture_stdout");
                if (!stdoutValue.IsUndefined()) {
                    captureStdout = JsInterop.ToBoolean(stdoutValue);
                }

                JsValue stderrValue = JsInterop.Get(opts, _engine, "capture_stderr");
                if (!stderrValue.IsUndefined()) {
                    captureStderr = JsInterop.ToBoolean(stderrValue);
                }

                JsValue timeoutValue = JsInterop.Get(opts, _engine, "timeout_ms");
                if (!timeoutValue.IsUndefined()) {
                    timeoutMs = JsInterop.ToNullableInt(timeoutValue);
                }

                JsValue envValue = JsInterop.Get(opts, _engine, "env");
                if (!envValue.IsUndefined() && envValue.IsObject()) {
                    IDictionary<String, Object?> dict = JsInterop.ToDictionary(_engine, envValue.AsObject());
                    foreach (KeyValuePair<String, Object?> kv in dict) {
                        if (kv.Value is String s) {
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

            StringBuilder stdoutBuilder = new StringBuilder();
            StringBuilder stderrBuilder = new StringBuilder();
            using Process process = new Process();
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
                        } catch { }
                        throw JsInterop.CreateJsException(_engine, "Error", $"Process '{fileName}' timed out after {timeoutMs.Value} ms");
                    }
                } else {
                    process.WaitForExit();
                }
            } catch (Exception ex) {
                throw JsInterop.CreateJsException(_engine, "Error", $"Failed to run process '{fileName}': {ex.Message}");
            }
            Dictionary<String, Object?> result = new Dictionary<String, Object?>(StringComparer.Ordinal) {
                ["exit_code"] = process.ExitCode,
                ["success"] = process.ExitCode == 0
            };
            if (captureStdout) {
                result["stdout"] = stdoutBuilder.ToString();
            }

            if (captureStderr) {
                result["stderr"] = stderrBuilder.ToString();
            }

            return JsInterop.FromObject(_engine, result);
        }
    }
    private sealed class SqliteModule {
        private readonly Engine _engine;

        internal SqliteModule(Engine engine) {
            _engine = engine;
        }

        public SqliteHandle open(String path) => new SqliteHandle(_engine, path);
    }
    private sealed class SqliteHandle:IDisposable {
        private readonly Engine _engine;
        private readonly SqliteConnection _connection;
        private SqliteTransaction? _transaction;
        private Boolean _disposed;

        internal SqliteHandle(Engine engine, String path) {
            _engine = engine;
            String fullPath = Path.GetFullPath(path);
            SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder {
                DataSource = fullPath
            };
            _connection = new SqliteConnection(builder.ConnectionString);
            _connection.Open();
        }

        public Int32 exec(String sql, Object? parameters = null) {
            EnsureNotDisposed();
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = sql;
            if (_transaction != null) {
                command.Transaction = _transaction;
            }

            BindParameters(command, parameters);
            return command.ExecuteNonQuery();
        }

        public JsValue query(String sql, Object? parameters = null) {
            EnsureNotDisposed();
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = sql;
            if (_transaction != null) {
                command.Transaction = _transaction;
            }

            BindParameters(command, parameters);
            using SqliteDataReader reader = command.ExecuteReader();
            List<Dictionary<String, Object?>> rows = new List<Dictionary<String, Object?>>();
            while (reader.Read()) {
                Dictionary<String, Object?> row = new Dictionary<String, Object?>(StringComparer.Ordinal);
                for (Int32 i = 0; i < reader.FieldCount; i++) {
                    String column = reader.GetName(i);
                    row[column] = ConvertValue(reader.GetValue(i));
                }
                rows.Add(row);
            }
            return JsInterop.FromObject(_engine, rows);
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
                throw new ObjectDisposedException(nameof(SqliteHandle));
            }
        }

        private void BindParameters(SqliteCommand command, Object? parameters) {
            if (parameters is null) {
                return;
            }

            IDictionary<String, Object?> dict = JsInterop.ToDictionary(_engine, parameters);
            foreach (KeyValuePair<String, Object?> kv in dict) {
                SqliteParameter parameter = command.CreateParameter();
                String name = kv.Key;
                if (!name.StartsWith(":", StringComparison.Ordinal) && !name.StartsWith("@", StringComparison.Ordinal) && !name.StartsWith("$", StringComparison.Ordinal)) {
                    name = ":" + name;
                }

                parameter.ParameterName = name;
                parameter.Value = kv.Value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
        }

        private static Object? ConvertValue(Object? value) {
            return value is null || value is DBNull
                ? null
                : value switch {
                    DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
                    Byte[] bytes => Convert.ToHexString(bytes),
                    _ => value
                };
        }
    }
    private sealed class LfsModule {

        public String currentdir() => Environment.CurrentDirectory;

        public Object? mkdir(String path) {
            try {
                Directory.CreateDirectory(path);
                return true;
            } catch {
                return null;
            }
        }

        public Object? attributes(String path) {
            if (Directory.Exists(path)) {
                return new Dictionary<String, Object?>(StringComparer.Ordinal) {
                    ["mode"] = "directory"
                };
            }
            if (File.Exists(path)) {
                FileInfo info = new FileInfo(path);
                return new Dictionary<String, Object?>(StringComparer.Ordinal) {
                    ["mode"] = "file",
                    ["size"] = info.Length,
                    ["modtime"] = info.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture)
                };
            }
            return null;
        }

        public Func<Object?> dir(String path) {
            IEnumerable<String> Enumerate() {
                yield return ".";
                yield return "..";
                if (Directory.Exists(path)) {
                    foreach (String entry in Directory.EnumerateFileSystemEntries(path)) {
                        yield return Path.GetFileName(entry);
                    }
                }
            }
            IEnumerator<String> enumerator = Enumerate().GetEnumerator();
            return () => enumerator.MoveNext() ? enumerator.Current : null;
        }
    }
    private sealed class DkJsonModule {
        private readonly Engine _engine;

        internal DkJsonModule(Engine engine) {
            _engine = engine;
        }

        public String encode(JsValue value, JsValue options) {
            Boolean indent = false;
            if (!options.IsUndefined() && !options.IsNull() && options.IsObject()) {
                JsValue indentVal = JsInterop.Get(options.AsObject(), _engine, "indent");
                if (!indentVal.IsUndefined()) {
                    indent = JsInterop.ToBoolean(indentVal);
                }
            }
            Object? plain = JsInterop.ToPlainObject(_engine, value);
            JsonSerializerOptions jsonOptions = new JsonSerializerOptions { WriteIndented = indent };
            return JsonSerializer.Serialize(plain, jsonOptions);
        }

        public JsValue encode(JsValue value) => encode(value, JsValue.Undefined);

        public JsValue decode(String json) {
            try {
                using JsonDocument doc = JsonDocument.Parse(json);
                return JsInterop.FromJsonElement(_engine, doc.RootElement);
            } catch {
                return JsValue.Null;
            }
        }
    }
    private sealed class DebugModule {
        private readonly String _scriptPath;

        internal DebugModule(String scriptPath) {
            _scriptPath = scriptPath;
        }

        public Object getinfo(Object? level = null, Object? what = null) {
            return new Dictionary<String, Object?>(StringComparer.Ordinal) {
                ["source"] = "@" + _scriptPath
            };
        }
    }
    private sealed class PackageModule {
        public IDictionary<String, Object?> loaded {
            get;
        }

        internal PackageModule(IDictionary<String, Object?> modules) {
            loaded = modules;
        }
    }
    private static class FsUtils {

        public static Boolean PathExists(String path) => Directory.Exists(path) || File.Exists(path);

        public static Boolean PathExistsIncludingLinks(String path) {
            if (PathExists(path)) {
                return true;
            }

            try {
                FileSystemInfo info = GetInfo(path);
                return info.Exists ? true : info.LinkTarget != null;
            } catch {
                return false;
            }
        }

        public static Boolean IsSymlink(String path) {
            try {
                FileSystemInfo info = GetInfo(path);
                return info.LinkTarget != null || info.Attributes.HasFlag(FileAttributes.ReparsePoint);
            } catch {
                return false;
            }
        }

        public static Boolean CreateSymlink(String source, String destination, Boolean isDirectory) {
            try {
                String destFull = Path.GetFullPath(destination);
                String srcFull = Path.GetFullPath(source);
                String? parent = Path.GetDirectoryName(destFull);
                if (!String.IsNullOrEmpty(parent)) {
                    Directory.CreateDirectory(parent);
                }

                if (isDirectory) {
                    Directory.CreateSymbolicLink(destFull, srcFull);
                } else {
                    File.CreateSymbolicLink(destFull, srcFull);
                }

                return true;
            } catch {
                return false;
            }
        }

        public static String? RealPath(String path) {
            try {
                return Path.GetFullPath(path);
            } catch {
                return null;
            }
        }

        public static String? ReadLink(String path) {
            try {
                FileSystemInfo info = GetInfo(path);
                return info.LinkTarget;
            } catch {
                return null;
            }
        }

        private static FileSystemInfo GetInfo(String path) {
            String full = Path.GetFullPath(path);
            DirectoryInfo dirInfo = new DirectoryInfo(full);
            if (dirInfo.Exists) {
                return dirInfo;
            }

            FileInfo fileInfo = new FileInfo(full);
            return fileInfo.Exists
                ? fileInfo
                : full.EndsWith(Path.DirectorySeparatorChar) || full.EndsWith(Path.AltDirectorySeparatorChar)
                ? new DirectoryInfo(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : fileInfo;
        }
    }
    private static class JsInterop {

        public static JsValue AsJsValue(Engine engine, Object? value) {
            return value is null ? JsValue.Null : value is JsValue js ? js : JsValue.FromObject(engine, value);
        }

        public static Boolean ToBoolean(Engine engine, Object? value, Boolean defaultValue = false) => ToBoolean(AsJsValue(engine, value), defaultValue);

        public static Boolean ToBoolean(JsValue value, Boolean defaultValue = false) {
            return value.IsNull() || value.IsUndefined() ? defaultValue : TypeConverter.ToBoolean(value);
        }

        public static Double ToNumber(JsValue value, Double defaultValue = 0) {
            return value.IsNull() || value.IsUndefined() ? defaultValue : TypeConverter.ToNumber(value);
        }

        public static ArrayInstance EnsureArray(Engine engine, Object? value, String paramName) {
            JsValue js = AsJsValue(engine, value);
            return !js.IsArray() ? throw CreateJsException(engine, "TypeError", $"{paramName} must be an array") : (ArrayInstance)js.AsArray();
        }

        public static List<String> ToStringList(Engine engine, Object? value) => ToStringList(engine, EnsureArray(engine, value, "value"));

        public static List<String> ToStringList(Engine engine, ArrayInstance array) {
            List<String> list = new List<String>();
            foreach (JsValue item in array) {
                if (item.IsNull() || item.IsUndefined()) {
                    break;
                }

                list.Add(ToString(item));
            }
            return list;
        }

        public static IDictionary<String, Object?> ToDictionary(Engine engine, Object? value) {
            JsValue js = AsJsValue(engine, value);
            return js.IsNull() || js.IsUndefined()
                ? new Dictionary<String, Object?>(StringComparer.Ordinal)
                : !js.IsObject() ? throw CreateJsException(engine, "TypeError", "Expected object value") : ToDictionary(engine, js.AsObject());
        }

        public static IDictionary<String, Object?> ToDictionary(Engine engine, ObjectInstance obj) {
            Dictionary<String, Object?> dict = new Dictionary<String, Object?>(StringComparer.Ordinal);
            foreach (JsValue key in obj.GetOwnPropertyKeys()) {
                JsValue value = obj.Get(key, obj);
                dict[key.IsString() ? key.AsString() : key.ToString()] = ToPlainObject(engine, value);
            }
            return dict;
        }

        public static Object? ToPlainObject(Engine engine, JsValue value) {
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
                List<Object?> list = new List<Object?>();
                foreach (JsValue item in value.AsArray()) {
                    list.Add(ToPlainObject(engine, item));
                }

                return list;
            }
            if (value.IsObject()) {
                Dictionary<String, Object?> dict = new Dictionary<String, Object?>(StringComparer.Ordinal);
                ObjectInstance obj = value.AsObject();
                foreach (JsValue key in obj.GetOwnPropertyKeys()) {
                    dict[key.IsString() ? key.AsString() : key.ToString()] = ToPlainObject(engine, obj.Get(key, obj));
                }

                return dict;
            }
            return value.ToString();
        }

        public static JavaScriptException CreateJsException(Engine engine, String constructorName, String message) {
            try {
                JsValue ctorValue = engine.GetValue(constructorName);
                JsValue errorInstance = engine.Invoke(ctorValue, message);
                return new JavaScriptException(errorInstance);
            } catch {
            }
            return new JavaScriptException(JsValue.FromObject(engine, message));
        }

        public static JsValue FromObject(Engine engine, Object? value) => JsValue.FromObject(engine, value);

        public static JsValue FromJsonElement(Engine engine, JsonElement element) => FromObject(engine, FromJsonElement(element));

        public static Object? FromJsonElement(JsonElement element) => element.ValueKind switch {
            JsonValueKind.Object => ToDictionary(element),
            JsonValueKind.Array => ToList(element),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out Int64 l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };

        public static ObjectInstance? TryGetPlainObject(Engine engine, Object? value) {
            JsValue js = AsJsValue(engine, value);
            return js.IsObject() && !js.IsArray() ? js.AsObject() : null;
        }

        public static JsValue Get(ObjectInstance obj, Engine engine, String name) => obj.Get(JsValue.FromObject(engine, name), obj);

        public static String ToString(JsValue value) => value.IsString() ? value.AsString() : value.ToString();

        public static Boolean ToBoolean(JsValue value) => ToBoolean(value, false);

        public static Int32? ToNullableInt(JsValue value) {
            return value.IsNull() || value.IsUndefined() ? null : (Int32)Math.Max(0, Math.Round(TypeConverter.ToNumber(value)));
        }

        private static Dictionary<String, Object?> ToDictionary(JsonElement element) {
            Dictionary<String, Object?> dict = new Dictionary<String, Object?>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject()) {
                dict[property.Name] = FromJsonElement(property.Value);
            }

            return dict;
        }

        private static List<Object?> ToList(JsonElement element) {
            List<Object?> list = new List<Object?>();
            foreach (JsonElement item in element.EnumerateArray()) {
                list.Add(FromJsonElement(item));
            }

            return list;
        }
    }
}
