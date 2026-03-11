using System.Collections.Generic;
using System.Threading;

namespace EngineNet.Core.Engine;

/// <summary>
/// Core Engine class providing main functionality
/// </summary>
public sealed partial class Engine {

    /* :: :: Vars :: Start :: */

    // Services exposed to partial classes
    public Core.Abstractions.IGameRegistry GameRegistry { get; }
    public Core.Abstractions.IGameLauncher GameLauncher { get; }
    public Core.Abstractions.IOperationsLoader OperationsLoader { get; }
    public Core.Services.OperationsService OperationsService { get; }
    public Core.Services.GitService GitService { get; }
    public Core.Abstractions.ICommandService CommandService { get; }
    public Core.ExternalTools.IToolResolver ToolResolver { get; }
    public Core.EngineConfig EngineConfig { get; }
    public Core.Engine.OperationExecution OperationExecution { get; }
    public Core.Engine.Engino Engino { get; }

    /* :: :: Vars :: End :: */

    public Engine(
        Core.Abstractions.IGameRegistry gameRegistry,
        Core.Abstractions.IGameLauncher gameLauncher,
        Core.Abstractions.IOperationsLoader operationsLoader,
        Core.Services.OperationsService operationsService,
        Core.Services.GitService gitService,
        Core.Abstractions.ICommandService commandService,
        Core.ExternalTools.IToolResolver toolResolver,
        Core.EngineConfig engineConfig,
        Core.Engine.OperationExecution operationExecution,
        Core.Engine.Engino engino
    ) {
        GameRegistry = gameRegistry;
        GameLauncher = gameLauncher;
        OperationsLoader = operationsLoader;
        OperationsService = operationsService;
        GitService = gitService;
        CommandService = commandService;
        ToolResolver = toolResolver;
        EngineConfig = engineConfig;
        OperationExecution = operationExecution;
        Engino = engino;
    }

    /* :: :: */
    //
    /* :: :: */

    // Downloads a game module via Git
    public bool DownloadModule(string url) {
        return GitService.CloneModule(url);
    }

    // Builds a command from operation and context
    public List<string> BuildCommand(string currentGame, Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games, IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers) {
        return CommandService.BuildCommand(currentGame, games, EngineConfig.Data, op, promptAnswers);
    }

    // Executes a command via ProcessRunner
    public bool ExecuteCommand(IList<string> commandParts, string title, EngineNet.Core.ProcessRunner.OutputHandler? onOutput = null, Core.ProcessRunner.EventHandler? onEvent = null, Core.ProcessRunner.StdinProvider? stdinProvider = null, IDictionary<string, object?>? envOverrides = null, CancellationToken cancellationToken = default) {
        return CommandService.ExecuteCommand(commandParts, title, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdinProvider, envOverrides: envOverrides, cancellationToken: cancellationToken);
    }

    // Scans for game modules in registries
    /// <summary>
    /// returns a dictionary of game modules filtered by the provided filter
    /// with the name as the key and the GameModuleInfo as the value
    /// </summary>
    /// <param name="_Filter"></param>
    /// <returns></returns>
    public Dictionary<string, Core.Utils.GameModuleInfo> Modules(Core.Utils.ModuleFilter _Filter) {
        return GameRegistry.GetModules(_Filter);
    }

    // Discovers built games from registries
    public Dictionary<string, Core.Utils.GameInfo> DiscoverBuiltGames() {
        return GameRegistry.GetBuiltGames();
    }

    // Gets the executable path for a built game
    public string? GetGameExecutable(string name) {
        return GameRegistry.GetGameExecutable(name);
    }

    /* :: :: */
    //
    /* :: :: */

    /// <summary>
    /// Loads a list of operations from a file (JSON or TOML)
    /// </summary>
    /// <param name="opsFile"></param>
    /// <returns></returns>
    public List<Dictionary<string, object?>>? LoadOperationsList(string opsFile) {
        return OperationsLoader.LoadOperations(opsFile);
    }

    /// <summary>
    /// Gets the root path for a game by name
    /// </summary>
    public string? GetGamePath(string name) {
        return GameRegistry.GetGamePath(name);
    }

    /* :: :: */
    //

}
