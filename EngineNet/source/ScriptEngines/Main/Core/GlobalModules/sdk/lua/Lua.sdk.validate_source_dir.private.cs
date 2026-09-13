namespace EngineNet.ScriptEngines.Lua.Global;

internal static partial class Sdk {
    private static void AddConfigurationHelpers(LuaWorld _LuaWorld) {
        _LuaWorld.Sdk.Table[key: "validate_source_dir"] = (string dir) => {
            return ScriptEngines.Global.SdkModule.Helpers.AddConfigurationHelpers.Validate_Source_Dir(dir: dir);
        };
    }

}
