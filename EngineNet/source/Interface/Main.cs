
using EngineNet.Core.Engine;

namespace EngineNet.Interface;

/// <summary>
/// Utility methods for all UI
/// </summary>
public sealed class Main {

    private static Core.Engine.Engine Engine = null!;

    public Main(Engine _engine) {
        Engine = _engine;
    }

    // called by program.cs to choose ui, and manage engine, instead of passing engine to ui, this class will manage and expose methods via a child class it passes into the ui
    public async Task<int> init(string[] args, string ui, System.Threading.CancellationToken cancellationToken) {

        var miniEngine = new MiniEngine(Engine);

        switch (ui) {
            case "gui":
                // for now gui uses engine direclty
                Core.Diagnostics.Trace("Launching GUI Interface...");
                return Interface.GUI.GuiBootstrapper.Run(Engine);
            case "tui":
                Core.Diagnostics.Trace("Launching TUI Interface...");
                Interface.Terminal.TUI TUI = new Interface.Terminal.TUI(miniEngine);
                return await TUI.RunInteractiveMenuAsync(cancellationToken);
            case "cli":
                Core.Diagnostics.Trace("Launching CLI Interface...");
                Interface.Terminal.CLI CLI = new Interface.Terminal.CLI(miniEngine);
                return await CLI.RunAsync(args, cancellationToken);
            default:
                System.Console.Error.WriteLine(value: $"No valid interface mode selected. Expected 'gui', 'tui', or 'cli', but got '{ui}'.");
                Core.Diagnostics.Bug("No valid interface mode selected.");
                break;
        }

        return 0;
    }

    private class MiniEngine : MiniEngineFace {
        private Core.Engine.Engine Engine;

        public MiniEngine(Core.Engine.Engine engine) {
            Engine = engine;
        }

        // expose methods here that the ui can call to interact with the engine, but only the ones we want to allow, and in a way that is safe and easy to use for the ui

        public Dictionary<string, Core.Data.GameModuleInfo> Context_GameRegistry_GetModules(Core.Utils.ModuleFilter filter) {
            return Engine.Context.GameRegistry.GetModules(filter);
        }

        public Core.Data.PreparedOperations Context_OperationContext_OperationsService_LoadAndPrepare(
            string opsFile,
            string? currentGame = null,
            Dictionary<string, Core.Data.GameModuleInfo>? games = null,
            IDictionary<string, object?>? engineConfig = null
        ) {
            return Engine.Context.OperationContext.OperationsService.LoadAndPrepare(opsFile, currentGame, games, engineConfig);
        }

        public IDictionary<string, object?> Context_EngineConfig_Data {
            get => Engine.Context.EngineConfig.Data;
        }

        public async System.Threading.Tasks.Task<bool> RunSingleOperationAsync(
            string currentGame,
            Dictionary<string, EngineNet.Core.Data.GameModuleInfo> games,
            IDictionary<string, object?> op,
            IDictionary<string, object?> promptAnswers,
            System.Threading.CancellationToken cancellationToken = default
        ) {
            return await Engine.RunSingleOperationAsync(currentGame, games, op, promptAnswers, cancellationToken: cancellationToken);
        }
        public List<string> Context_CommandService_BuildCommand(string currentGame, Dictionary<string, Core.Data.GameModuleInfo> games, IDictionary<string, object?> engineData, IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers) {
            return Engine.Context.CommandService.BuildCommand(currentGame, games, engineData, op, promptAnswers);
        }

        public bool Context_CommandService_ExecuteCommand(IList<string> commandParts, string title, Core.ProcessRunner.OutputHandler? onOutput = null, Core.ProcessRunner.EventHandler? onEvent = null, Core.ProcessRunner.StdinProvider? stdinProvider = null, IDictionary<string, object?>? envOverrides = null, CancellationToken cancellationToken = default) {
            return Engine.Context.CommandService.ExecuteCommand(commandParts, title, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdinProvider, envOverrides: envOverrides, cancellationToken: cancellationToken);
        }

        public async Task<bool> Context_OperationContext_OperationsService_CollectAnswersAsync(
            Dictionary<string, object?> op,
            Dictionary<string, object?> answers,
            Core.Services.OperationsService.PromptHandler handler,
            bool defaultsOnly = false
        ) {
            return await Engine.Context.OperationContext.OperationsService.CollectAnswersAsync(op, answers, handler, defaultsOnly);
        }

        public async Task<bool> GameLauncher_LaunchGameAsync(string name) {
            return await Engine.GameLauncher.LaunchGameAsync(name);
        }

        public async System.Threading.Tasks.Task<RunAllResult> RunAllAsync(
            string gameName,
            Core.ProcessRunner.OutputHandler? onOutput = null,
            Core.ProcessRunner.EventHandler? onEvent = null,
            Core.ProcessRunner.StdinProvider? stdinProvider = null,
            System.Threading.CancellationToken cancellationToken = default
        ) {
            return await Engine.RunAllAsync(gameName, onOutput, onEvent, stdinProvider, cancellationToken);
        }


    }
}

public interface MiniEngineFace {

    public Dictionary<string, Core.Data.GameModuleInfo> Context_GameRegistry_GetModules(Core.Utils.ModuleFilter filter);

    Core.Data.PreparedOperations Context_OperationContext_OperationsService_LoadAndPrepare(
        string opsFile,
        string? currentGame = null,
        Dictionary<string, Core.Data.GameModuleInfo>? games = null,
        IDictionary<string, object?>? engineConfig = null
    );

    IDictionary<string, object?> Context_EngineConfig_Data { get; }

    public System.Threading.Tasks.Task<bool> RunSingleOperationAsync(
        string currentGame,
        Dictionary<string, EngineNet.Core.Data.GameModuleInfo> games,
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers,
        System.Threading.CancellationToken cancellationToken = default
    );

    public List<string> Context_CommandService_BuildCommand(string currentGame, Dictionary<string, Core.Data.GameModuleInfo> games, IDictionary<string, object?> engineData, IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers);

    public bool Context_CommandService_ExecuteCommand(IList<string> commandParts, string title, Core.ProcessRunner.OutputHandler? onOutput = null, Core.ProcessRunner.EventHandler? onEvent = null, Core.ProcessRunner.StdinProvider? stdinProvider = null, IDictionary<string, object?>? envOverrides = null, CancellationToken cancellationToken = default);

    public Task<bool> Context_OperationContext_OperationsService_CollectAnswersAsync(
        Dictionary<string, object?> op,
        Dictionary<string, object?> answers,
        Core.Services.OperationsService.PromptHandler handler,
        bool defaultsOnly = false
    );

    public Task<bool> GameLauncher_LaunchGameAsync(string name);

    public Task<RunAllResult> RunAllAsync(
        string gameName,
        Core.ProcessRunner.OutputHandler? onOutput = null,
        Core.ProcessRunner.EventHandler? onEvent = null,
        Core.ProcessRunner.StdinProvider? stdinProvider = null,
        System.Threading.CancellationToken cancellationToken = default
    );

}
