
namespace EngineNet.Core.Engine;

/// <summary>
/// Core Engine class providing main functionality
/// </summary>
public sealed partial class Engine {

    /* :: :: Vars :: Start :: */

    // Services exposed to partial classes
    public Core.Services.GameLauncher GameLauncher { get; }
    public Core.Engine.Runner Runner { get; }
    public Core.Engine.EngineContext Context { get; }

    public EngineRunAll EngineRunAll { get; }

    /* :: :: Vars :: End :: */

    public Engine(
        Core.Services.GameRegistry gameRegistry,
        Core.Services.GameLauncher gameLauncher,
        Core.Services.OperationsLoader operationsLoader,
        Core.Services.CommandService commandService,
        Core.ExternalTools.JsonToolResolver toolResolver,

        Core.Services.OperationsService operationsService,
        Core.Services.GitService gitService,

        Core.EngineConfig engineConfig,

        Core.Engine.Runner runner
    ) {
        GameLauncher = gameLauncher;
        Runner = runner;

        OperationContext operationContext = new OperationContext(
            operationsService,
            operationsLoader
        );

        EngineRunAll = new EngineRunAll();

        Context = new Core.Engine.EngineContext(
            gameRegistry,
            commandService,
            toolResolver,

            gitService,

            engineConfig,

            operationContext
        );
    }

    /* :: :: */
    //
    /* :: :: */

    // run single operation (used by GUI/TUI and RunAllAsync)
    public async System.Threading.Tasks.Task<bool> RunSingleOperationAsync(
        string currentGame,
        Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games,
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers,
        EngineContext context, // ignored in favor of this.Context
        System.Threading.CancellationToken cancellationToken = default
    ) {
        return await Runner.RRunSingleOperationAsync(currentGame, games, op, promptAnswers, this.Context, cancellationToken);
    }

    // run all
    public async System.Threading.Tasks.Task<RunAllResult> RunAllAsync(
        string gameName,
        Core.ProcessRunner.OutputHandler? onOutput = null,
        Core.ProcessRunner.EventHandler? onEvent = null,
        Core.ProcessRunner.StdinProvider? stdinProvider = null,
        System.Threading.CancellationToken cancellationToken = default
    ) {
        return await EngineRunAll.RunAllAsync(gameName, this, onOutput, onEvent, stdinProvider, cancellationToken);
    }


    // Downloads a game module via Git
    public bool DownloadModule(string url) {
        return Context.GitService.CloneModule(url);
    }

    // Builds a command from operation and context
    public List<string> BuildCommand(string currentGame, Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games, IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers) {
        return Context.CommandService.BuildCommand(currentGame, games, Context.EngineConfig.Data, op, promptAnswers);
    }

    // Executes a command via ProcessRunner
    public bool ExecuteCommand(IList<string> commandParts, string title, EngineNet.Core.ProcessRunner.OutputHandler? onOutput = null, Core.ProcessRunner.EventHandler? onEvent = null, Core.ProcessRunner.StdinProvider? stdinProvider = null, IDictionary<string, object?>? envOverrides = null, CancellationToken cancellationToken = default) {
        return Context.CommandService.ExecuteCommand(commandParts, title, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdinProvider, envOverrides: envOverrides, cancellationToken: cancellationToken);
    }

    // Scans for game modules in registries
    /// <summary>
    /// returns a dictionary of game modules filtered by the provided filter
    /// with the name as the key and the GameModuleInfo as the value
    /// </summary>
    /// <param name="_Filter"></param>
    /// <returns></returns>
    public Dictionary<string, Core.Utils.GameModuleInfo> Modules(Core.Utils.ModuleFilter _Filter) {
        return Context.GameRegistry.GetModules(_Filter);
    }

    /* :: :: */
    //
    /* :: :: */

    /// <summary>
    /// Gets the root path for a game by name
    /// </summary>
    public string? GetGamePath(string name) {
        return Context.GameRegistry.GetGamePath(name);
    }

    /* :: :: */
    //

}
