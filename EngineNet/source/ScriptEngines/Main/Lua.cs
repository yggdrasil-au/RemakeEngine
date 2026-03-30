// lua interpreter
using MoonSharp.Interpreter;

using EngineNet.Core;

namespace EngineNet.ScriptEngines.Lua;

/// <summary>
/// entry point for executing a Lua script, called from EngineNet.ScriptEngines.Helpers.EmbeddedActionDispatcher
/// </summary>
internal sealed class Main : IAction {

    private readonly string _scriptPath;
    private readonly string[] _args;
    private readonly string _gameRoot;
    private readonly string _projectRoot;

    internal Main(string scriptPath, IEnumerable<string>? args, string gameRoot, string projectRoot) {
        this._scriptPath = scriptPath;
        this._args = args is null ? System.Array.Empty<string>() : args as string[] ?? new List<string>(args).ToArray();
        this._gameRoot = gameRoot;
        this._projectRoot = projectRoot;
    }

    //
    public async Task ExecuteAsync(Core.ExternalTools.JsonToolResolver tools, Core.Services.CommandService commandService, CancellationToken cancellationToken = default) {
        bool ok = false;
        int exitCode = 0;
        System.Exception? executionError = null;
        LuaWorld? LuaWorld = null;
        try {
            if (!System.IO.File.Exists(this._scriptPath)) {
                throw new System.IO.FileNotFoundException("Lua script not found", this._scriptPath);
            }

            // ::
            // ::

            // read script code
            string code = await System.IO.File.ReadAllTextAsync(this._scriptPath, cancellationToken);
            // create new Lua script environment with default modules, all sandboxing is done manually
            Script LuaScript = new Script(CoreModules.Preset_Default);
            // object to hold all exposed tables
            LuaWorld = new LuaWorld(LuaScript, this._scriptPath);



            // ::
            // ::

            // Setup safer environment
            SetupEnvironment.LuaEnvironment(LuaWorld);

            // Load versions from current game module context
            Dictionary<string,string> moduleVersions = Helper.LoadModuleToolVersions(_gameRoot);
            var contextualTools = new ContextualToolResolver(tools, moduleVersions);

            // Expose core functions, SDK and modules
            LuaAction.CreateGlobals(LuaWorld, contextualTools, commandService, this._args, this._gameRoot, this._projectRoot, this._scriptPath);

            // Register UserData types
            UserData.RegisterType<Core.UI.EngineSdk.PanelProgress>();
            UserData.RegisterType<Core.UI.EngineSdk.ScriptProgress>();
            UserData.RegisterType<Global.SqliteHandle>();

            // Signal GUI that a script is active so the bottom panel can reflect activity even without progress events
            Core.UI.EngineSdk.ScriptActiveStart(scriptPath: this._scriptPath);

#if DEBUG
            Core.UI.EngineSdk.PrintLine($"Running lua script '{this._scriptPath}' with {this._args.Length} args...");
            Core.UI.EngineSdk.PrintLine($"input args: {string.Join(", ", this._args)}");
#endif

            // ::
            // ::

            // Create a fresh object array specifically for this call
            object[] argsForLua = new object[this._args.Length];
            Array.Copy(this._args, argsForLua, this._args.Length);

            await System.Threading.Tasks.Task.Run(() => {
                LuaWorld.LuaScript.Call(LuaWorld.LuaScript.LoadString(code), argsForLua);
            }, cancellationToken).ConfigureAwait(false);
            ok = true;
            exitCode = 0;

        } catch (ScriptExitException exitEx) {
            // Script called os.exit, treat as normal exit without error
            exitCode = exitEx.ExitCode;
            ok = exitEx.ExitCode == 0;
            if (!ok) {
                executionError = new System.InvalidOperationException($"Lua script exited with non-zero code {exitCode}.");
            }
        } catch (Exception ex) {
            Diagnostics.LuaInternalCatch("Lua script threw an exception: " + ex);
            Core.UI.EngineSdk.PrintLine(message: $"Lua script threw an exception: {ex}", color: System.ConsoleColor.Red);
            exitCode = 1;
            executionError = ex;
        } finally {
            LuaWorld?.DisposeOpenDisposables();

            Core.UI.EngineSdk.ScriptActiveEnd(success: ok, exitCode: exitCode);
        }

        if (!ok) {
            throw new System.InvalidOperationException(
                message: $"Lua script failed with exit code {exitCode}: '{this._scriptPath}'",
                innerException: executionError
            );
        }
    }

}
