using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua.Global;

public static partial class Sdk {

    private static void AddTomlHelpers(LuaWorld _LuaWorld) {
        // TOML helpers
        _LuaWorld.sdk["toml_read_file"] = (string path) => {
            object? obj = ScriptEngines.Global.SdkModule.Helpers.AddTomlHelpers.Toml_Read_File(path);
            if (obj == null) {
                return DynValue.Nil;
            }

            return Lua.Globals.Utils.ToDynValue(_LuaWorld.LuaScript, obj);
        };

        _LuaWorld.sdk["toml_write_file"] = (string path, DynValue value) => {
            object? obj = Lua.Globals.Utils.FromDynValue(value);
            ScriptEngines.Global.SdkModule.Helpers.AddTomlHelpers.Toml_Write_File(path, obj);
        };
    }
}
