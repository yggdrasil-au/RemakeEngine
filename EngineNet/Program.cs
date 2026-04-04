using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;

using EngineNet.Core.Abstractions;
using EngineNet.Core.Data;

namespace EngineNet;

using System.Threading;

public static class Program {

    private static EngineNet.Core.Engine.IEngineFace? Engine {get; set;}
    private static string rootPath {get; set;} = string.Empty;
    private static bool isGui {get; set;}
    private static bool isTui {get; set;}
    private static bool isCli {get; set;}

    /* :: :: Vars :: START :: */
    public static AppBuilder BuildAvaloniaApp()  {
        return Interface.GUI.GuiBootstrapper.BuildAvaloniaApp();
    }

    /* :: :: Vars :: END :: */
    // //
    /* :: :: Main :: START :: */
    [STAThread]
    public static async System.Threading.Tasks.Task<int> Main(string[] args) {
        var shutdownCancellationController = new ShutdownCancellationController();

        System.ConsoleCancelEventHandler cancelHandler = (_, e) => {
            e.Cancel = true;
            shutdownCancellationController.Cancel();
            Shared.IO.Diagnostics.Log("Global Cancellation Requested (Ctrl+C)");
        };
        System.Console.CancelKeyPress += cancelHandler;

        try {
            // Try to attach to the parent console (CMD/PowerShell) so stdout works.
            bool hasConsole = false;
            if (System.OperatingSystem.IsWindows()) {
                hasConsole = ConsoleHelper.AttachConsole(ConsoleHelper.ATTACH_PARENT_PROCESS);
            }

            // 1. Parse Args to separate the Root path from the Mode flags
            ParsedArgs parsedArgs = ParseArguments(args);

            // 2. Resolve Root Path
            if (parsedArgs.ExplicitRoot != null) {
                rootPath = parsedArgs.ExplicitRoot;
            } else {
                string foundRoot = TryFindProjectRoot(System.IO.Directory.GetCurrentDirectory());
                if (!string.IsNullOrEmpty(foundRoot)) {
                    rootPath = foundRoot;
                } else {
                    foundRoot = TryFindProjectRoot(System.AppContext.BaseDirectory);
                    rootPath = !string.IsNullOrEmpty(foundRoot) ? foundRoot : System.IO.Directory.GetCurrentDirectory();
                }
            }

            isTui = parsedArgs.Remaining.Any(arg => arg.Equals("--tui", System.StringComparison.OrdinalIgnoreCase));
            isGui = !isTui && (parsedArgs.Remaining.Count == 0 || parsedArgs.Remaining.Any(arg => arg.Equals("--gui", System.StringComparison.OrdinalIgnoreCase)));
            isCli = !isGui && !isTui;

            // :: Initialize the Logger
            Shared.IO.Diagnostics.Initialize(rootPath, isGui, isTui);

            IScriptActionDispatcher scriptActionDispatcher = new EngineNet.ScriptEngines.ScriptActionDispatcher();

            Shared.IO.Diagnostics.Trace($"Starting EngineNet in {(isGui ? "GUI" : isTui ? "TUI" : "CLI")} mode. Root Path: {rootPath}");

            // If user requested TUI/CLI but we failed to attach (e.g. shortcut double-click),
            // we must manually allocate a new console window or they will see nothing.
            if ((isTui || isCli) && !hasConsole && System.OperatingSystem.IsWindows()) {
                ConsoleHelper.AllocConsole();
                Shared.IO.Diagnostics.Trace("Allocated new console window for TUI/CLI mode.");
            }

            Core.Main.ConfigureRuntime(
                rootPath: rootPath,
                isGui: isGui,
                isTui: isTui,
                isCli: isCli,
                engineFactory: () => InitialiseEngine(scriptActionDispatcher)
            );

            Engine ??= await InitialiseEngine(scriptActionDispatcher);

            EngineNet.Interface.Main UI = new Interface.Main(Engine);

            // Logic:
            // - No remaining args -> GUI
            // - One arg "--gui" -> GUI
            if (isGui) {
                Shared.IO.Diagnostics.Trace("Launching GUI Interface...");
                //return Interface.GUI.GuiBootstrapper.Run(Engine); // ;; gui flow step1 ;;
                return await UI.init(args, "gui", shutdownCancellationController.Token);
            }

            // Logic:
            // - One arg "--tui" -> TUI
            if (isTui) {
                Shared.IO.Diagnostics.Trace("Launching TUI Interface...");
                //Interface.Terminal.TUI TUI = new Interface.Terminal.TUI(Engine);
                //return await TUI.RunInteractiveMenuAsync(shutdownCancellationController.Token);
                return await UI.init(args, "tui", shutdownCancellationController.Token);
            }

            // Logic:
            // - Anything else -> CLI (Pass original args so CLI can parse specific commands like 'build', 'run', etc.)
            if (isCli) {
                Shared.IO.Diagnostics.Trace("Launching CLI Interface...");
                //Interface.Terminal.CLI CLI = new Interface.Terminal.CLI(Engine);
                //return await CLI.RunAsync(args, shutdownCancellationController.Token);
                return await UI.init(args, "cli", shutdownCancellationController.Token);
            }
            Shared.IO.UI.EngineSdk.Error("No valid interface mode selected.");
            Shared.IO.Diagnostics.Bug("No valid interface mode selected.");
            return 1;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug("Critical Engine Failure in Main", ex);
            Shared.IO.Diagnostics.Log($"Engine Error: {ex}");
            await System.Console.Error.WriteLineAsync($"Critical Engine Failure: {ex.Message}");
            return 1;
        } finally {
            System.Console.CancelKeyPress -= cancelHandler;
            shutdownCancellationController.Release();
            Shared.IO.Diagnostics.Close();
            // :: Detach console on exit
            if (System.OperatingSystem.IsWindows()) {
                ConsoleHelper.FreeConsole();
            }
        }
    }

