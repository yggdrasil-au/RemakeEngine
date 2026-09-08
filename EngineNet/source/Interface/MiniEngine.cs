
namespace EngineNet.Interface;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;


public sealed class MiniEngine(Core.Engine.IEngineFace Engine) : MiniEngineFace {

    // Expose only the methods that the UI can use, in a safe and simple shape.

    public Core.Data.GameModules GameRegistry_GetModules(Core.Data.ModuleFilter filter) {
        return Engine.Context.GameRegistry.GetModules(filter);
    }

    /// <summary>
    /// Gets all registered modules from the registry.
    /// </summary>
    public IReadOnlyDictionary<string, object?> GameRegistry_GetRegisteredModules() {
        return Engine.Context.GameRegistry.GetRegisteredModules();
    }

    /// <summary>
    /// Forces a refresh of registered modules.
    /// </summary>
    public void GameRegistry_RefreshModules() {
        Engine.Context.GameRegistry.RefreshModules();
    }

    /// <summary>
    /// Resolves the absolute path for a game module.
    /// </summary>
    public string? GameRegistry_GetGamePath(string name) {
        return Engine.Context.GameRegistry.GetGamePath(name);
    }

    public Core.Data.PreparedOperations OperationsService_LoadAndPrepare(
        string opsFile,
        string? currentGame = null,
        Core.Data.GameModules? games = null,
        IDictionary<string, object?>? engineConfig = null
    ) {
        return Engine.OperationContext.OperationsService.LoadAndPrepare(opsFile, currentGame, games, engineConfig);
    }

    /// <summary>
    /// Loads the engine-owned operation session for a module.
    /// </summary>
    public Core.Data.ModuleOperationSession OperationsService_LoadModuleSession(
        string gameName,
        Core.Data.GameModules games
    ) {
        return Engine.OperationContext.OperationsService.LoadModuleSession(gameName, games, Engine.Context.EngineConfig.Data);
    }

    public IDictionary<string, object?> EngineConfig_Data => Engine.Context.EngineConfig.Data;

    /// <summary>
    /// Clones a module repository from the registry URL.
    /// </summary>
    public bool GitService_CloneModule(string url) {
        return Engine.CloneModule(url);
    }

    public async System.Threading.Tasks.Task<bool> RunSingleOperationAsync(
        string currentGame,
        Core.Data.GameModules games,
        IDictionary<string, object?> op,
        Core.Data.PromptAnswers promptAnswers,
        System.Threading.CancellationToken cancellationToken = default(CancellationToken)
    ) {
        return await Engine.RunSingleOperationAsync(currentGame, games, op, promptAnswers, cancellationToken: cancellationToken);
    }
    public List<string> CommandService_BuildCommand(string currentGame, Core.Data.GameModules games, IDictionary<string, object?> engineData, IDictionary<string, object?> op, Core.Data.PromptAnswers promptAnswers) {
        return Engine.Context.CommandService.BuildCommand(currentGame, games, engineData, op, promptAnswers);
    }

    public bool CommandService_ExecuteCommand(IList<string> commandParts, string title, Core.ProcessRunner.OutputHandler? onOutput = null, Core.ProcessRunner.EventHandler? onEvent = null, Core.ProcessRunner.StdinProvider? stdinProvider = null, IDictionary<string, object?>? envOverrides = null, CancellationToken cancellationToken = default(CancellationToken)) {
        return Engine.Context.CommandService.ExecuteCommand(commandParts, title, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdinProvider, envOverrides: envOverrides, cancellationToken: cancellationToken);
    }

    public async Task<bool> OperationsService_CollectAnswersAsync(
        Dictionary<string, object?> op,
        Core.Data.PromptAnswers answers,
        Core.Services.OperationsService.PromptHandler promptHandler,
        bool defaultsOnly = false,
        CancellationToken cancellationToken = default(CancellationToken)
    ) {
        return await Engine.OperationContext.OperationsService.CollectAnswersAsync(op, answers, promptHandler, defaultsOnly, cancellationToken);
    }

