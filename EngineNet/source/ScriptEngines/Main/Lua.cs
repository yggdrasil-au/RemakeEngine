// lua interpreter
using MoonSharp.Interpreter;


using System.IO;
using System.Collections.Generic;

using EngineNet.Core;
using EngineNet.Core.UI;
using EngineNet.ScriptEngines.Lua;

namespace EngineNet.ScriptEngines.Lua;

/// <summary>
/// entry point for executing a Lua script, called from EngineNet.ScriptEngines.Helpers.EmbeddedActionDispatcher
/// </summary>
public sealed class Main : Helpers.IAction {

    private readonly string _scriptPath;
    private readonly string[] _args;
    private readonly string _gameRoot;
    private readonly string _projectRoot;

    public Main(string scriptPath, IEnumerable<string>? args, string gameRoot, string projectRoot) {
        _scriptPath = scriptPath;
        _args = args is null ? System.Array.Empty<string>() : args as string[] ?? new List<string>(args).ToArray();
        _gameRoot = gameRoot;
        _projectRoot = projectRoot;
    }

    //
    public async System.Threading.Tasks.Task ExecuteAsync(Core.ExternalTools.IToolResolver tools, System.Threading.CancellationToken cancellationToken = default) {
        bool ok = false;
        try {
            if (!System.IO.File.Exists(_scriptPath)) {
                throw new System.IO.FileNotFoundException("Lua script not found", _scriptPath);
            }

            // ::
            // ::

            // read script code
            string code = await System.IO.File.ReadAllTextAsync(_scriptPath, cancellationToken);
            // create new Lua script environment with default modules, all sandboxing is done manually
            Script LuaScript = new Script(CoreModules.Preset_Default);
            // object to hold all exposed tables
            LuaWorld LuaWorld = new LuaWorld(LuaScript, _scriptPath);



            // ::
            // ::

            // Setup safer environment
            SetupSafeEnvironment.LuaEnvironment(LuaWorld);

            // Load versions from current game module context
            var moduleVersions = Helper.LoadModuleToolVersions(_gameRoot);
            var contextualTools = new ContextualToolResolver(tools, moduleVersions);

            // Expose core functions, SDK and modules
            LuaAction.SetupCoreFunctions(LuaWorld, contextualTools, _args, _gameRoot, _projectRoot, _scriptPath);

            // Register UserData types
            UserData.RegisterType<Core.UI.EngineSdk.PanelProgress>();
            UserData.RegisterType<Core.UI.EngineSdk.ScriptProgress>();
            UserData.RegisterType<Global.SqliteHandle>();

            Core.UI.EngineSdk.PrintLine(message: $"Running lua script '{_scriptPath}' with {_args.Length} args...", color: System.ConsoleColor.Cyan);
            Core.UI.EngineSdk.PrintLine(message: $"input args: {string.Join(", ", _args)}", color: System.ConsoleColor.Gray);

            // Signal GUI that a script is active so the bottom panel can reflect activity even without progress events
            Core.UI.EngineSdk.ScriptActiveStart(scriptPath: _scriptPath);

#if DEBUG
            Core.UI.EngineSdk.PrintLine($"Running lua script '{_scriptPath}' with {_args.Length} args...");
            Core.UI.EngineSdk.PrintLine($"input args: {string.Join(", ", _args)}");
#endif

            // ::
            // ::

            await System.Threading.Tasks.Task.Run(() => {
                var func = LuaWorld.LuaScript.LoadString(code);
                LuaWorld.LuaScript.Call(func, (object[])_args);
            }, cancellationToken).ConfigureAwait(false);
            ok = true;

        } catch (ScriptExitException) {
            // Script called os.exit, treat as normal exit without error
            ok = true;
        } catch (Exception ex) {
            Diagnostics.luaInternalCatch("Lua script threw an exception: " + ex);
            Core.UI.EngineSdk.PrintLine(message: $"Lua script threw an exception: {ex}", color: System.ConsoleColor.Red);
        } finally {
            // Always signal end; GUI will jump to 100% and close the indicator.
            Core.UI.EngineSdk.ScriptActiveEnd(success: ok, exitCode: ok ? 0 : 1);
        }
    }

}
