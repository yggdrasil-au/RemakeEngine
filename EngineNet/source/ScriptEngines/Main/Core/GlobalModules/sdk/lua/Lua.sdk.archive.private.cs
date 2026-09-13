namespace EngineNet.ScriptEngines.Lua.Global;

internal static partial class Sdk {
    private static void AddArchiveOperations(LuaWorld _LuaWorld) {
        // Archive operations (using system's built-in capabilities)
        _LuaWorld.Sdk.Table[key: "extract_archive"] = (string archivePath, string destDir) => {
            return ScriptEngines.Global.SdkModule.Helpers.AddArchiveOperations.Extract_Archive(archivePath: archivePath, destDir: destDir);
        };

        _LuaWorld.Sdk.Table[key: "create_archive"] = (string srcPath, string archivePath, string type) => {
            return ScriptEngines.Global.SdkModule.Helpers.AddArchiveOperations.Create_Archive(srcPath: srcPath, archivePath: archivePath, type: type);
        };
    }

}
