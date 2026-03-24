using EngineNet.Core.Data;

namespace EngineNet.Core.Engine;

/// <summary>
/// Core Engine class providing main functionality
/// </summary>
internal sealed partial class Engine : IEngineFace{

    /* :: :: Vars :: Start :: */

    // Services exposed to partial classes
    public Core.Services.GameLauncher GameLauncher { get; }
    public Core.Engine.EngineContext Context { get; }
    internal EngineRunAll EngineRunAll { get; }

    /* :: :: Vars :: End :: */

    internal Engine(
        Core.Services.GameRegistry gameRegistry,
        Core.Services.GameLauncher gameLauncher,
        Core.Services.OperationsLoader OperationsLoader,
        Core.Services.CommandService commandService,
        Core.Services.OperationsService OperationsService,
        Core.Services.GitService gitService,

        Core.ExternalTools.JsonToolResolver toolResolver,

        EngineConfig engineConfig,

        Core.Engine.Runner Runner
    ) {
        GameLauncher = gameLauncher;

        OperationContext operationContext = new OperationContext(
            OperationsService,
            OperationsLoader,
            Runner
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
        Dictionary<string, EngineNet.Core.Data.GameModuleInfo> games,
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers,
        System.Threading.CancellationToken cancellationToken = default
    ) {
        return await Context.OperationContext.Runner.RunSingleOperationAsync(currentGame, games, op, promptAnswers, this.Context, cancellationToken);
    }

    // run all
    public async System.Threading.Tasks.Task<RunAllResult> RunAllAsync(
        string gameName,
        Core.ProcessRunner.OutputHandler? onOutput = null,
        Core.ProcessRunner.EventHandler? onEvent = null,
        Core.ProcessRunner.StdinProvider? stdinProvider = null,
        System.Threading.CancellationToken cancellationToken = default
    ) {
        return await EngineRunAll.RunAllAsync(gameName, this.Context, onOutput, onEvent, stdinProvider, cancellationToken);
    }


}

public interface IEngineFace {
    public Task<bool> RunSingleOperationAsync(string currentGame, Dictionary<string, EngineNet.Core.Data.GameModuleInfo> games, IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers, CancellationToken cancellationToken = default);
    public Task<RunAllResult> RunAllAsync(string gameName, Core.ProcessRunner.OutputHandler? onOutput = null, Core.ProcessRunner.EventHandler? onEvent = null, Core.ProcessRunner.StdinProvider? stdinProvider = null, CancellationToken cancellationToken = default);

    internal Core.Services.GameLauncher GameLauncher { get; }

    internal EngineContext Context { get; }


}
