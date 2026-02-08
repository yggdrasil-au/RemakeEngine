using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua.Global;

public static partial class Sdk {

    private static void AddTomlHelpers(LuaWorld _LuaWorld) {

        // Get/Create the hierarchy: sdk -> text -> toml


        // Assign to the primary location: sdk.text.toml.*
        _LuaWorld.Sdk.Text.Toml["read_file"] = (string path) => {
            object? obj = ScriptEngines.Global.SdkModule.Helpers.AddTomlHelpers.Toml_Read_File(path);
            return obj == null ? DynValue.Nil : Lua.Globals.Utils.ToDynValue(_LuaWorld.LuaScript, obj);
        };
        _LuaWorld.Sdk.Text.Toml["write_file"] = (string path, DynValue value) => {
            object? obj = Lua.Globals.Utils.FromDynValue(value);
            ScriptEngines.Global.SdkModule.Helpers.AddTomlHelpers.Toml_Write_File(path, obj);
        };

        // Assign aliases to the root: sdk.toml_*
        _LuaWorld.Sdk.Table["toml_read_file"] = _LuaWorld.Sdk.Text.Toml["read_file"];
        _LuaWorld.Sdk.Table["toml_write_file"] = _LuaWorld.Sdk.Text.Toml["write_file"];
    }
}
