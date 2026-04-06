
namespace EngineNet.Core.Engine;

/// <summary>
/// Core Engine class providing main functionality
/// </summary>
internal sealed class Engine : IEngineFace{

    /* :: :: Vars :: Start :: */

    // Services exposed to partial classes
    public Core.Services.GameLauncher GameLauncher { get; }
    public Core.Engine.EngineContext Context { get; }

    public OperationContext OperationContext { get; }

    /* :: :: Vars :: End :: */

    internal Engine(
        Core.Services.GameRegistry gameRegistry,
        Core.Services.GameLauncher gameLauncher,
        Core.Services.OperationsLoader OperationsLoader,
        Core.Services.CommandService commandService,
        Core.Services.OperationsService OperationsService,
        Core.ExternalTools.JsonToolResolver toolResolver,
        Data.EngineConfig engineConfig,
        EngineNet.Core.Operations.Single Runner
    ) {
        this.GameLauncher = gameLauncher;
        this.Context = new Core.Engine.EngineContext(
            gameRegistry,
            commandService,
            toolResolver,
            engineConfig
        );
        this.OperationContext = new OperationContext(
            OperationsService,
            OperationsLoader,
            Runner
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
        return await this.OperationContext.Single.RunAsync(currentGame, games, op, promptAnswers, this.Context, this.OperationContext, cancellationToken);
    }

    public bool CloneModule(string url, System.Threading.CancellationToken cancellationToken = default) {
        return Utils.GitTools.CloneModule(url, this.Context.CommandService);
    }

}

public interface IEngineFace {
    public Task<bool> RunSingleOperationAsync(string currentGame, Core.Data.GameModules games, IDictionary<string, object?> op, Data.PromptAnswers promptAnswers, CancellationToken cancellationToken = default(CancellationToken));
    public bool CloneModule(string url, CancellationToken cancellationToken = default(CancellationToken));
    public Core.Services.GameLauncher GameLauncher { get; }
    public EngineContext Context { get; }
    public OperationContext OperationContext { get; }
}