    /* :: :: Main :: END :: */
    // //
    /* :: :: Methods :: START :: */

    // Simple container for parsed results
    private class ParsedArgs {
        public string? ExplicitRoot { get; set; }
        public List<string> Remaining { get; } = new List<string>();
    }

    // Walks arguments, extracts --root value, and keeps the rest preserving order
    private static ParsedArgs ParseArguments(string[] args) {
        var result = new ParsedArgs();
        for (int i = 0; i < args.Length; i++) {
            bool isRootFlag = args[i].Equals("--root", System.StringComparison.OrdinalIgnoreCase);

            if (isRootFlag && i + 1 < args.Length) {
                // Found --root and a value exists next to it
                result.ExplicitRoot = args[i + 1];
                i++; // Skip the value argument in the next loop
            }
            else {
                // Determine if this is a loose --root without a value (CLI error case usually, but we treat as arg here)
                // or just a normal argument
                if (!isRootFlag) {
                    result.Remaining.Add(args[i]);
                }
            }
        }
        return result;
    }

    private static string TryFindProjectRoot(string? startDir) {
        try {
            string dir = string.IsNullOrWhiteSpace(startDir) ? string.Empty : System.IO.Path.GetFullPath(startDir);
            while (!string.IsNullOrEmpty(dir)) {
                string reg = System.IO.Path.Combine(dir, "EngineApps");
                string games = System.IO.Path.Combine(reg, "Games");
                if (System.IO.Directory.Exists(games)) {
                    return dir;
                }

                System.IO.DirectoryInfo? parent = System.IO.Directory.GetParent(dir);
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
        private readonly System.Threading.CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

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

        var tools = new Core.ExternalTools.JsonToolResolver();
        var engineConfig = new EngineConfig();

        var _registries = await Core.Utils.Registries.CreateAsync();
        var _scanner = new Core.Utils.ModuleScanner(_registries);

        var gameRegistry = new Core.Services.GameRegistry(_registries, _scanner);

        var _commandService = new Core.Services.CommandService();
        var _gameLauncher = new Core.Services.GameLauncher(gameRegistry, tools, engineConfig, _commandService, scriptActionDispatcher);
        var _opsLoader = new Core.Services.OperationsLoader();
        var _operationsService = new Core.Services.OperationsService(_opsLoader, gameRegistry);

        var Single = new Core.Operations.Single(scriptActionDispatcher);

        EngineNet.Core.Engine.Engine _engine = new EngineNet.Core.Engine.Engine(
            gameRegistry,
            _gameLauncher,
            _opsLoader,
            _commandService,
            _operationsService,

            tools,

            engineConfig,

            Single
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
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool AllocConsole();

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool AttachConsole(int dwProcessId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool FreeConsole();

        public const int ATTACH_PARENT_PROCESS = -1;
    }

    /* :: :: Methods :: END :: */
    // //
}