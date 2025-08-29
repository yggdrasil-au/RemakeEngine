using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MoonSharp.Interpreter;
using RemakeEngine.Tools;
using RemakeEngine.Utils;
using System.Globalization;

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

        await Task.Run(() => lua.DoString(code), cancellationToken);
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
}
