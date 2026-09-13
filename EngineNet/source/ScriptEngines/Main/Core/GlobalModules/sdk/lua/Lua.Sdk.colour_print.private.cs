using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua.Global;

internal static partial class Sdk {

    private static void AddColorPrintFunctions(LuaWorld _LuaWorld) {
        // color/colour print: accepts either (color, message[, newline]) or a table { colour=?, color=?, message=?, newline=? }
        var colorPrintFunc = new CallbackFunction(callBack: (ctx, args) => {
            string? color = null;
            string message = string.Empty;
            bool newline = true;
            if (args.Count >= 2 && (args[index: 0].Type == DataType.String || args[index: 0].Type == DataType.UserData)) {
                // color, message, [newline]
                color = args[index: 0].ToPrintString();
                message = args[index: 1].Type == DataType.String ? args[index: 1].String : args[index: 1].ToPrintString();
                if (args.Count >= 3 && args[index: 2].Type == DataType.Boolean) {
                    newline = args[index: 2].Boolean;
                }
            } else if (args.Count >= 1 && args[index: 0].Type == DataType.Table) {
                Table t = args[index: 0].Table;
                DynValue c = t.Get(key: "color");
                if (c.IsNil()) {
                    c = t.Get(key: "colour");
                }

                if (!c.IsNil()) {
                    color = c.Type == DataType.String ? c.String : c.ToPrintString();
                }

                DynValue m = t.Get(key: "message");
                if (!m.IsNil()) {
                    message = m.Type == DataType.String ? m.String : m.ToPrintString();
                }

                DynValue nl = t.Get(key: "newline");
                if (!nl.IsNil() && nl.Type == DataType.Boolean) {
                    newline = nl.Boolean;
                }
            }
            Shared.IO.UI.EngineSdk.Print(message, color: color, newline: newline);
            return DynValue.Nil;
        });
        _LuaWorld.Sdk.Table[key: "color_print"] = DynValue.NewCallback(function: colorPrintFunc);
        _LuaWorld.Sdk.Table[key: "colour_print"] = DynValue.NewCallback(function: colorPrintFunc);
    }


}
