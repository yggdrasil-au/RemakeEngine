using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua.Global;

public static partial class Sdk {

    private static void AddColorPrintFunctions(LuaWorld _LuaWorld) {
        // color/colour print: accepts either (color, message[, newline]) or a table { colour=?, color=?, message=?, newline=? }
        var colorPrintFunc = new CallbackFunction((ctx, args) => {
            string? color = null;
            string message = string.Empty;
            bool newline = true;
            if (args.Count >= 2 && (args[0].Type == DataType.String || args[0].Type == DataType.UserData)) {
                // color, message, [newline]
                color = args[0].ToPrintString();
                message = args[1].Type == DataType.String ? args[1].String : args[1].ToPrintString();
                if (args.Count >= 3 && args[2].Type == DataType.Boolean) {
                    newline = args[2].Boolean;
                }
            } else if (args.Count >= 1 && args[0].Type == DataType.Table) {
                Table t = args[0].Table;
                DynValue c = t.Get("color");
                if (c.IsNil()) {
                    c = t.Get("colour");
                }

                if (!c.IsNil()) {
                    color = c.Type == DataType.String ? c.String : c.ToPrintString();
                }

                DynValue m = t.Get("message");
                if (!m.IsNil()) {
                    message = m.Type == DataType.String ? m.String : m.ToPrintString();
                }

                DynValue nl = t.Get("newline");
                if (!nl.IsNil() && nl.Type == DataType.Boolean) {
                    newline = nl.Boolean;
                }
            }
            Core.UI.EngineSdk.Print(message, color, newline);
            return DynValue.Nil;
        });
        _LuaWorld.Sdk.Table["color_print"] = DynValue.NewCallback(colorPrintFunc);
        _LuaWorld.Sdk.Table["colour_print"] = DynValue.NewCallback(colorPrintFunc);
    }


}
