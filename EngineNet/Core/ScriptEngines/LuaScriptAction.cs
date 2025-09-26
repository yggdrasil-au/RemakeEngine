// MoonSharp -- Lua interpreter
using MoonSharp.Interpreter;

using System.Globalization;
using System.Text.Json;

using RemakeEngine.Sys;
using RemakeEngine.Tools;

namespace RemakeEngine.Core.ScriptEngines;

/// <summary>
/// Executes a Lua script using the embedded MoonSharp interpreter.
///
/// Exposed Lua globals for module authors:
/// - tool(name): resolve a registered tool to an absolute path.
/// - argv: string array of arguments passed to the script.
/// - emit(event, data?): send a structured engine event. `data` is an optional table.
///   Example: emit("warning", { message = "Heads up" })
/// - warn(message): shorthand for emit("warning", { message = message })
/// - error(message): shorthand for emit("error", { message = message })
/// - prompt(message, id?, secret?): prompts the user; returns the entered string.
///   Example: local answer = prompt("Continue? (y/n)", "confirm1", false)
/// - progress(total, id?, label?): creates a progress handle; returns an object with Update(inc?)
///   Example: local p = progress(100, "extract", "Extracting files"); p:Update(5)
///
/// All helpers wrap RemakeEngine.Utils.EngineSdk for consistent engine integration.
/// </summary>
public sealed class LuaScriptAction:Helpers.IAction {
    private readonly String _scriptPath;
    private readonly String[] _args;

    public LuaScriptAction(String scriptPath) : this(scriptPath, Array.Empty<String>()) { }

    public LuaScriptAction(String scriptPath, IEnumerable<String>? args) {
        _scriptPath = scriptPath;
        _args = args is null ? Array.Empty<String>() : (args as String[] ?? new List<String>(args).ToArray());
    }

    public async Task ExecuteAsync(IToolResolver tools, CancellationToken cancellationToken = default) {
        if (!File.Exists(_scriptPath))
            throw new FileNotFoundException("Lua script not found", _scriptPath);

        String code = await File.ReadAllTextAsync(_scriptPath, cancellationToken);
        Script lua = new Script();

        // Expose a simple function to resolve tool paths from Lua:
        lua.Globals["tool"] = (Func<String, String>)(tools.ResolveToolPath);
        lua.Globals["argv"] = _args;

        // Register types used as userdata
        UserData.RegisterType<EngineSdk.Progress>();

        // EngineSdk wrappers
        lua.Globals["warn"] = (Action<String>)EngineSdk.Warn;
        lua.Globals["error"] = (Action<String>)EngineSdk.Error;

        // emit(event, data?) where data is an optional Lua table
        lua.Globals["emit"] = (Action<DynValue, DynValue>)((ev, data) => {
            String evName = ev.Type == DataType.String ? ev.String : ev.ToPrintString();
            IDictionary<String, Object?>? dict = (data.Type == DataType.Nil || data.Type == DataType.Void) ? null : TableToDictionary(data.Table);
            EngineSdk.Emit(evName, dict);
        });

        // prompt(message, id?, secret?) -> string
        lua.Globals["prompt"] = (Func<DynValue, DynValue, DynValue, String>)((message, id, secret) => {
            String msg = message.Type == DataType.String ? message.String : message.ToPrintString();
            String pid = (id.Type == DataType.Nil || id.Type == DataType.Void) ? "q1" : (id.Type == DataType.String ? id.String : id.ToPrintString());
            Boolean sec = (secret.Type == DataType.Boolean) && secret.Boolean;
            return EngineSdk.Prompt(msg, pid, sec);
        });

        // progress(total, id?, label?) -> EngineSdk.Progress userdata
        lua.Globals["progress"] = (Func<Int32, String?, String?, EngineSdk.Progress>)((total, id, label) => {
            String pid = String.IsNullOrEmpty(id) ? "p1" : id!;
            return new EngineSdk.Progress(total, pid, label);
        });

        // sdk: config/filesystem helpers + terminal print helpers
        Table sdk = new Table(lua);
        // color/colour print: accepts either (color, message[, newline]) or a table { colour=?, color=?, message=?, newline=? }
        CallbackFunction colorPrintFunc = new CallbackFunction((ctx, args) => {
            String? color = null;
            String message = String.Empty;
            Boolean newline = true;
            if (args.Count >= 2 && (args[0].Type == DataType.String || args[0].Type == DataType.UserData)) {
                // color, message, [newline]
                color = args[0].ToPrintString();
                message = args[1].Type == DataType.String ? args[1].String : args[1].ToPrintString();
                if (args.Count >= 3 && args[2].Type == DataType.Boolean)
                    newline = args[2].Boolean;
            } else if (args.Count >= 1 && args[0].Type == DataType.Table) {
                Table t = args[0].Table;
                DynValue c = t.Get("color");
                if (c.IsNil())
                    c = t.Get("colour");
                if (!c.IsNil())
                    color = c.Type == DataType.String ? c.String : c.ToPrintString();
                DynValue m = t.Get("message");
                if (!m.IsNil())
                    message = m.Type == DataType.String ? m.String : m.ToPrintString();
                DynValue nl = t.Get("newline");
                if (!nl.IsNil() && nl.Type == DataType.Boolean)
                    newline = nl.Boolean;
            }
            EngineSdk.Print(message, color, newline);
            return DynValue.Nil;
        });
        sdk["color_print"] = DynValue.NewCallback(colorPrintFunc);
        sdk["colour_print"] = DynValue.NewCallback(colorPrintFunc);
        sdk["ensure_project_config"] = (Func<String, String>)((root) => ConfigHelpers.EnsureProjectConfig(root));
        sdk["validate_source_dir"] = (Func<String, Boolean>)((dir) => {
            try {
                ConfigHelpers.ValidateSourceDir(dir);
                return true;
            } catch { return false; }
        });

        sdk["copy_dir"] = (Func<String, String, DynValue, Boolean>)((src, dst, overwrite) => {
            try {
                Boolean ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                ConfigHelpers.CopyDirectory(src, dst, ow);
                return true;
            } catch { return false; }
        });
        sdk["move_dir"] = (Func<String, String, DynValue, Boolean>)((src, dst, overwrite) => {
            try {
                Boolean ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                ConfigHelpers.MoveDirectory(src, dst, ow);
                return true;
            } catch { return false; }
        });
        sdk["find_subdir"] = (Func<String, String, String?>)((baseDir, name) => ConfigHelpers.FindSubdir(baseDir, name));
        sdk["has_all_subdirs"] = (Func<String, Table, Boolean>)((baseDir, names) => {
            try {
                List<String> list = TableToStringList(names);
                return ConfigHelpers.HasAllSubdirs(baseDir, list);
            } catch { return false; }
        });
        lua.Globals["sdk"] = sdk;

        // Preload minimal shims for LuaFileSystem (lfs) and dkjson used by game modules
        PreloadShimModules(lua, _scriptPath);
        Console.WriteLine($"Running lua script '{_scriptPath}' with {_args.Length} args...");
        await Task.Run(() => lua.DoString(code), cancellationToken);
    }

