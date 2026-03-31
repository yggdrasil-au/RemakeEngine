
namespace EngineNet.Core.Engine;

/// <summary>
/// Core Engine class providing main functionality
/// </summary>
internal sealed class Engine : IEngineFace{

    /* :: :: Vars :: Start :: */

    // Services exposed to partial classes
    public Core.Services.GameLauncher GameLauncher { get; }
    public Core.Engine.EngineContext Context { get; }

    /* :: :: Vars :: End :: */

    internal Engine(
        Core.Services.GameRegistry gameRegistry,
        Core.Services.GameLauncher gameLauncher,
        Core.Services.OperationsLoader OperationsLoader,
        Core.Services.CommandService commandService,
        Core.Services.OperationsService OperationsService,
        Core.ExternalTools.JsonToolResolver toolResolver,
        Data.EngineConfig engineConfig,
        EngineNet.Core.Engine.Operations.Single Runner
    ) {
        this.GameLauncher = gameLauncher;
        OperationContext operationContext = new OperationContext(
            OperationsService,
            OperationsLoader,
            Runner
        );
        this.Context = new Core.Engine.EngineContext(
            gameRegistry,
            commandService,
            toolResolver,
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
        Data.PromptAnswers promptAnswers,
        System.Threading.CancellationToken cancellationToken = default
    ) {
        return await this.Context.OperationContext.Single.RunAsync(currentGame, games, op, promptAnswers, this.Context, cancellationToken);
    }

    public bool CloneModule(string url) {
        return Utils.GitTools.CloneModule(url, this.Context.CommandService);
    }

}

public interface IEngineFace {
    public Task<bool> RunSingleOperationAsync(string currentGame, Dictionary<string, EngineNet.Core.Data.GameModuleInfo> games, IDictionary<string, object?> op, Data.PromptAnswers promptAnswers, CancellationToken cancellationToken = default);
    public bool CloneModule(string url);
    internal Core.Services.GameLauncher GameLauncher { get; }
    internal EngineContext Context { get; }
}
