
namespace EngineNet.Core.Engine;

/// <summary>
/// Core Engine class providing main functionality
/// </summary>
public sealed class Engine : IEngineFace{

    /* :: :: Vars :: Start :: */

    // Services exposed to partial classes
    public Core.Services.GameLauncher GameLauncher { get; }
    public Core.Abstractions.IEngineContext Context { get; }

    public Core.Abstractions.IOperationContext OperationContext { get; }

    /* :: :: Vars :: End :: */

    public Engine(
        Core.Abstractions.IGameRegistry gameRegistry,
        Core.Services.GameLauncher gameLauncher,
        Core.Services.OperationsService.OperationsLoader OperationsLoader,
        Core.Services.CommandService.CommandService commandService,
        Core.Services.OperationsService.OperationsService OperationsService,
        Core.ExternalTools.JsonToolResolver toolResolver,
        Core.Data.EngineConfig engineConfig,
        Core.Abstractions.ISingle Runner
    ) {
        this.GameLauncher = gameLauncher;
        this.Context = new Core.Abstractions.IEngineContext(
            GameRegistry: gameRegistry,
            CommandService: commandService,
            ToolResolver: toolResolver,
            EngineConfig: engineConfig
        );
        this.OperationContext = new Core.Abstractions.IOperationContext(
            OperationsService: OperationsService,
            OperationsLoader: OperationsLoader,
            Single: Runner
        );
    }

    /* :: :: */
    //
    /* :: :: */

    // run single operation (used by GUI/TUI and RunAllAsync)
    public async System.Threading.Tasks.Task<bool> RunSingleOperationAsync(
        string currentGame,
        Core.Data.GameModules games,
        IDictionary<string, object?> op,
        Data.PromptAnswers promptAnswers,
        System.Threading.CancellationToken cancellationToken = default
    ) {
        return await this.OperationContext.Single.RunAsync(currentGame: currentGame, games: games, op: op, promptAnswers: promptAnswers, Context: this.Context, OperationContext: this.OperationContext, cancellationToken: cancellationToken);
    }

    public bool CloneModule(string url, System.Threading.CancellationToken cancellationToken = default) {
        return Core.Services.Git.GitTools.CloneModule(url: url, commandService: this.Context.CommandService);
    }

}

public interface IEngineFace {
    public Task<bool> RunSingleOperationAsync(string currentGame, Core.Data.GameModules games, IDictionary<string, object?> op, Data.PromptAnswers promptAnswers, CancellationToken cancellationToken = default(CancellationToken));
    public bool CloneModule(string url, CancellationToken cancellationToken = default(CancellationToken));
    public Core.Services.GameLauncher GameLauncher { get; }
    public Core.Abstractions.IEngineContext Context { get; }
    public Core.Abstractions.IOperationContext OperationContext { get; }
}