    private static void PreloadShimModules(Script lua, String scriptPath) {
        // Ensure package.loaded exists
        Table package = lua.Globals.Get("package").IsNil() ? new Table(lua) : lua.Globals.Get("package").Table;
        if (package.Get("loaded").IsNil())
            package["loaded"] = new Table(lua);
        Table loaded = package.Get("loaded").Table;

        // Minimal 'require' shim: return preloaded modules from package.loaded
        if (lua.Globals.Get("require").IsNil()) {
            lua.Globals["require"] = (Func<String, DynValue>)(name => {
                DynValue mod = loaded.Get(name);
                return !mod.IsNil() ? mod : throw new ScriptRuntimeException($"module '{name}' not found (only preloaded modules available)");
            });
        }

        // lfs shim
        Table lfs = new Table(lua);
        lfs["currentdir"] = (Func<String>)(() => Environment.CurrentDirectory);
        // lfs.mkdir(path) -> true on success, nil on failure (minimal behavior)
        lfs["mkdir"] = (Func<String, DynValue>)((path) => {
            try {
                Directory.CreateDirectory(path);
                return DynValue.True;
            } catch (Exception) {
                // Return nil to indicate failure; message not used by current scripts
                return DynValue.Nil;
            }
        });
        lfs["attributes"] = (Func<String, DynValue>)((path) => {
            if (Directory.Exists(path)) {
                Table t = new Table(lua);
                t["mode"] = "directory";
                return DynValue.NewTable(t);
            }
            if (File.Exists(path)) {
                FileInfo info = new FileInfo(path);
                Table t = new Table(lua);
                t["mode"] = "file";
                t["size"] = info.Length;
                t["modtime"] = info.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture);
                return DynValue.NewTable(t);
            }
            return DynValue.Nil;
        });
        lfs["dir"] = (Func<String, DynValue>)((path) => {
            // Return an iterator function like lfs.dir
            IEnumerable<String> Enumerate() {
                // In real lfs, '.' and '..' are included; we'll include them for compatibility
                yield return ".";
                yield return "..";
                if (Directory.Exists(path)) {
                    foreach (String entry in Directory.EnumerateFileSystemEntries(path)) {
                        yield return Path.GetFileName(entry);
                    }
                }
            }
            IEnumerator<String> enumerator = Enumerate().GetEnumerator();
            CallbackFunction iterator = new CallbackFunction((ctx, args) => {
                return enumerator.MoveNext() ? DynValue.NewString(enumerator.Current) : DynValue.Nil;
            });
            return DynValue.NewCallback(iterator);
        });
        loaded["lfs"] = DynValue.NewTable(lfs);