    public async Task<bool> GameLauncher_LaunchGameAsync(string name, CancellationToken cancellationToken = default(CancellationToken)) {
        return await Engine.GameLauncher.LaunchGameAsync(name, cancellationToken: cancellationToken);
    }

    public async System.Threading.Tasks.Task<Core.Operations.RunAllResult> RunAllAsync(
        string gameName,
        Core.ProcessRunner.OutputHandler? onOutput = null,
        Core.ProcessRunner.EventHandler? onEvent = null,
        Core.ProcessRunner.StdinProvider? stdinProvider = null,
        System.Threading.CancellationToken cancellationToken = default(CancellationToken)
    ) {
        return await EngineNet.Core.Operations.All.RunAsync(gameName, Engine.Context, Engine.OperationContext, onOutput, onEvent, stdinProvider, cancellationToken);
    }

    public void CommandService_OpenFolder(string path) {
        Engine.Context.CommandService.OpenFolder(path);
    }

}


public interface MiniEngineFace {
    public Core.Data.GameModules GameRegistry_GetModules(Core.Data.ModuleFilter filter);

    /// <summary>
    /// Gets all registered modules from the registry.
    /// </summary>
    public IReadOnlyDictionary<string, object?> GameRegistry_GetRegisteredModules();

    /// <summary>
    /// Forces a refresh of registered modules.
    /// </summary>
    public void GameRegistry_RefreshModules();

    /// <summary>
    /// Resolves the absolute path for a game module.
    /// </summary>
    public string? GameRegistry_GetGamePath(string name);

    Core.Data.PreparedOperations OperationsService_LoadAndPrepare(
        string opsFile,
        string? currentGame = null,
        Core.Data.GameModules? games = null,
        IDictionary<string, object?>? engineConfig = null
    );

    /// <summary>
    /// Loads the engine-owned operation session for a module.
    /// </summary>
    public Core.Data.ModuleOperationSession OperationsService_LoadModuleSession(
        string gameName,
        Core.Data.GameModules games
    );

    IDictionary<string, object?> EngineConfig_Data { get; }

    /// <summary>
    /// Clones a module repository from the registry URL.
    /// </summary>
    public bool GitService_CloneModule(string url);

    public System.Threading.Tasks.Task<bool> RunSingleOperationAsync(
        string currentGame,
        Core.Data.GameModules games,
        IDictionary<string, object?> op,
        Core.Data.PromptAnswers promptAnswers,
        System.Threading.CancellationToken cancellationToken = default(CancellationToken)
    );

    public List<string> CommandService_BuildCommand(string currentGame, Core.Data.GameModules games, IDictionary<string, object?> engineData, IDictionary<string, object?> op, Core.Data.PromptAnswers promptAnswers);

    public bool CommandService_ExecuteCommand(IList<string> commandParts, string title, Core.ProcessRunner.OutputHandler? onOutput = null, Core.ProcessRunner.EventHandler? onEvent = null, Core.ProcessRunner.StdinProvider? stdinProvider = null, IDictionary<string, object?>? envOverrides = null, CancellationToken cancellationToken = default(CancellationToken));

    public Task<bool> OperationsService_CollectAnswersAsync(
        Dictionary<string, object?> op,
        Core.Data.PromptAnswers answers,
        Core.Services.OperationsService.PromptHandler promptHandler,
        bool defaultsOnly = false,
        CancellationToken cancellationToken = default(CancellationToken)
    );

    public Task<bool> GameLauncher_LaunchGameAsync(string name, CancellationToken cancellationToken = default(CancellationToken));

    public Task<Core.Operations.RunAllResult> RunAllAsync(
        string gameName,
        Core.ProcessRunner.OutputHandler? onOutput = null,
        Core.ProcessRunner.EventHandler? onEvent = null,
        Core.ProcessRunner.StdinProvider? stdinProvider = null,
        System.Threading.CancellationToken cancellationToken = default(CancellationToken)
    );

    public void CommandService_OpenFolder(string path);

}
