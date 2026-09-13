
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Avalonia;
using EngineNet.Core.Abstractions;
using EngineNet.Core.Data;

namespace EngineNet;

using Core.ExternalTools;
using Core.Operations;
using Core.Services;
using Core.Utils;

public static class Program {

    private static EngineNet.Core.Engine.IEngineFace? Engine {get; set;}
    private static string rootPath {get; set;} = string.Empty;
    private static bool isGui {get; set;}
    private static bool isTui {get; set;}
    private static bool isCli {get; set;}

    // Add a static counter inside the Program class
    private static int _ctrlCCount = 0;

    /* :: :: Vars :: START :: */
    public static AppBuilder BuildAvaloniaApp()  {
        return GUI.GuiBootstrapper.BuildAvaloniaApp();
    }

    /* :: :: Vars :: END :: */
    // //
    /* :: :: Main :: START :: */
    [STAThread]
    public static async System.Threading.Tasks.Task<int> Main(string[] args) {
        ShutdownCancellationController shutdownCancellationController = new ShutdownCancellationController();
        List<string> PreinitialDiagnosticsLog = new List<string>();

        bool diagnosticsInitialized = false;
        object logLock = new object();

        // Local function to safely route log messages based on initialization state
        void LogCancelMessage(string msg) {
            lock (logLock) {
                if (diagnosticsInitialized) {
                    Shared.IO.Diagnostics.Log(msg);
                } else {
                    PreinitialDiagnosticsLog.Add(msg);
                }
            }
        }

        System.ConsoleCancelEventHandler cancelHandler = (_, e) => {
            _ctrlCCount++;
            if (_ctrlCCount >= 3) {
                LogCancelMessage("Multiple Ctrl+C detected. Forcing shutdown.");
                System.Environment.Exit(exitCode: 1);
            }

            e.Cancel = true;
            try {
                shutdownCancellationController.Cancel();
            } catch (System.Exception ex) {
                LogCancelMessage($"Cancellation error: {ex}");
            }

            LogCancelMessage("Global Cancellation Requested (Ctrl+C)");
        };

        try {
            System.Console.CancelKeyPress += cancelHandler;

            bool hasConsole = false;
            if (System.OperatingSystem.IsWindows()) {
                hasConsole = ConsoleHelper.AttachConsole(dwProcessId: ConsoleHelper.ATTACH_PARENT_PROCESS);
            }

            // 1. Parse Args to separate the Root path from the Mode flags
            ParsedArgs parsedArgs = ParseArguments(args: args);

            // 2. Resolve Root Path
            if (parsedArgs.ExplicitRoot != null) {
                rootPath = parsedArgs.ExplicitRoot;
            } else {
                string foundRoot = TryFindProjectRoot(startDir: System.IO.Directory.GetCurrentDirectory());
                if (!string.IsNullOrEmpty(foundRoot)) {
                    rootPath = foundRoot;
                } else {
                    foundRoot = TryFindProjectRoot(startDir: System.AppContext.BaseDirectory);
                    rootPath = !string.IsNullOrEmpty(foundRoot) ? foundRoot : System.IO.Directory.GetCurrentDirectory();
                }
            }

            isTui = parsedArgs.Remaining.Any(predicate: arg =>
                arg.Equals("--tui", comparisonType: System.StringComparison.OrdinalIgnoreCase));
            isGui = !isTui && (parsedArgs.Remaining.Count == 0 || parsedArgs.Remaining.Any(predicate: arg =>
                arg.Equals("--gui", comparisonType: System.StringComparison.OrdinalIgnoreCase)));
            isCli = !isGui && !isTui;

            // :: Initialize the Logger
            Shared.IO.Diagnostics.Initialize(rootPath: rootPath, isGui: isGui, isTui: isTui);

            // Lock the drain process and flip the flag so future Ctrl+C events route to the live logger
            lock (logLock) {
                diagnosticsInitialized = true;
                foreach (string logEntry in PreinitialDiagnosticsLog) {
                    Shared.IO.Diagnostics.Log(logEntry);
                }

                PreinitialDiagnosticsLog.Clear();
            }

            IScriptActionDispatcher scriptActionDispatcher = new EngineNet.ScriptEngines.ScriptActionDispatcher();
            Shared.IO.Diagnostics.Trace(
                $"Starting EngineNet in {(isGui ? "GUI" : isTui ? "TUI" : "CLI")} mode. Root Path: {rootPath}");

            if ((isTui || isCli) && !hasConsole && System.OperatingSystem.IsWindows()) {
                ConsoleHelper.AllocConsole();
                Shared.IO.Diagnostics.Trace("Allocated new console window for TUI/CLI mode.");
            }

            Shared.State.ConfigureRuntime(
                rootPath: rootPath,
                isGui: isGui,
                isTui: isTui,
                isCli: isCli
            );

            Engine ??= await InitialiseEngine(scriptActionDispatcher: scriptActionDispatcher);
            EngineNet.Interface.MiniEngineFace miniEngine = new Interface.MiniEngine(Engine: Engine);
            InitUI UI = new InitUI();

            if (isGui) {
                Shared.IO.Diagnostics.Trace("Launching GUI Interface...");
                var code = await UI.init(args: args, ui: "gui", miniEngine: miniEngine, cancellationToken: shutdownCancellationController.Token);
                Shared.IO.Diagnostics.Trace($"GUI Interface exited with code: {code}");
                return code;
            }

            if (isTui) {
                Shared.IO.Diagnostics.Trace("Launching TUI Interface...");
                var code = await UI.init(args: args, ui: "tui", miniEngine: miniEngine, cancellationToken: shutdownCancellationController.Token);
                Shared.IO.Diagnostics.Trace($"TUI Interface exited with code: {code}");
                return code;
            }

            if (isCli) {
                Shared.IO.Diagnostics.Trace("Launching CLI Interface...");
                var code = await UI.init(args: args, ui: "cli", miniEngine: miniEngine, cancellationToken: shutdownCancellationController.Token);
                Shared.IO.Diagnostics.Trace($"CLI Interface exited with code: {code}");
                return code;
            }

            Shared.IO.UI.EngineSdk.Error("No valid interface mode selected.");
            Shared.IO.Diagnostics.Bug("No valid interface mode selected.");
            return 1;
        } catch (OperationCanceledException) {
            // all subsequent catches for this event should Throw, allowing them to be handled here
            Shared.IO.Diagnostics.Trace("Operation canceled by user (Ctrl+C).");
            await System.Console.Error.WriteLineAsync("Operation canceled by user (Ctrl+C).");
            return 0;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug("Critical Engine Failure in Main", ex: ex);
            Shared.IO.Diagnostics.Log($"Engine Error: {ex}");
            await System.Console.Error.WriteLineAsync($"Critical Engine Failure: {ex.Message}");
            return 1;
        } finally {
            System.Console.CancelKeyPress -= cancelHandler;
            shutdownCancellationController.Release();
            Shared.IO.Diagnostics.Trace("Shutting down Engine...");
            Shared.IO.Diagnostics.Close();
            System.Console.ResetColor();
            if (System.OperatingSystem.IsWindows()) {
                ConsoleHelper.FreeConsole();
            }
        }
    }


