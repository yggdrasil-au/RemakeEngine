using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua.Global;

public static partial class Sdk {
    /// <summary>
    /// register JSON encoding/decoding functions in sdk.text.json
    /// </summary>
    /// <param name="_LuaWorld"></param>
    /// <exception cref="ScriptRuntimeException"></exception>
    private static void JsonModules(LuaWorld _LuaWorld) {
        // Create the table structure: sdk.text.json
        Table textTable = new Table(_LuaWorld.LuaScript);
        Table jsonTable = new Table(_LuaWorld.LuaScript);

        _LuaWorld.sdk["text"] = textTable;
        textTable["json"] = jsonTable;

        // sdk.text.json.encode(value, opts)
        jsonTable["encode"] = (System.Func<DynValue, DynValue, string>)((val, opts) => {
            bool indent = false;
            if (opts.Type == DataType.Table) {
                DynValue indentVal = opts.Table.Get("indent");
                indent = indentVal.Type == DataType.Boolean && indentVal.Boolean;
            }
            object? obj = Lua.Globals.Utils.FromDynValue(val);
            var jsonOpts = new System.Text.Json.JsonSerializerOptions { WriteIndented = indent };
            return System.Text.Json.JsonSerializer.Serialize(obj, jsonOpts);
        });

        // sdk.text.json.decode(string)
        jsonTable["decode"] = (System.Func<string, DynValue>)((json) => {
            try {
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
                return Lua.Globals.Utils.JsonElementToDynValue(_LuaWorld.LuaScript, doc.RootElement);
            } catch (Exception ex) {
                Core.Diagnostics.luaInternalCatch("sdk.text.json.decode failed: " + ex);
                return DynValue.Nil;
            }
        });
    }
}
