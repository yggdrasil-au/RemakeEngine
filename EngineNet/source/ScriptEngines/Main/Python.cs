// python interpreter
using IronPython.Hosting;


using System.IO;
using System.Collections.Generic;

using EngineNet.Core;
using EngineNet.Core.UI;
using EngineNet.ScriptEngines.Python;

namespace EngineNet.ScriptEngines.Python;

/// <summary>
/// entry point for executing a Python script, called from EngineNet.ScriptEngines.Helpers.EmbeddedActionDispatcher
/// </summary>
internal sealed class Main : Helpers.IAction {

    private readonly string _scriptPath;
    private readonly string[] _args;
    private readonly string _gameRoot;
    private readonly string _projectRoot;

    internal Main(string scriptPath, System.Collections.Generic.IEnumerable<string>? args, string gameRoot, string projectRoot) {
        _scriptPath = scriptPath;
        _args = args is null ? System.Array.Empty<string>() : args as string[] ?? new System.Collections.Generic.List<string>(args).ToArray();
        _gameRoot = gameRoot;
        _projectRoot = projectRoot;
    }

    //
    public async System.Threading.Tasks.Task ExecuteAsync(Core.ExternalTools.IToolResolver tools, System.Threading.CancellationToken cancellationToken = default) {
        bool ok = false;
        try {
            if (!System.IO.File.Exists(_scriptPath)) {
                throw new System.IO.FileNotFoundException("Python script file not found", _scriptPath);
            }

            // ::
            // ::

            // create new Python script environment
            Microsoft.Scripting.Hosting.ScriptEngine PythonEngine = IronPython.Hosting.Python.CreateEngine();
            // create a scope for variables, functions, and imported modules; this is separate from the engine to allow multiple executions with different scopes if desired
            var scope = PythonEngine.CreateScope();
            // object to hold all exposed tables
            PyWorld PyWorld = new PyWorld(PythonEngine, scope);



            // ::
            // ::

            // Setup safer environment
            SetupSafeEnvironment.PyEnvironment(PyWorld);

            // Load versions from current game module context
            var moduleVersions = Helper.LoadModuleToolVersions(_gameRoot);
            var contextualTools = new ContextualToolResolver(tools, moduleVersions);

            // Expose core functions, SDK and modules
            PyAction.SetupCoreFunctions(PyWorld, contextualTools, _args, _gameRoot, _projectRoot, _scriptPath);

            // Register UserData types
            //UserData.RegisterType<Core.UI.EngineSdk.PanelProgress>();
            //UserData.RegisterType<Core.UI.EngineSdk.ScriptProgress>();
            //UserData.RegisterType<Global.SqliteHandle>();

            Core.UI.EngineSdk.PrintLine(message: $"Running python script '{_scriptPath}' with {_args.Length} args...", color: System.ConsoleColor.Cyan);
            Core.UI.EngineSdk.PrintLine(message: $"input args: {string.Join(", ", _args)}", color: System.ConsoleColor.Gray);

            // Signal GUI that a script is active so the bottom panel can reflect activity even without progress events
            Core.UI.EngineSdk.ScriptActiveStart(scriptPath: _scriptPath);

#if DEBUG
            Core.UI.EngineSdk.PrintLine($"Running python script '{_scriptPath}' with {_args.Length} args...");
            Core.UI.EngineSdk.PrintLine($"input args: {string.Join(", ", _args)}");
#endif

            // ::
            // ::

            await System.Threading.Tasks.Task.Run(() => {
                PythonEngine.ExecuteFile(_scriptPath, scope);
            }, cancellationToken).ConfigureAwait(false);
            ok = true;
        } finally {
            // Always signal end; GUI will jump to 100% and close the indicator.
            Core.UI.EngineSdk.ScriptActiveEnd(success: ok, exitCode: ok ? 0 : 1);
        }
    }

}
