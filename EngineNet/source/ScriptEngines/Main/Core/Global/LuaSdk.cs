using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua.Global;

/// <summary>
/// SDK module providing file operations, archive handling, and system utilities for Lua scripts.
/// </summary>
public static partial class Sdk {
    public static Table CreateSdkModule(LuaWorld _LuaWorld, Core.ExternalTools.IToolResolver tools) {
        // Color/colour print functionality
        AddColorPrintFunctions(_LuaWorld);

        // Configuration helpers
        AddConfigurationHelpers(_LuaWorld);

        // File system operations
        AddFileSystemOperations(_LuaWorld);

        // Archive operations
        AddArchiveOperations(_LuaWorld);

        // TOML helpers
        AddTomlHelpers(_LuaWorld);

        // Register JSON encoding/decoding functions in sdk.text.json
        JsonModules(_LuaWorld);

        // Process execution helpers
        AddProcessExecution(_LuaWorld, tools);

        // Hashing functions
        AddHashMethods(_LuaWorld);

        _LuaWorld.Sdk.Table["sleep"] = (double seconds) => {
            ScriptEngines.Global.SdkModule.Helpers.Sleep(seconds);
        };

        // Expose CPU count both globally and as a member of the sdk table
        var cpuCount = DynValue.NewNumber(System.Environment.ProcessorCount);
        _LuaWorld.LuaScript.Globals["cpu_count"] = cpuCount;
        _LuaWorld.Sdk.Table["cpu_count"] = cpuCount;

        return _LuaWorld.Sdk.Table;
    }

}
