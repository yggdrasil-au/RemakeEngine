using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;

// Allow 'internal' access for tests
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(assemblyName: "EngineNet.Tests")]

namespace EngineNet;

internal static class Program {

    /* :: :: Vars :: START :: */
    // ensure Avalonia VS preview can find GUI
    internal static AppBuilder BuildAvaloniaApp()  {
        return Interface.GUI.AvaloniaGui.BuildAvaloniaApp();
    }
    /* :: :: Vars :: END :: */
    // //
    /* :: :: Main :: START :: */

    internal static async System.Threading.Tasks.Task<int> Main(string[] args) {
        string? logPath = null;
        try {
            logPath = System.IO.Path.Combine(System.Environment.CurrentDirectory, "debug.log");
            string? logDirectory = System.IO.Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(logDirectory)) {
                System.IO.Directory.CreateDirectory(logDirectory);
            }

            Trace.Listeners.Add(new TextWriterTraceListener(logPath));
            Trace.AutoFlush = true;
#if DEBUG
            Trace.WriteLine($"[Program.cs::Main()] Logging started at {System.DateTimeOffset.Now:u}");
#endif
        } catch (System.Exception ex) {
#if DEBUG
            System.Console.Error.WriteLine($"WARN: Failed to initialize debug log '{logPath ?? "debug.log"}': {ex.Message}");
#endif
        }
        try {
            string root;
            string? explicitRoot = GetRootPath(args);
            if (explicitRoot != null) {
#if DEBUG
                Trace.WriteLine($"[Program.cs::Main()] Using explicit root from --root: {explicitRoot}");
#endif
                root = explicitRoot;
            } else {
                string? foundRoot = TryFindProjectRoot(System.IO.Directory.GetCurrentDirectory());
                if (foundRoot != null) {
#if DEBUG
                    Trace.WriteLine($"[Program.cs::Main()] Found project root from current directory: {foundRoot}");
#endif
                    root = foundRoot;
                } else {
                    foundRoot = TryFindProjectRoot(System.AppContext.BaseDirectory);
                    if (foundRoot != null) {
#if DEBUG
                        Trace.WriteLine($"[Program.cs::Main()] Found project root from base directory: {foundRoot}");
#endif
                        root = foundRoot;
                    } else {
#if DEBUG
                        Trace.WriteLine($"[Program.cs::Main()] No project root found, using current directory: {System.IO.Directory.GetCurrentDirectory()}");
#endif
                        root = System.IO.Directory.GetCurrentDirectory();
                    }
                }
            }

            Core.Tools.IToolResolver tools = CreateToolResolver(root);

            Core.EngineConfig engineConfig = new Core.EngineConfig();
            Core.Engine _engine = new Core.Engine(root, tools, engineConfig);

            // Interface selection:
            // - GUI if no args or ONLY arg is --gui
            // - Otherwise CLI (CLI handles additional args itself)
            bool onlyGuiFlag = args.Length == 1 && string.Equals(args[0], "--gui", System.StringComparison.OrdinalIgnoreCase);
            if (args.Length == 0 || onlyGuiFlag) {
                return await Interface.GUI.AvaloniaGui.RunAsync(_engine);
            }

            // Interface selection:
            // - TUI if ONLY arg is --tui
            // - Otherwise CLI (CLI handles additional args itself)
            bool onlyTuiFlag = args.Length == 1 && string.Equals(args[0], "--tui", System.StringComparison.OrdinalIgnoreCase);
            if (onlyTuiFlag) {
                Interface.Terminal.TUI TUI = new Interface.Terminal.TUI(_engine);
                return await TUI.RunInteractiveMenuAsync();
            }

            // if not gui run CLIApp with all args, it then uses CLI or TUI as needed
            Interface.Terminal.CLI CLI = new Interface.Terminal.CLI(_engine);
            return await CLI.RunAsync(args);
        } catch (System.Exception ex) {
#if DEBUG
            Trace.WriteLine($"Engine Error: {ex}");
#endif
            return 1;
        }
    }

    /* :: :: Main :: END :: */
    // //
    /* :: :: Methods :: START :: */

    // Parse --root <path> from args
    private static string? GetRootPath(string[] args) {
        for (int i = 0; i < args.Length; i++) {
            if (args[i] == "--root" && i + 1 < args.Length) {
                return args[i + 1];
            }
        }
        return null;
    }

    // Walk upwards from a starting directory to find a folder containing EngineApps/Games, this is the project root
    private static string? TryFindProjectRoot(string? startDir) {
        try {
            string? dir;
            if (string.IsNullOrWhiteSpace(startDir)) {
                dir = null;
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
#if DEBUG
            Trace.WriteLine($"Error finding project root: {e.Message}");
#endif
        }
        return null;
    }

    // Create a tool resolver based on available config files
    private static Core.Tools.IToolResolver CreateToolResolver(string root) {
        // Prefer Tools.local.json if present, then "EngineApps", "Registries", "Tools", "Main.json"
        string EngineAppsDir = System.IO.Path.Combine(root, "EngineApps");
        string[] candidates = new[] {
            System.IO.Path.Combine(root, "Tools.local.json"), System.IO.Path.Combine(root, "tools.local.json"),
            System.IO.Path.Combine(EngineAppsDir, "EngineApps", "Registries", "Tools", "Main.json"), System.IO.Path.Combine(EngineAppsDir, "EngineApps", "Registries", "Tools", "main.json"),
        };
        string? found = candidates.FirstOrDefault(System.IO.File.Exists);
        return !string.IsNullOrEmpty(found) ? new Core.Tools.JsonToolResolver(found) : new Core.Tools.PassthroughToolResolver();
    }

    /* :: :: Methods :: END :: */
    // //
}
