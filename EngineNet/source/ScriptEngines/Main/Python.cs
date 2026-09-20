// python interpreter

namespace EngineNet.ScriptEngines.Python;

using Microsoft.Scripting.Hosting;

/// <summary>
/// entry point for executing a Python script, called from EngineNet.ScriptEngines.Helpers.EmbeddedActionDispatcher
/// </summary>
internal sealed class Main : IScriptAction {

    private readonly string _scriptPath;
    private readonly string[] _args;
    private readonly string _gameRoot;
    private readonly string _projectRoot;

    internal Main(string scriptPath, System.Collections.Generic.IEnumerable<string>? args, string gameRoot, string projectRoot) {
        _scriptPath = scriptPath;
        _args = args is null ? System.Array.Empty<string>() : args as string[] ?? new System.Collections.Generic.List<string>(collection: args).ToArray();
        _gameRoot = gameRoot;
        _projectRoot = projectRoot;
    }

    //
    public async Task ExecuteAsync(Core.ExternalTools.JsonToolResolver tools, Core.Services.CommandService.CommandService commandService, CancellationToken cancellationToken = default(CancellationToken)) {
        bool ok = false;
        try {
            if (!System.IO.File.Exists(path: _scriptPath)) {
                throw new System.IO.FileNotFoundException("Python script file not found", fileName: _scriptPath);
            }

            // ::
            // ::

            // create new Python script environment
            Microsoft.Scripting.Hosting.ScriptEngine PythonEngine = IronPython.Hosting.Python.CreateEngine();
            // create a scope for variables, functions, and imported modules; this is separate from the engine to allow multiple executions with different scopes if desired
            ScriptScope scope = PythonEngine.CreateScope();
            // object to hold all exposed tables
            PyWorld PyWorld = new(engine: PythonEngine, scope: scope);



            // ::
            // ::

            // Setup safer environment
            SetupSafeEnvironment.PyEnvironment(_PyWorld: PyWorld);

            // Load versions from current game module context
            Dictionary<string, string> moduleVersions = Helper.LoadModuleToolVersions(_gameRoot: _gameRoot);
            ContextualToolResolver contextualTools = new(baseResolver: tools, contextVersions: moduleVersions);

            // Expose core functions, SDK and modules
            PyAction.SetupCoreFunctions(world: PyWorld, tools: contextualTools, args: _args, gameRoot: _gameRoot, projectRoot: _projectRoot, scriptPath: _scriptPath);

            // Register UserData types
            //UserData.RegisterType<Shared.IO.UI.EngineSdk.PanelProgress>();
            //UserData.RegisterType<Shared.IO.UI.EngineSdk.ScriptProgress>();
            //UserData.RegisterType<Global.SqliteHandle>();

            Shared.IO.UI.EngineSdk.PrintLine($"Running python script '{_scriptPath}' with {_args.Length} args...", color: System.ConsoleColor.Cyan);
            Shared.IO.UI.EngineSdk.PrintLine($"input args: {string.Join(separator: ", ", _args)}", color: System.ConsoleColor.Gray);

            // Signal GUI that a script is active so the bottom panel can reflect activity even without progress events
            Shared.IO.UI.EngineSdk.ScriptActiveStart(scriptPath: _scriptPath);

#if DEBUG
            Shared.IO.UI.EngineSdk.PrintLine($"Running python script '{_scriptPath}' with {_args.Length} args...");
            Shared.IO.UI.EngineSdk.PrintLine($"input args: {string.Join(separator: ", ", _args)}");
#endif

            // ::
            // ::

            await System.Threading.Tasks.Task.Run(action: () => {
                PythonEngine.ExecuteFile(path: _scriptPath, scope: scope);
            }, cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            ok = true;
        } finally {
            // Always signal end; GUI will jump to 100% and close the indicator.
            Shared.IO.UI.EngineSdk.ScriptActiveEnd(success: ok, exitCode: ok ? 0 : 1);
        }
    }

}