    /* :: :: Main :: END :: */
    // //
    /* :: :: Methods :: START :: */

    // Simple container for parsed results
    private sealed class ParsedArgs {
        public string? ExplicitRoot { get; set; }
        public List<string> Remaining { get; } = new();
    }

    // Walks arguments, extracts --root value, and keeps the rest preserving order
    private static ParsedArgs ParseArguments(string[] args) {
        ParsedArgs result = new ParsedArgs();
        for (int i = 0; i < args.Length; i++) {
            bool isRootFlag = args[i].Equals("--root", comparisonType: System.StringComparison.OrdinalIgnoreCase);
            bool hasRootValue = isRootFlag
                && i + 1 < args.Length
                && !args[i + 1].StartsWith("--", comparisonType: System.StringComparison.Ordinal);

            if (hasRootValue) {
                // Found --root and a non-flag value exists next to it.
                result.ExplicitRoot = args[i + 1];
                i++; // Skip the value argument in the next loop
            } else {
                // Skip loose --root tokens and keep all other arguments in order.
                if (!isRootFlag) {
                    result.Remaining.Add(item: args[i]);
                }
            }
        }
        return result;
    }

    private static string TryFindProjectRoot(string? startDir) {
        try {
            string dir = string.IsNullOrWhiteSpace(startDir) ? string.Empty : System.IO.Path.GetFullPath(path: startDir);
            while (!string.IsNullOrEmpty(dir)) {
                string reg = System.IO.Path.Combine(path1: dir, path2: "EngineApps");
                string games = System.IO.Path.Combine(path1: reg, path2: "Games");
                if (System.IO.Directory.Exists(path: games)) {
                    return dir;
                }

                System.IO.DirectoryInfo? parent = System.IO.Directory.GetParent(path: dir);
                if (parent is null) {
                    break;
                }

                dir = parent.FullName;
            }
        } catch (System.Exception e) {
            Shared.IO.Diagnostics.Bug($"Error finding project root: {e.Message}");
        }
        return string.Empty;
    }

