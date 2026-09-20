
namespace EngineNet.Core.Abstractions;

public interface ISingle {
    public System.Threading.Tasks.Task<bool> RunAsync(
        string currentGame,
        Core.Data.GameModules games,
        System.Collections.Generic.IDictionary<string, object?> op,
        Core.Data.PromptAnswers promptAnswers,
        Core.Abstractions.IEngineContext Context,
        Core.Abstractions.IOperationContext OperationContext,
        System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)
    );
}
