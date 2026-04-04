// js interpreter
using Jint;

namespace EngineNet.ScriptEngines.Js;

/// <summary>
/// entry point for executing a JS script, called from EngineNet.ScriptEngines.Helpers.EmbeddedActionDispatcher
/// </summary>
internal sealed class Main : IScriptAction {

    private readonly string _scriptPath;
    private readonly string[] _args;
    private readonly string _gameRoot;
    private readonly string _projectRoot;

    internal Main(string scriptPath, IEnumerable<string>? args, string gameRoot, string projectRoot) {
        _scriptPath = scriptPath;
        _args = args is null ? System.Array.Empty<string>() : args as string[] ?? new List<string>(args).ToArray();
        _gameRoot = gameRoot;
        _projectRoot = projectRoot;
    }

    //
    public async Task ExecuteAsync(Core.ExternalTools.JsonToolResolver tools, Core.Services.CommandService commandService, CancellationToken cancellationToken = default(CancellationToken)) {
        bool ok = false;
        try {
            if (!System.IO.File.Exists(_scriptPath)) {
                throw new System.IO.FileNotFoundException("JavaScript file not found", _scriptPath);
            }

            // ::
            // ::

            // read script code
            string code = await System.IO.File.ReadAllTextAsync(_scriptPath, cancellationToken);
            // create new JS script environment
            Jint.Engine JsEngine = new Jint.Engine(options => options.CancellationToken(cancellationToken));
            // object to hold all exposed tables
            JsWorld JsWorld = new JsWorld(JsEngine);



            // ::
            // ::

            // Setup safer environment
            SetupSafeEnvironment.JsEnvironment(JsWorld);

            // Load versions from current game module context
            var moduleVersions = Helper.LoadModuleToolVersions(_gameRoot);
            var contextualTools = new ContextualToolResolver(tools, moduleVersions);

            // Expose core functions, SDK and modules
            JsAction.SetupCoreFunctions(JsWorld, contextualTools, _args, _gameRoot, _projectRoot, _scriptPath);

            // Register UserData types
            //UserData.RegisterType<Shared.IO.UI.EngineSdk.PanelProgress>();
            //UserData.RegisterType<Shared.IO.UI.EngineSdk.ScriptProgress>();
            //UserData.RegisterType<Global.SqliteHandle>();

            Shared.IO.UI.EngineSdk.PrintLine(message: $"Running js script '{_scriptPath}' with {_args.Length} args...", color: System.ConsoleColor.Cyan);
            Shared.IO.UI.EngineSdk.PrintLine(message: $"input args: {string.Join(", ", _args)}", color: System.ConsoleColor.Gray);

            // Signal GUI that a script is active so the bottom panel can reflect activity even without progress events
            Shared.IO.UI.EngineSdk.ScriptActiveStart(scriptPath: _scriptPath);

#if DEBUG
            Shared.IO.UI.EngineSdk.PrintLine($"Running js script '{_scriptPath}' with {_args.Length} args...");
            Shared.IO.UI.EngineSdk.PrintLine($"input args: {string.Join(", ", _args)}");
#endif

            // ::
            // ::

            await System.Threading.Tasks.Task.Run(() => {
                JsWorld.JsScript.Execute(code);
            }, cancellationToken).ConfigureAwait(false);
            ok = true;
        } finally {
            // Always signal end; GUI will jump to 100% and close the indicator.
            Shared.IO.UI.EngineSdk.ScriptActiveEnd(success: ok, exitCode: ok ? 0 : 1);
        }

    }

}
