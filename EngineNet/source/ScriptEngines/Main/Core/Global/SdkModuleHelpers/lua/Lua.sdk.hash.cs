using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua.Global;

public static partial class Sdk {

    private static void AddHashMethods(LuaWorld _LuaWorld) {
        //old
        _LuaWorld.Sdk.Table["md5"] = (string text) => {
            return ScriptEngines.Global.SdkModule.Helpers.AddHashMethods.Md5Hash(text);
        };
        _LuaWorld.Sdk.Table["sha1_file"] = (string path) => {
            return ScriptEngines.Global.SdkModule.Helpers.AddHashMethods.sha1_file(path);
        };

        // new, under sdk.Hash
        _LuaWorld.Sdk.Hash["sha1_file"] = (string path) => {
            return ScriptEngines.Global.SdkModule.Helpers.AddHashMethods.sha1_file(path);
        };

        _LuaWorld.Sdk.Hash["md5"] = (string text) => {
            return ScriptEngines.Global.SdkModule.Helpers.AddHashMethods.Md5Hash(text);
        };

    }

}