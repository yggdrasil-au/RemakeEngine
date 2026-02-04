using MoonSharp.Interpreter;

using System.Collections.Generic;

namespace EngineNet.ScriptEngines.lua;

internal sealed partial class LuaScriptAction : Helpers.IAction {

    internal LuaScriptAction(string scriptPath, IEnumerable<string>? args, string gameRoot, string projectRoot) {
        _scriptPath = scriptPath;
        _args = args is null ? System.Array.Empty<string>() : args as string[] ?? new List<string>(args).ToArray();
        _gameRoot = gameRoot;
        _projectRoot = projectRoot;
    }

    /// <summary>
    /// Executes the Lua script.
    /// </summary>
    /// <param name="tools">The tool resolver to resolve registered tools.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="System.IO.FileNotFoundException">Thrown if the Lua script file is not found.</exception>
    public async System.Threading.Tasks.Task ExecuteAsync(Core.ExternalTools.IToolResolver tools, System.Threading.CancellationToken cancellationToken = default) {
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

        // Load versions from current game module context
        var moduleVersions = LoadModuleToolVersions();
        var contextualTools = new ContextualToolResolver(tools, moduleVersions);

        // Expose core functions, SDK and modules and any shims
        SetupCoreFunctions(LuaEnvObj, contextualTools);

        // Register UserData types
        UserData.RegisterType<Core.UI.EngineSdk.PanelProgress>();
        UserData.RegisterType<Core.UI.EngineSdk.ScriptProgress>();
        UserData.RegisterType<LuaModules.SqliteHandle>();

        // Setup SDK and modules
        //lua.Globals["sdk"] = LuaModules.LuaSdkModule.CreateSdkModule(lua, tools);
        //lua.Globals["sqlite"] = LuaModules.LuaSqliteModule.CreateSqliteModule(lua);

        // Preload minimal shims for LuaFileSystem (lfs) and dkjson used by game modules
        //LuaModules.LuaShimModules.PreloadShimModules(lua, _scriptPath);

        Core.UI.EngineSdk.PrintLine(message: $"Running lua script '{_scriptPath}' with {_args.Length} args...", color: System.ConsoleColor.Cyan);
        Core.UI.EngineSdk.PrintLine(message: $"input args: {string.Join(", ", _args)}", color: System.ConsoleColor.Gray);

        // Signal GUI that a script is active so the bottom panel can reflect activity even without progress events
        Core.UI.EngineSdk.ScriptActiveStart(scriptPath: _scriptPath);

        bool ok = false;
        try {
            await System.Threading.Tasks.Task.Run(() => LuaEnvObj.LuaScript.DoString(code), cancellationToken).ConfigureAwait(false);
            ok = true;
        } finally {
            // Always signal end; GUI will jump to 100% and close the indicator.
            Core.UI.EngineSdk.ScriptActiveEnd(success: ok, exitCode: ok ? 0 : 1);
        }
    }

}
