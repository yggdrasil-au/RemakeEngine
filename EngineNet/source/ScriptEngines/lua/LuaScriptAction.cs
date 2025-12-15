using MoonSharp.Interpreter;

using System.Collections.Generic;

namespace EngineNet.ScriptEngines.lua;

/// <summary>
/// Executes a Lua script using the embedded MoonSharp interpreter.
///
/// Exposed Lua globals for module authors:
/// - tool(name): resolve a registered tool to an absolute path.
/// - argv: string array of arguments passed to the script.
/// - emit(event, data?): send a structured engine event. `data` is an optional table.
///   Example: emit("warning", { message = "Heads up" })
/// - warn(message): shorthand for emit("warning", { message = message })
/// - error(message): shorthand for emit("error", { message = message })
/// - prompt(message, id?, secret?): prompts the user; returns the entered string.
///   Example: local answer = prompt("Continue? (y/n)", "confirm1", false)
/// - progress(total, id?, label?): creates a progress handle; returns an object with Update(inc?)
///   Example: local p = progress(100, "extract", "Extracting files"); p:Update(5)
///
/// All helpers wrap RemakeEngine.Utils.EngineSdk for consistent engine integration.
/// </summary>
internal sealed partial class LuaScriptAction : Helpers.IAction {

    //internal LuaScriptAction(string scriptPath) : this(scriptPath, System.Array.Empty<string>()) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="LuaScriptAction"/> class.
    /// </summary>
    /// <param name="scriptPath">The path to the Lua script file.</param>
    /// <param name="args">The arguments to pass to the Lua script.</param>
    /// <param name="gameRoot">The root directory of the current game module.</param>
    /// <param name="projectRoot">The root directory of the engine project.</param>
    internal LuaScriptAction(string scriptPath, IEnumerable<string>? args, string gameRoot, string projectRoot) {
        _scriptPath = scriptPath;
        _args = args is null ? System.Array.Empty<string>() : args as string[] ?? new List<string>(args).ToArray();
        _gameRoot = gameRoot;
        _projectRoot = projectRoot;
    }

    /// <summary>
    /// Executes the Lua script asynchronously.
    /// </summary>
    /// <param name="tools">The tool resolver to resolve registered tools.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="System.IO.FileNotFoundException">Thrown if the Lua script file is not found.</exception>
    public async System.Threading.Tasks.Task ExecuteAsync(Core.Tools.IToolResolver tools, System.Threading.CancellationToken cancellationToken = default) {
        if (!System.IO.File.Exists(_scriptPath)) {
            throw new System.IO.FileNotFoundException("Lua script not found", _scriptPath);
        }

        // read script code
        string code = await System.IO.File.ReadAllTextAsync(_scriptPath, cancellationToken);
        // create new Lua script environment with default modules, all sandboxing is done manually
        Script luaScript = new Script(CoreModules.Preset_Default);
        // object to hold all exposed tables
        LuaWorld LuaEnvObj = new LuaWorld(luaScript);

        // Setup safer Lua environment, overrides IO table and OS table for sandboxing
        SetupSafeLuaEnvironment(LuaEnvObj);

        // Expose core functions, SDK and modules and any shims
        SetupCoreFunctions(LuaEnvObj, tools);

        // Register UserData types
        UserData.RegisterType<Core.Utils.EngineSdk.PanelProgress>();
        UserData.RegisterType<Core.Utils.EngineSdk.ScriptProgress>();
        UserData.RegisterType<LuaModules.SqliteHandle>();

        // Setup SDK and modules
        //lua.Globals["sdk"] = LuaModules.LuaSdkModule.CreateSdkModule(lua, tools);
        //lua.Globals["sqlite"] = LuaModules.LuaSqliteModule.CreateSqliteModule(lua);

        // Preload minimal shims for LuaFileSystem (lfs) and dkjson used by game modules
        //LuaModules.LuaShimModules.PreloadShimModules(lua, _scriptPath);

        Core.Utils.EngineSdk.PrintLine(message: $"Running lua script '{_scriptPath}' with {_args.Length} args...", color: System.ConsoleColor.Cyan);
        Core.Utils.EngineSdk.PrintLine(message: $"input args: {string.Join(", ", _args)}", color: System.ConsoleColor.Gray);

        // Signal GUI that a script is active so the bottom panel can reflect activity even without progress events
        Core.Utils.EngineSdk.ScriptActiveStart(scriptPath: _scriptPath);

        bool ok = false;
        try {
            await System.Threading.Tasks.Task.Run(() => LuaEnvObj.LuaScript.DoString(code), cancellationToken).ConfigureAwait(false);
            ok = true;
        } finally {
            // Always signal end; GUI will jump to 100% and close the indicator.
            Core.Utils.EngineSdk.ScriptActiveEnd(success: ok, exitCode: ok ? 0 : 1);
        }
    }

}
