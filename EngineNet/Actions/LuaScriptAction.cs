using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MoonSharp.Interpreter;
using RemakeEngine.Tools;
using RemakeEngine.Utils;
using System.Globalization;
using System.Text.Json;

namespace RemakeEngine.Actions;

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
public sealed class LuaScriptAction:IAction {
    private readonly string _scriptPath;
    private readonly string[] _args;

    public LuaScriptAction(string scriptPath) : this(scriptPath, Array.Empty<string>()) { }

    public LuaScriptAction(string scriptPath, IEnumerable<string>? args) {
        _scriptPath = scriptPath;
        _args = args is null ? Array.Empty<string>() : (args as string[] ?? new List<string>(args).ToArray());
    }

    public async Task ExecuteAsync(IToolResolver tools, CancellationToken cancellationToken = default) {
        if (!File.Exists(_scriptPath))
            throw new FileNotFoundException("Lua script not found", _scriptPath);

        var code = await File.ReadAllTextAsync(_scriptPath, cancellationToken);
        var lua = new Script();

        // Expose a simple function to resolve tool paths from Lua:
        lua.Globals["tool"] = (Func<string, string>)(tools.ResolveToolPath);
        lua.Globals["argv"] = _args;

        // Register types used as userdata
        UserData.RegisterType<EngineSdk.Progress>();

        // EngineSdk wrappers
		lua.Globals["warn"] = (Action<string>)EngineSdk.Warn;
        lua.Globals["error"] = (Action<string>)EngineSdk.Error;

        // emit(event, data?) where data is an optional Lua table
        lua.Globals["emit"] = (Action<DynValue, DynValue>)((ev, data) => {
            var evName = ev.Type == DataType.String ? ev.String : ev.ToPrintString();
            var dict = (data.Type == DataType.Nil || data.Type == DataType.Void) ? null : TableToDictionary(data.Table);
            EngineSdk.Emit(evName, dict);
        });

        // prompt(message, id?, secret?) -> string
		lua.Globals["prompt"] = (Func<DynValue, DynValue, DynValue, string>)((message, id, secret) => {
            var msg = message.Type == DataType.String ? message.String : message.ToPrintString();
            var pid = (id.Type == DataType.Nil || id.Type == DataType.Void) ? "q1" : (id.Type == DataType.String ? id.String : id.ToPrintString());
            var sec = (secret.Type == DataType.Boolean) && secret.Boolean;
            return EngineSdk.Prompt(msg, pid, sec);
        });

        // progress(total, id?, label?) -> EngineSdk.Progress userdata
		lua.Globals["progress"] = (Func<int, string?, string?, EngineSdk.Progress>)((total, id, label) => {
            var pid = string.IsNullOrEmpty(id) ? "p1" : id!;
            return new EngineSdk.Progress(total, pid, label);
        });

        // sdk: config/filesystem helpers + terminal print helpers
		var sdk = new Table(lua);
        // color/colour print: accepts either (color, message[, newline]) or a table { colour=?, color=?, message=?, newline=? }
        CallbackFunction colorPrintFunc = new CallbackFunction((ctx, args) =>
        {
            string? color = null; string message = string.Empty; bool newline = true;
            if (args.Count >= 2 && (args[0].Type == DataType.String || args[0].Type == DataType.UserData))
            {
                // color, message, [newline]
                color = args[0].ToPrintString();
                message = args[1].Type == DataType.String ? args[1].String : args[1].ToPrintString();
                if (args.Count >= 3 && args[2].Type == DataType.Boolean) newline = args[2].Boolean;
            }
            else if (args.Count >= 1 && args[0].Type == DataType.Table)
            {
                var t = args[0].Table;
                var c = t.Get("color"); if (c.IsNil()) c = t.Get("colour");
                if (!c.IsNil()) color = c.Type == DataType.String ? c.String : c.ToPrintString();
                var m = t.Get("message"); if (!m.IsNil()) message = m.Type == DataType.String ? m.String : m.ToPrintString();
                var nl = t.Get("newline"); if (!nl.IsNil() && nl.Type == DataType.Boolean) newline = nl.Boolean;
            }
            EngineSdk.Print(message, color, newline);
            return DynValue.Nil;
        });
        sdk["color_print"] = DynValue.NewCallback(colorPrintFunc);
        sdk["colour_print"] = DynValue.NewCallback(colorPrintFunc);
        sdk["ensure_project_config"] = (Func<string, string>)((root) => ConfigHelpers.EnsureProjectConfig(root));
        sdk["validate_source_dir"] = (Func<string, bool>)((dir) => {
            try { ConfigHelpers.ValidateSourceDir(dir); return true; }
            catch { return false; }
        });

        sdk["copy_dir"] = (Func<string, string, DynValue, bool>)((src, dst, overwrite) => {
            try {
                var ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                ConfigHelpers.CopyDirectory(src, dst, ow);
                return true;
            } catch { return false; }
        });
        sdk["move_dir"] = (Func<string, string, DynValue, bool>)((src, dst, overwrite) => {
            try {
                var ow = overwrite.Type == DataType.Boolean && overwrite.Boolean;
                ConfigHelpers.MoveDirectory(src, dst, ow);
                return true;
            } catch { return false; }
        });
        sdk["find_subdir"] = (Func<string, string, string?>)((baseDir, name) => ConfigHelpers.FindSubdir(baseDir, name));
        sdk["has_all_subdirs"] = (Func<string, Table, bool>)((baseDir, names) => {
            try {
                var list = TableToStringList(names);
                return ConfigHelpers.HasAllSubdirs(baseDir, list);
            } catch { return false; }
        });
        lua.Globals["sdk"] = sdk;

        // Preload minimal shims for LuaFileSystem (lfs) and dkjson used by game modules
		PreloadShimModules(lua, _scriptPath);
		Console.WriteLine($"Running lua script '{_scriptPath}' with {_args.Length} args...");
        await Task.Run(() => lua.DoString(code), cancellationToken);
    }