    /// <summary>
    /// Owns the shutdown cancellation source without exposing the disposable source directly to event handlers.
    /// </summary>
    private sealed class ShutdownCancellationController {
        private readonly System.Threading.CancellationTokenSource cancellationTokenSource = new();

        public System.Threading.CancellationToken Token => cancellationTokenSource.Token;

        public void Cancel() {
            cancellationTokenSource.Cancel();
        }

        public void Release() {
            cancellationTokenSource.Dispose();
        }
    }

    /// <summary>
    /// Initialises the engine
    /// </summary>
    private static async System.Threading.Tasks.Task<EngineNet.Core.Engine.IEngineFace> InitialiseEngine(IScriptActionDispatcher scriptActionDispatcher) {
        if (Engine != null) {
            return Engine;
        }

        JsonToolResolver tools = new Core.ExternalTools.JsonToolResolver();
        EngineConfig engineConfig = new EngineConfig();

        Registries _registries = await Core.Utils.Registries.CreateAsync();
        ModuleScanner _scanner = new Core.Utils.ModuleScanner(registries: _registries);

        GameRegistry gameRegistry = new Core.Services.GameRegistry(registries: _registries, scanner: _scanner);

        CommandService _commandService = new Core.Services.CommandService();
        GameLauncher _gameLauncher = new Core.Services.GameLauncher(gameRegistry: gameRegistry, toolResolver: tools, config: engineConfig, commandService: _commandService, scriptActionDispatcher: scriptActionDispatcher);
        OperationsLoader _opsLoader = new Core.Services.OperationsLoader();
        OperationsService _operationsService = new Core.Services.OperationsService(loader: _opsLoader, gameRegistry: gameRegistry);

        Single Single = new Core.Operations.Single(scriptActionDispatcher: scriptActionDispatcher);

        EngineNet.Core.Engine.Engine _engine = new EngineNet.Core.Engine.Engine(
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

    /// <summary>
    /// Helper methods for managing the console window on Windows OS.
    /// This allows the application to attach to the parent console (if launched from CMD/PowerShell) or allocate a new console if needed (e.g. when double-clicked).
    /// It also provides a method to free the console on exit.
    /// This is important for ensuring that TUI/CLI modes have a visible console to interact with, while GUI mode can run without a console window.
    /// </summary>
    public static class ConsoleHelper {
        [System.Runtime.InteropServices.DllImport(dllName: "kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(unmanagedType: System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool AllocConsole();

        [System.Runtime.InteropServices.DllImport(dllName: "kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(unmanagedType: System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool AttachConsole(int dwProcessId);

        [System.Runtime.InteropServices.DllImport(dllName: "kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(unmanagedType: System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool FreeConsole();

        public const int ATTACH_PARENT_PROCESS = -1;
    }

    /* :: :: Methods :: END :: */
    // //
}

internal sealed class InitUI {
    // choose ui, and manage engine, instead of passing engine to ui, this class will manage and expose methods via a child class it passes into the ui
    public async Task<int> init(string[] args, string ui, Interface.MiniEngineFace miniEngine, System.Threading.CancellationToken cancellationToken) {
        try {
            switch (ui) {
                case "gui":
                    // GUI uses the limited mini engine surface; the full engine is only stashed for previewer/bootstrapping.
                    Shared.IO.Diagnostics.Trace("Launching GUI Interface...");
                    return GUI.GuiBootstrapper.Run(miniEngine: miniEngine, cancellationToken: cancellationToken);
                case "tui":
                    Shared.IO.Diagnostics.Trace("Launching TUI Interface...");
                    Terminal.TUI TUI = new Terminal.TUI(engine: miniEngine);
                    return await TUI.RunAsync(cancellationToken: cancellationToken);
                case "cli":
                    Shared.IO.Diagnostics.Trace("Launching CLI Interface...");
                    Terminal.CLI CLI = new Terminal.CLI(engine: miniEngine);
                    return await CLI.RunAsync(args: args, cancellationToken: cancellationToken);
                default:
                    await System.Console.Error.WriteLineAsync($"No valid interface mode selected. Expected 'gui', 'tui', or 'cli', but got '{ui}'.");
                    Shared.IO.Diagnostics.Bug("No valid interface mode selected.");
                    break;
            }

            return 0;
        } catch (OperationCanceledException) {
            Shared.IO.Diagnostics.Trace("exiting ui");
            throw;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Error initializing UI '{ui}': {ex.Message}", ex: ex);
            await System.Console.Error.WriteLineAsync($"Error initializing UI '{ui}': {ex.Message}");
            return 1;
        }
    }
}