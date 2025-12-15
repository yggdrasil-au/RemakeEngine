using System.Linq;
using System.Collections.Generic;
using Avalonia;

// Allow 'internal' access for tests
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(assemblyName: "EngineNet.Tests")]

namespace EngineNet;

public static class Program {

    public static string rootPath {get; private set;} = string.Empty;

    /* :: :: Vars :: START :: */
    public static AppBuilder BuildAvaloniaApp()  {
        return Interface.GUI.AvaloniaGui.BuildAvaloniaApp();
    }
    /* :: :: Vars :: END :: */
    // //
    /* :: :: Main :: START :: */

    internal static async System.Threading.Tasks.Task<int> Main(string[] args) {
        try {
            // 1. Parse Args to separate the Root path from the Mode flags
            var parsedArgs = ParseArguments(args);

            // 2. Resolve Root Path
            if (parsedArgs.ExplicitRoot != null) {
                rootPath = parsedArgs.ExplicitRoot;
            } else {
                string foundRoot = TryFindProjectRoot(System.IO.Directory.GetCurrentDirectory());
                if (foundRoot != string.Empty) {
                    rootPath = foundRoot;
                } else {
                    foundRoot = TryFindProjectRoot(System.AppContext.BaseDirectory);
                    if (foundRoot != string.Empty) {
                        rootPath = foundRoot;
                    } else {
                        rootPath = System.IO.Directory.GetCurrentDirectory();
                    }
                }
            }

            bool isGui = parsedArgs.Remaining.Count == 0 || (parsedArgs.Remaining.Count == 1 && parsedArgs.Remaining[0].Equals("--gui", System.StringComparison.OrdinalIgnoreCase));
            bool isTui = parsedArgs.Remaining.Count == 1 && parsedArgs.Remaining[0].Equals("--tui", System.StringComparison.OrdinalIgnoreCase);


            // :: Initialize the Logger
            Core.Diagnostics.Initialize(isGui, isTui);
            
            // :: Setup Services
            Core.Tools.IToolResolver tools = CreateToolResolver(rootPath);
            Core.EngineConfig engineConfig = new Core.EngineConfig();
            
            Core.Abstractions.IGameRegistry gameRegistry = new Core.Services.GameRegistry(rootPath);
            Core.Abstractions.IGameLauncher gameLauncher = new Core.Services.GameLauncher(gameRegistry, tools, engineConfig, rootPath);
            Core.Abstractions.IOperationsLoader opsLoader = new Core.Services.OperationsLoader();
            Core.Abstractions.IGitService gitService = new Core.Services.GitService(rootPath);
            Core.Abstractions.ICommandService commandService = new Core.Services.CommandService();

            Core.Engine _engine = new Core.Engine(
                rootPath: rootPath,
                gameRegistry: gameRegistry,
                gameLauncher: gameLauncher,
                operationsLoader: opsLoader,
                gitService: gitService,
                commandService: commandService,
                toolResolver: tools,
                engineConfig: engineConfig
            );

            // 3. Interface selection based on "Remaining Args" (args with --root removed)

            // Logic:
            // - No remaining args -> GUI
            // - One arg "--gui" -> GUI
            if (isGui) {
                return await Interface.GUI.AvaloniaGui.RunAsync(_engine);
            }

            // Logic:
            // - One arg "--tui" -> TUI
            if (isTui) {
                Interface.Terminal.TUI TUI = new Interface.Terminal.TUI(_engine);
                return await TUI.RunInteractiveMenuAsync();
            }

            // Logic:
            // - Anything else -> CLI (Pass original args so CLI can parse specific commands like 'build', 'run', etc.)
            Interface.Terminal.CLI CLI = new Interface.Terminal.CLI(_engine);
            return await CLI.RunAsync(args);
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug("Critical Engine Failure in Main", ex);
            Core.Diagnostics.Log($"Engine Error: {ex}");
            return 1;
        } finally {
            Core.Diagnostics.Close();
        }
    }

    /* :: :: Main :: END :: */
    // //
    /* :: :: Methods :: START :: */

    // Creates the tool resolver based on available config files
    private static Core.Tools.IToolResolver CreateToolResolver(string root) {
        string[] candidates = new[] {
            System.IO.Path.Combine(root, "Tools.local.json"), System.IO.Path.Combine(root, "tools.local.json"),
            System.IO.Path.Combine(root, "EngineApps", "Registries", "Tools", "Main.json"), System.IO.Path.Combine(root, "EngineApps", "Registries", "Tools", "main.json"),
        };
        string? found = candidates.FirstOrDefault(System.IO.File.Exists);
        return !string.IsNullOrEmpty(found) ? new Core.Tools.JsonToolResolver(found) : new Core.Tools.PassthroughToolResolver();
    }

    // Simple container for parsed results
    private class ParsedArgs {
        public string? ExplicitRoot { get; set; }
        public List<string> Remaining { get; set; } = new List<string>();
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
            string dir;
            if (string.IsNullOrWhiteSpace(startDir)) {
                dir = string.Empty;
            } else {
                dir = System.IO.Path.GetFullPath(startDir!);
            }
            while (!string.IsNullOrEmpty(dir)) {
                string reg = System.IO.Path.Combine(dir!, "EngineApps");
                string games = System.IO.Path.Combine(reg, "Games");
                if (System.IO.Directory.Exists(games)) {
                    return dir!;
                }

                System.IO.DirectoryInfo? parent = System.IO.Directory.GetParent(dir!);
                if (parent is null) {
                    break;
                }

                dir = parent.FullName;
            }
        } catch (System.Exception e) {
            Core.Diagnostics.Bug($"Error finding project root: {e.Message}");
        }
        return string.Empty;
    }

    /* :: :: Methods :: END :: */
    // //
}