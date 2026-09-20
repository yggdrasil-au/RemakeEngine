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
        _args = args is null ? System.Array.Empty<string>() : args as string[] ?? new List<string>(collection: args).ToArray();
        _gameRoot = gameRoot;
        _projectRoot = projectRoot;
    }

    //
    public async Task ExecuteAsync(Core.Abstractions.IJsonToolResolver tools, Core.Abstractions.ICommandService commandService, CancellationToken cancellationToken = default(CancellationToken)) {
        bool ok = false;
        try {
            if (!System.IO.File.Exists(path: _scriptPath)) {
                throw new System.IO.FileNotFoundException("JavaScript file not found", fileName: _scriptPath);
            }

            // ::
            // ::

            // read script code
            string code = await System.IO.File.ReadAllTextAsync(path: _scriptPath, cancellationToken: cancellationToken);
            // create new JS script environment
            Jint.Engine JsEngine = new(options: options => options.CancellationToken(cancellationToken: cancellationToken));
            // object to hold all exposed tables
            JsWorld JsWorld = new(_jsEngine: JsEngine);



            // ::
            // ::

            // Setup safer environment
            SetupSafeEnvironment.JsEnvironment(_JSWorld: JsWorld);

            // Load versions from current game module context
            Dictionary<string, string> moduleVersions = Helper.LoadModuleToolVersions(_gameRoot: _gameRoot);
            ContextualToolResolver contextualTools = new(baseResolver: tools, contextVersions: moduleVersions);

            // Expose core functions, SDK and modules
            JsAction.SetupCoreFunctions(_JSWorld: JsWorld, _tools: contextualTools, _args: _args, _gameRoot: _gameRoot, _projectRoot: _projectRoot, _scriptPath: _scriptPath);

            // Register UserData types
            //UserData.RegisterType<Shared.IO.UI.EngineSdk.PanelProgress>();
            //UserData.RegisterType<Shared.IO.UI.EngineSdk.ScriptProgress>();
            //UserData.RegisterType<Global.SqliteHandle>();

            Shared.IO.UI.EngineSdk.PrintLine($"Running js script '{_scriptPath}' with {_args.Length} args...", color: System.ConsoleColor.Cyan);
            Shared.IO.UI.EngineSdk.PrintLine($"input args: {string.Join(separator: ", ", _args)}", color: System.ConsoleColor.Gray);

            // Signal GUI that a script is active so the bottom panel can reflect activity even without progress events
            Shared.IO.UI.EngineSdk.ScriptActiveStart(scriptPath: _scriptPath);

#if DEBUG
            Shared.IO.UI.EngineSdk.PrintLine($"Running js script '{_scriptPath}' with {_args.Length} args...");
            Shared.IO.UI.EngineSdk.PrintLine($"input args: {string.Join(separator: ", ", _args)}");
#endif

            // ::
            // ::

            await System.Threading.Tasks.Task.Run(action: () => {
                JsWorld.JsScript.Execute(code: code);
            }, cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            ok = true;
        } finally {
            // Always signal end; GUI will jump to 100% and close the indicator.
            Shared.IO.UI.EngineSdk.ScriptActiveEnd(success: ok, exitCode: ok ? 0 : 1);
        }

    }

}
