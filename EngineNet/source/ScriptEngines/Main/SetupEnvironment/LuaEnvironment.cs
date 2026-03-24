using System.Text;
using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua;

// custom exception to signal script exit without treating it as an error
internal class ScriptExitException(int exitCode = 0) : Exception {
    internal int ExitCode { get; } = exitCode;
}

internal static partial class SetupEnvironment {

    /// <summary>
    /// Disallowed environment variables to prevent information disclosure from Lua.
    /// </summary>
    private static readonly HashSet<string> DisallowedEnv = new(StringComparer.OrdinalIgnoreCase) {
        "TMP", "TEMP", "Path", "OneDrive", "ComSpec", "DriverData", "PSModulePath", "USERNAME", "windir"
    };

    internal static void LuaEnvironment(LuaWorld _LuaWorld) {

        _LuaWorld.LuaScript.Globals["loadfile"] = DynValue.Nil;     // Remove ability to load arbitrary files
        _LuaWorld.LuaScript.Globals["dofile"] = DynValue.Nil;       // Remove ability to execute arbitrary files

        // Remove entire io table
        if (_LuaWorld.LuaScript.Globals.Get("io").Type == DataType.Table) {
            _LuaWorld.LuaScript.Globals["io"] = DynValue.Nil;
        }
        // Remove entire os table
        if (_LuaWorld.LuaScript.Globals.Get("os").Type == DataType.Table) {
            _LuaWorld.LuaScript.Globals["os"] = DynValue.Nil;
        }

        CreateOsTable(_LuaWorld);

        CreateIoTable(_LuaWorld);
    }


}
