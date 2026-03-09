using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua.Global;

public static partial class Sdk {
    private static void AddArchiveOperations(LuaWorld _LuaWorld) {
        // Archive operations (using system's built-in capabilities)
        _LuaWorld.Sdk.Table["extract_archive"] = (string archivePath, string destDir) => {
            return ScriptEngines.Global.SdkModule.Helpers.AddArchiveOperations.Extract_Archive(archivePath, destDir);
        };

        _LuaWorld.Sdk.Table["create_archive"] = (string srcPath, string archivePath, string type) => {
            return ScriptEngines.Global.SdkModule.Helpers.AddArchiveOperations.Create_Archive(srcPath, archivePath, type);
        };
    }

}