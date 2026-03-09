using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua.Global;

public static partial class Sdk {
    private static void AddConfigurationHelpers(LuaWorld _LuaWorld) {
        _LuaWorld.Sdk.Table["validate_source_dir"] = (string dir) => {
            return ScriptEngines.Global.SdkModule.Helpers.AddConfigurationHelpers.Validate_Source_Dir(dir);
        };
    }

}