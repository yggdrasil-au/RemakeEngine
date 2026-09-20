using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua.Global;

/// <summary>
/// SDK module providing file operations, archive handling, and system utilities for Lua scripts.
/// </summary>
internal static partial class Sdk {
    internal static void CreateSdkModule(LuaWorld _LuaWorld, Core.Abstractions.IJsonToolResolver tools, Core.Abstractions.ICommandService commandService) {
        // Color/colour print functionality
        AddColorPrintFunctions(_LuaWorld: _LuaWorld);

        // Configuration helpers
        AddConfigurationHelpers(_LuaWorld: _LuaWorld);

        // File system operations
        AddFileSystemOperations(_LuaWorld: _LuaWorld);

        // Archive operations
        AddArchiveOperations(_LuaWorld: _LuaWorld);

        // Register TOML encoding/decoding functions in sdk.text.toml
        AddTomlHelpers(_LuaWorld: _LuaWorld);

        // Register JSON encoding/decoding functions in sdk.text.json
        JsonModules(_LuaWorld: _LuaWorld);

        // Register YAML encoding/decoding and file helpers in sdk.text.yaml
        YamlModules(_LuaWorld: _LuaWorld);

        // Process execution helpers
        AddProcessExecution(_LuaWorld: _LuaWorld, tools: tools, cs: commandService);

        // Hashing functions
        AddHashMethods(_LuaWorld: _LuaWorld);

        _LuaWorld.Sdk.Table[key: "sleep"] = (double seconds) => {
            ScriptEngines.Global.SdkModule.Helpers.Sleep(seconds: seconds);
        };

        // Expose CPU count both globally and as a member of the sdk table
        DynValue cpuCount = DynValue.NewNumber(num: System.Environment.ProcessorCount);
        _LuaWorld.LuaScript.Globals[key: "cpu_count"] = cpuCount;
        _LuaWorld.Sdk.Table[key: "cpu_count"] = cpuCount;

        // return _LuaWorld.Sdk.Table;
        _LuaWorld.LuaScript.Globals[key: "sdk"] = _LuaWorld.Sdk.Table;
    }
}
