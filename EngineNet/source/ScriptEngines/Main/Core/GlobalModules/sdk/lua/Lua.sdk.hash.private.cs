namespace EngineNet.ScriptEngines.Lua.Global;

internal static partial class Sdk {

    private static void AddHashMethods(LuaWorld _LuaWorld) {
        //old
        _LuaWorld.Sdk.Table[key: "md5"] = (string text) => {
            return ScriptEngines.Global.SdkModule.Helpers.AddHashMethods.Md5Hash(text: text);
        };
        _LuaWorld.Sdk.Table[key: "sha1_file"] = (string path) => {
            return ScriptEngines.Global.SdkModule.Helpers.AddHashMethods.sha1_file(path: path);
        };

        // new, under sdk.Hash
        _LuaWorld.Sdk.Hash[key: "sha1_file"] = (string path) => {
            return ScriptEngines.Global.SdkModule.Helpers.AddHashMethods.sha1_file(path: path);
        };

        _LuaWorld.Sdk.Hash[key: "md5"] = (string text) => {
            return ScriptEngines.Global.SdkModule.Helpers.AddHashMethods.Md5Hash(text: text);
        };

    }

}
