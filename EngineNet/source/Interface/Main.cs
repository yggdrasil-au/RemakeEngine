
namespace EngineNet.Interface;

/// <summary>
/// Utility methods for all UI
/// </summary>
public sealed class Main {
    private readonly MiniEngineFace miniEngine;

    public Main(Core.Engine.IEngineFace engine) {
        MiniEngine mini = new MiniEngine(engine);
        miniEngine = mini;
    }

    // called by program.cs to choose ui, and manage engine, instead of passing engine to ui, this class will manage and expose methods via a child class it passes into the ui
    public async Task<int> init(string[] args, string ui, System.Threading.CancellationToken cancellationToken) {

        switch (ui) {
            case "gui":
                // GUI uses the limited mini engine surface; the full engine is only stashed for previewer/bootstrapping.
                Shared.IO.Diagnostics.Trace("Launching GUI Interface...");
                return Interface.GUI.GuiBootstrapper.Run(miniEngine, cancellationToken);
            case "tui":
                Shared.IO.Diagnostics.Trace("Launching TUI Interface...");
                Interface.Terminal.TUI TUI = new Interface.Terminal.TUI(miniEngine);
                return await TUI.RunInteractiveMenuAsync(cancellationToken);
            case "cli":
                Shared.IO.Diagnostics.Trace("Launching CLI Interface...");
                Interface.Terminal.CLI CLI = new Interface.Terminal.CLI(miniEngine);
                return await CLI.RunAsync(args, cancellationToken);
            default:
                await System.Console.Error.WriteLineAsync(value: $"No valid interface mode selected. Expected 'gui', 'tui', or 'cli', but got '{ui}'.");
                Shared.IO.Diagnostics.Bug("No valid interface mode selected.");
                break;
        }

        return 0;
    }

    private sealed class MiniEngine(Core.Engine.IEngineFace Engine) : MiniEngineFace {

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
}

internal interface MiniEngineFace {

    internal Core.Data.GameModules GameRegistry_GetModules(Core.Data.ModuleFilter filter);

    /// <summary>
    /// Gets all registered modules from the registry.
    /// </summary>
    internal IReadOnlyDictionary<string, object?> GameRegistry_GetRegisteredModules();

    /// <summary>
    /// Forces a refresh of registered modules.
    /// </summary>
    internal void GameRegistry_RefreshModules();

    /// <summary>
    /// Resolves the absolute path for a game module.
    /// </summary>
    internal string? GameRegistry_GetGamePath(string name);

    Core.Data.PreparedOperations OperationsService_LoadAndPrepare(
        string opsFile,
        string? currentGame = null,
        Core.Data.GameModules? games = null,
        IDictionary<string, object?>? engineConfig = null
    );

    IDictionary<string, object?> EngineConfig_Data { get; }

    /// <summary>
    /// Clones a module repository from the registry URL.
    /// </summary>
    internal bool GitService_CloneModule(string url);

    internal System.Threading.Tasks.Task<bool> RunSingleOperationAsync(
        string currentGame,
        Core.Data.GameModules games,
        IDictionary<string, object?> op,
        Core.Data.PromptAnswers promptAnswers,
        System.Threading.CancellationToken cancellationToken = default(CancellationToken)
    );

    internal List<string> CommandService_BuildCommand(string currentGame, Core.Data.GameModules games, IDictionary<string, object?> engineData, IDictionary<string, object?> op, Core.Data.PromptAnswers promptAnswers);

    internal bool CommandService_ExecuteCommand(IList<string> commandParts, string title, Core.ProcessRunner.OutputHandler? onOutput = null, Core.ProcessRunner.EventHandler? onEvent = null, Core.ProcessRunner.StdinProvider? stdinProvider = null, IDictionary<string, object?>? envOverrides = null, CancellationToken cancellationToken = default(CancellationToken));

    internal Task<bool> OperationsService_CollectAnswersAsync(
        Dictionary<string, object?> op,
        Core.Data.PromptAnswers answers,
        Core.Services.OperationsService.PromptHandler promptHandler,
        bool defaultsOnly = false,
        CancellationToken cancellationToken = default(CancellationToken)
    );

    internal Task<bool> GameLauncher_LaunchGameAsync(string name, CancellationToken cancellationToken = default(CancellationToken));

    internal Task<Core.Operations.RunAllResult> RunAllAsync(
        string gameName,
        Core.ProcessRunner.OutputHandler? onOutput = null,
        Core.ProcessRunner.EventHandler? onEvent = null,
        Core.ProcessRunner.StdinProvider? stdinProvider = null,
        System.Threading.CancellationToken cancellationToken = default(CancellationToken)
    );

    internal void CommandService_OpenFolder(string path);

}
