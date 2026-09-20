namespace EngineNet;

using System;
using System.Threading.Tasks;
using Core.Abstractions;
using Core.Data;
using Core.ExternalTools;
using Core.Services;
using Core.Utils;
using EngineNet.Core.Engine;

internal static class Init {
    /// <summary>
    /// Initialises the engine
    /// </summary>
    internal static async System.Threading.Tasks.Task<EngineNet.Core.Engine.IEngineFace> InitialiseEngine() {
        IScriptActionDispatcher scriptActionDispatcher = new EngineNet.ScriptEngines.ScriptActionDispatcher();
        if (Program.Engine != null) {
            return Program.Engine;
        }

        JsonToolResolver tools = new();
        EngineConfig engineConfig = new();

        Core.Utils.GameRegistry.Registries _registries = await Core.Utils.GameRegistry.Registries.CreateAsync();
        ModuleScanner _scanner = new(registries: _registries);

        Core.Utils.GameRegistry.GameRegistry gameRegistry = new(registries: _registries, scanner: _scanner);

        Core.Services.CommandService.CommandService _commandService = new();
        GameLauncher _gameLauncher = new(gameRegistry: gameRegistry, toolResolver: tools, config: engineConfig, commandService: _commandService, scriptActionDispatcher: scriptActionDispatcher);
        Core.Services.OperationsService.OperationsLoader _opsLoader = new();
        Core.Services.OperationsService.OperationsService _operationsService = new(loader: _opsLoader, gameRegistry: gameRegistry);

        Core.Operations.Single Single = new(scriptActionDispatcher: scriptActionDispatcher);

        EngineNet.Core.Engine.Engine _engine = new(
            gameRegistry: gameRegistry,
            gameLauncher: _gameLauncher,
            OperationsLoader: _opsLoader,
            commandService: _commandService,
            OperationsService: _operationsService,
            toolResolver: tools,
            engineConfig: engineConfig,
            Runner: Single
        );

        return _engine;
    }
}

internal sealed class InitUI {
    // choose ui, and manage engine, instead of passing engine to ui, this class will manage and expose methods via a child class it passes into the ui
    public async Task<int> init(string[] args, string ui, Interface.MiniEngineFace miniEngine,
        System.Threading.CancellationToken cancellationToken) {
        try {
            switch (ui) {
                case "gui":
                    // GUI uses the limited mini engine surface; the full engine is only stashed for previewer/bootstrapping.
                    Shared.IO.Diagnostics.Trace("Launching GUI Interface...");
                    return Interface.GUI.GuiBootstrapper.Run(miniEngine: miniEngine, cancellationToken: cancellationToken);
                case "tui":
                    Shared.IO.Diagnostics.Trace("Launching TUI Interface...");
                    Terminal.TUI TUI = new(engine: miniEngine);
                    return await TUI.RunAsync(cancellationToken: cancellationToken);
                case "cli":
                    Shared.IO.Diagnostics.Trace("Launching CLI Interface...");
                    Terminal.CLI CLI = new(engine: miniEngine);
                    return await CLI.RunAsync(args: args, cancellationToken: cancellationToken);
                default:
                    await System.Console.Error.WriteLineAsync(
                        $"No valid interface mode selected. Expected 'gui', 'tui', or 'cli', but got '{ui}'.");
                    Shared.IO.Diagnostics.Bug("No valid interface mode selected.");
                    break;
            }

            return 0;
        }
        catch (OperationCanceledException) {
            Shared.IO.Diagnostics.Trace("exiting ui");
            throw;
        }
        catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Error initializing UI '{ui}': {ex.Message}", ex: ex);
            await System.Console.Error.WriteLineAsync($"Error initializing UI '{ui}': {ex.Message}");
            return 1;
        }
    }
}