    private static void PreloadShimModules(Script lua, string scriptPath)
    {
        // Ensure package.loaded exists
        var package = lua.Globals.Get("package").IsNil() ? new Table(lua) : lua.Globals.Get("package").Table;
        if (package.Get("loaded").IsNil()) package["loaded"] = new Table(lua);
        var loaded = package.Get("loaded").Table;

        // Minimal 'require' shim: return preloaded modules from package.loaded
        if (lua.Globals.Get("require").IsNil())
        {
            lua.Globals["require"] = (Func<string, DynValue>)(name =>
            {
                var mod = loaded.Get(name);
                if (!mod.IsNil()) return mod;
                throw new ScriptRuntimeException($"module '{name}' not found (only preloaded modules available)");
            });
        }

        // lfs shim
        var lfs = new Table(lua);
        lfs["currentdir"] = (Func<string>)(() => Environment.CurrentDirectory);
        lfs["attributes"] = (Func<string, DynValue>)((path) =>
        {
            if (Directory.Exists(path))
            {
                var t = new Table(lua);
                t["mode"] = "directory";
                return DynValue.NewTable(t);
            }
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                var t = new Table(lua);
                t["mode"] = "file";
                t["size"] = info.Length;
                t["modtime"] = info.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture);
                return DynValue.NewTable(t);
            }
            return DynValue.Nil;
        });
        lfs["dir"] = (Func<string, DynValue>)((path) =>
        {
            // Return an iterator function like lfs.dir
            IEnumerable<string> Enumerate()
            {
                // In real lfs, '.' and '..' are included; we'll include them for compatibility
                yield return ".";
                yield return "..";
                if (Directory.Exists(path))
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(path))
                    {
                        yield return Path.GetFileName(entry);
                    }
                }
            }
            var enumerator = Enumerate().GetEnumerator();
            CallbackFunction iterator = new CallbackFunction((ctx, args) =>
            {
                if (enumerator.MoveNext())
                {
                    return DynValue.NewString(enumerator.Current);
                }
                return DynValue.Nil;
            });
            return DynValue.NewCallback(iterator);
        });
        loaded["lfs"] = DynValue.NewTable(lfs);

        // dkjson shim: provides encode(value, opts?) and decode(string)
        var dkjson = new Table(lua);
        dkjson["encode"] = (Func<DynValue, DynValue, string>)((val, opts) =>
        {
            bool indent = false;
            if (opts.Type == DataType.Table)
            {
                var indentVal = opts.Table.Get("indent");
                indent = indentVal.Type == DataType.Boolean && indentVal.Boolean;
            }
            var obj = FromDynValue(val);
            var jsonOpts = new JsonSerializerOptions { WriteIndented = indent };
            return JsonSerializer.Serialize(obj, jsonOpts);
        });
        dkjson["decode"] = (Func<string, DynValue>)((json) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return JsonElementToDynValue(lua, doc.RootElement);
            }
            catch
            {
                return DynValue.Nil; // caller will treat as error
            }
        });
        loaded["dkjson"] = DynValue.NewTable(dkjson);

        // debug shim: provide getinfo with .source used by modules to find their file path
        var debugTbl = lua.Globals.Get("debug").IsNil() ? new Table(lua) : lua.Globals.Get("debug").Table;
        debugTbl["getinfo"] = (Func<DynValue, DynValue, DynValue>)((level, what) =>
        {
            var t = new Table(lua);
            // Lua expects '@' prefix for file paths
            t["source"] = "@" + scriptPath;
            return DynValue.NewTable(t);
        });
        lua.Globals["debug"] = debugTbl;

        // publish back package (in case it didn't exist)
        lua.Globals["package"] = package;
    }

    private static DynValue JsonElementToDynValue(Script lua, JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                var t = new Table(lua);
                foreach (var p in el.EnumerateObject())
                {
                    t[p.Name] = JsonElementToDynValue(lua, p.Value);
                }
                return DynValue.NewTable(t);
            case JsonValueKind.Array:
                var arr = new Table(lua);
                int i = 1;
                foreach (var item in el.EnumerateArray())
                {
                    arr[i++] = JsonElementToDynValue(lua, item);
                }
                return DynValue.NewTable(arr);
            case JsonValueKind.String:
                return DynValue.NewString(el.GetString() ?? string.Empty);
            case JsonValueKind.Number:
                if (el.TryGetDouble(out var d)) return DynValue.NewNumber(d);
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

    private static IDictionary<string, object?> TableToDictionary(Table table) {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in table.Pairs) {
            // Convert key to string
            string key = pair.Key.Type switch {
                DataType.String => pair.Key.String,
                DataType.Number => pair.Key.Number.ToString(CultureInfo.InvariantCulture),
                _ => pair.Key.ToPrintString()
            };
            dict[key] = FromDynValue(pair.Value);
        }
        return dict;
    }

    private static object? FromDynValue(DynValue v) => v.Type switch {
        DataType.Nil or DataType.Void => null,
        DataType.Boolean => v.Boolean,
        DataType.Number => v.Number,
        DataType.String => v.String,
        DataType.Table => TableToPlainObject(v.Table),
        _ => v.ToPrintString()
    };

    private static object TableToPlainObject(Table t) {
        // Heuristic: if all keys are consecutive 1..n numbers, treat as array
        int count = 0;
        bool arrayLike = true;
        foreach (var pair in t.Pairs) {
            count++;
            if (pair.Key.Type != DataType.Number)
                arrayLike = false;
        }
        if (arrayLike) {
            var list = new List<object?>(count);
            for (int i = 1; i <= count; i++) {
                var dv = t.Get(i);
                list.Add(FromDynValue(dv));
            }
            return list;
        }
        return TableToDictionary(t);
    }

    private static List<string> TableToStringList(Table t) {
        var list = new List<string>();
        for (int i = 1; ; i++) {
            var dv = t.Get(i);
            if (dv.Type == DataType.Nil || dv.Type == DataType.Void) break;
            var s = dv.Type == DataType.String ? dv.String : dv.ToPrintString();
            list.Add(s);
        }
        return list;
    }
}
