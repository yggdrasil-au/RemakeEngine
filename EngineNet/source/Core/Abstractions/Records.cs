
namespace EngineNet.Core.Abstractions;

/// <summary>
/// Encapsulates the core services required for executing engine operations.
/// </summary>
public sealed record IEngineContext(
    Core.Abstractions.IGameRegistry GameRegistry,
    Core.Abstractions.ICommandService CommandService,
    Core.Abstractions.IJsonToolResolver ToolResolver,
    Core.Data.EngineConfig EngineConfig
);

// operation context record
public sealed record IOperationContext(
    Core.Abstractions.IOperationsService OperationsService,
    Core.Abstractions.IOperationsLoader OperationsLoader,
    Core.Abstractions.ISingle Single
);

public interface IGameRegistry {
    public Core.Data.GameModules GetModules(Core.Data.ModuleFilter filter);
    public string? GetGameExecutable(string name);
    public string? GetGamePath(string name);
    public IReadOnlyDictionary<string, object?> GetRegisteredModules();
    public void RefreshModules();
}

public interface IOperationsService {

    public Data.ModuleOperationSession LoadModuleSession(
        string gameName,
        Core.Data.GameModules games,
        IDictionary<string, object?> engineConfig
    );

    public Core.Data.PreparedOperations LoadAndPrepare(
        string opsFile,
        string? currentGame = null,
        Core.Data.GameModules? games = null,
        IDictionary<string, object?>? engineConfig = null
    );

    public Task<bool> CollectAnswersAsync(
        Dictionary<string, object?> op,
        Data.PromptAnswers answers,
        PromptHandler promptHandler,
        bool defaultsOnly = false,
        CancellationToken cancellationToken = default(CancellationToken)
    );

    public delegate Task<Data.PromptResponse> PromptHandler(Data.PromptRequest request, CancellationToken cancellationToken);

}

public interface IOperationsLoader {
}