        // dkjson shim: provides encode(value, opts?) and decode(string)
        Table dkjson = new Table(lua);
        dkjson["encode"] = (Func<DynValue, DynValue, String>)((val, opts) => {
            Boolean indent = false;
            if (opts.Type == DataType.Table) {
                DynValue indentVal = opts.Table.Get("indent");
                indent = indentVal.Type == DataType.Boolean && indentVal.Boolean;
            }
            Object? obj = FromDynValue(val);
            JsonSerializerOptions jsonOpts = new JsonSerializerOptions { WriteIndented = indent };
            return JsonSerializer.Serialize(obj, jsonOpts);
        });
        dkjson["decode"] = (Func<String, DynValue>)((json) => {
            try {
                using JsonDocument doc = JsonDocument.Parse(json);
                return JsonElementToDynValue(lua, doc.RootElement);
            } catch {
                return DynValue.Nil; // caller will treat as error
            }
        });
        loaded["dkjson"] = DynValue.NewTable(dkjson);

        // debug shim: provide getinfo with .source used by modules to find their file path
        Table debugTbl = lua.Globals.Get("debug").IsNil() ? new Table(lua) : lua.Globals.Get("debug").Table;
        debugTbl["getinfo"] = (Func<DynValue, DynValue, DynValue>)((level, what) => {
            Table t = new Table(lua);
            // Lua expects '@' prefix for file paths
            t["source"] = "@" + scriptPath;
            return DynValue.NewTable(t);
        });
        lua.Globals["debug"] = debugTbl;

        // publish back package (in case it didn't exist)
        lua.Globals["package"] = package;
    }

    private static DynValue JsonElementToDynValue(Script lua, JsonElement el) {
        switch (el.ValueKind) {
            case JsonValueKind.Object:
                Table t = new Table(lua);
                foreach (JsonProperty p in el.EnumerateObject()) {
                    t[p.Name] = JsonElementToDynValue(lua, p.Value);
                }
                return DynValue.NewTable(t);
            case JsonValueKind.Array:
                Table arr = new Table(lua);
                Int32 i = 1;
                foreach (JsonElement item in el.EnumerateArray()) {
                    arr[i++] = JsonElementToDynValue(lua, item);
                }
                return DynValue.NewTable(arr);
            case JsonValueKind.String:
                return DynValue.NewString(el.GetString() ?? String.Empty);
            case JsonValueKind.Number:
                if (el.TryGetDouble(out Double d))
                    return DynValue.NewNumber(d);
                return DynValue.NewNumber(0);
            case JsonValueKind.True:
                return DynValue.True;
            case JsonValueKind.False:
                return DynValue.False;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                return DynValue.Nil;
        }
    }

    private static IDictionary<String, Object?> TableToDictionary(Table table) {
        Dictionary<String, Object?> dict = new Dictionary<String, Object?>(StringComparer.Ordinal);
        foreach (TablePair pair in table.Pairs) {
            // Convert key to string
            String key = pair.Key.Type switch {
                DataType.String => pair.Key.String,
                DataType.Number => pair.Key.Number.ToString(CultureInfo.InvariantCulture),
                _ => pair.Key.ToPrintString()
            };
            dict[key] = FromDynValue(pair.Value);
        }
        return dict;
    }

    private static Object? FromDynValue(DynValue v) => v.Type switch {
        DataType.Nil or DataType.Void => null,
        DataType.Boolean => v.Boolean,
        DataType.Number => v.Number,
        DataType.String => v.String,
        DataType.Table => TableToPlainObject(v.Table),
        _ => v.ToPrintString()
    };

    private static Object TableToPlainObject(Table t) {
        // Heuristic: if all keys are consecutive 1..n numbers, treat as array
        Int32 count = 0;
        Boolean arrayLike = true;
        foreach (TablePair pair in t.Pairs) {
            count++;
            if (pair.Key.Type != DataType.Number)
                arrayLike = false;
        }
        if (arrayLike) {
            List<Object?> list = new List<Object?>(count);
            for (Int32 i = 1; i <= count; i++) {
                DynValue dv = t.Get(i);
                list.Add(FromDynValue(dv));
            }
            return list;
        }
        return TableToDictionary(t);
    }

    private static List<String> TableToStringList(Table t) {
        List<String> list = new List<String>();
        // Iterate up to the numeric length; stop when we hit a Nil entry
        for (Int32 i = 1; i <= t.Length; i++) {
            DynValue dv = t.Get(i);
            if (dv.Type == DataType.Nil || dv.Type == DataType.Void)
                break;
            String s = dv.Type == DataType.String ? dv.String : dv.ToPrintString();
            list.Add(s);
        }
        return list;
    }
}
