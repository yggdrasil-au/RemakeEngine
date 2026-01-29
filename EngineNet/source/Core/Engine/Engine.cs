using System.Collections.Generic;
using System.Threading;

namespace EngineNet.Core;

/// <summary>
/// Core Engine class providing main functionality
/// </summary>
internal sealed partial class Engine {

    /* :: :: Vars :: Start :: */
    public string RootPath { get; }

    // Services exposed to partial classes
    internal Core.Abstractions.IGameRegistry GameRegistry { get; }
    internal Core.Abstractions.IGameLauncher GameLauncher { get; }
    internal Core.Abstractions.IOperationsLoader OperationsLoader { get; }
    internal Core.Abstractions.IGitService GitService { get; }
    internal Core.Abstractions.ICommandService CommandService { get; }
    internal Core.Tools.IToolResolver ToolResolver { get; }
    internal Core.EngineConfig EngineConfig { get; }
    internal Core.Enginey Enginey { get; }

    /* :: :: Vars :: End :: */

    internal Engine(
        string rootPath,
        Core.Abstractions.IGameRegistry gameRegistry,
        Core.Abstractions.IGameLauncher gameLauncher,
        Core.Abstractions.IOperationsLoader operationsLoader,
        Core.Abstractions.IGitService gitService,
        Core.Abstractions.ICommandService commandService,
        Core.Tools.IToolResolver toolResolver,
        Core.EngineConfig engineConfig,
        Core.Enginey enginey
    ) {
        RootPath = rootPath;
        GameRegistry = gameRegistry;
        GameLauncher = gameLauncher;
        OperationsLoader = operationsLoader;
        GitService = gitService;
        CommandService = commandService;
        ToolResolver = toolResolver;
        EngineConfig = engineConfig;
        Enginey = enginey;
    }

    /* :: :: */
    //
    /* :: :: */

    // Downloads a game module via Git
    internal bool DownloadModule(string url) {
        return GitService.CloneModule(url);
    }

    // Builds a command from operation and context
    internal List<string> BuildCommand(string currentGame, Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games, IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers) {
        return CommandService.BuildCommand(currentGame, games, EngineConfig.Data, op, promptAnswers);
    }

    // Executes a command via ProcessRunner
    internal bool ExecuteCommand(IList<string> commandParts, string title, EngineNet.Core.ProcessRunner.OutputHandler? onOutput = null, Core.ProcessRunner.EventHandler? onEvent = null, Core.ProcessRunner.StdinProvider? stdinProvider = null, IDictionary<string, object?>? envOverrides = null, CancellationToken cancellationToken = default) {
        return CommandService.ExecuteCommand(commandParts, title, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdinProvider, envOverrides: envOverrides, cancellationToken: cancellationToken);
    }

    // Scans for game modules in registries
    /// <summary>
    /// returns a dictionary of game modules filtered by the provided filter
    /// with the name as the key and the GameModuleInfo as the value
    /// </summary>
    /// <param name="_Filter"></param>
    /// <returns></returns>
    internal Dictionary<string, Core.Utils.GameModuleInfo> Modules(Core.Utils.ModuleFilter _Filter) {
        return GameRegistry.GetModules(_Filter);
    }

    // Discovers built games from registries
    internal Dictionary<string, Core.Utils.GameInfo> DiscoverBuiltGames() {
        return GameRegistry.GetBuiltGames();
    }

    // Gets the executable path for a built game
    internal string? GetGameExecutable(string name) {
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
    internal List<Dictionary<string, object?>>? LoadOperationsList(string opsFile) {
        return OperationsLoader.LoadOperations(opsFile);
    }

    /// <summary>
    /// Gets the root path for a game by name
    /// </summary>
    internal string? GetGamePath(string name) {
        return GameRegistry.GetGamePath(name);
    }

    /// <summary>
    /// Launches a game by name
    /// </summary>
    internal bool LaunchGame(string name) {
        // Synchronous wrapper for async method to maintain API compatibility for now
        return GameLauncher.LaunchGameAsync(name).GetAwaiter().GetResult();
    }

    /* :: :: */
    //

}
