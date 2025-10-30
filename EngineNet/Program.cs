using System.Diagnostics;
using System.Linq;
using Avalonia;

namespace EngineNet;

internal static class Program {

    /* :: :: Vars :: START :: */
    // ensure Avalonia VS preview can find GUI
    internal static AppBuilder BuildAvaloniaApp() => Interface.GUI.AvaloniaGui.BuildAvaloniaApp();

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
            Trace.WriteLine($"[EngineNet] Logging started at {System.DateTimeOffset.Now:u}");
        } catch (System.Exception ex) {
            System.Console.WriteLine($"WARN: Failed to initialize debug log '{logPath ?? "debug.log"}': {ex.Message}");
        }
        try {
            string root = GetRootPath(args) ?? TryFindProjectRoot(System.IO.Directory.GetCurrentDirectory())
                                            ?? TryFindProjectRoot(System.AppContext.BaseDirectory)
                                            ?? System.IO.Directory.GetCurrentDirectory();
            string configPath = System.IO.Path.Combine(root, "project.json");

            // Auto-create a minimal project.json if missing
            if (!System.IO.File.Exists(configPath)) {
                try {
                    System.IO.Directory.CreateDirectory(root);

                    string minimal = $$"""
                    {
                        "RemakeEngine": {
                            "Config": {
                                "project_path": "{{ root.Replace("\\", "\\\\") }}"
                            }
                        }
                    }
                    """;

                    await System.IO.File.WriteAllTextAsync(configPath, minimal);
                    System.Console.ForegroundColor = System.ConsoleColor.DarkYellow;
                    System.Console.WriteLine($"Created default project.json at {configPath}");
                    System.Console.ResetColor();
                } catch (System.Exception ex) {
                    System.Console.WriteLine($"WARN: Could not create project.json - {ex.Message}");
                }
            }

            Core.Tools.IToolResolver tools = CreateToolResolver(root);

            Core.EngineConfig engineConfig = new Core.EngineConfig(configPath);
            Core.OperationsEngine engine = new Core.OperationsEngine(root, tools, engineConfig);

            // Interface selection:
            // - GUI if no args or ONLY arg is --gui
            // - Otherwise CLI (CLI handles additional args itself)
            bool onlyGuiFlag = args.Length == 1 && string.Equals(args[0], "--gui", System.StringComparison.OrdinalIgnoreCase);
            if (args.Length == 0 || onlyGuiFlag) {
                return Interface.GUI.AvaloniaGui.Run(engine);
            }

            // Interface selection:
            // - TUI if ONLY arg is --tui
            // - Otherwise CLI (CLI handles additional args itself)
            bool onlyTuiFlag = args.Length == 1 && string.Equals(args[0], "--tui", System.StringComparison.OrdinalIgnoreCase);
            if (onlyTuiFlag) {
                return await new Interface.Terminal.TUI(engine).RunInteractiveMenuAsync();
            }

            // if not gui run CLIApp with all args, it then uses CLI or TUI as needed
            return await new Interface.Terminal.CLI(engine).Run(args);
        } catch (System.Exception ex) {
            // Print full exception (message + stack trace) to help diagnose runtime errors
            System.Console.WriteLine($"Engine Error: {ex}");
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
            System.Console.WriteLine($"Error finding project root: {e.Message}");
        }
        return null;
    }

    // Create a tool resolver based on available config files
    private static Core.Tools.IToolResolver CreateToolResolver(string root) {
        // Prefer Tools.local.json if present, then Tools.json
        string EngineAppsDir = System.IO.Path.Combine(root, "EngineApps");
        string[] candidates = new[] {
            System.IO.Path.Combine(root, "Tools.local.json"), System.IO.Path.Combine(root, "tools.local.json"),
            System.IO.Path.Combine(EngineAppsDir, "Tools.json"), System.IO.Path.Combine(EngineAppsDir, "tools.json"),
        };
        string? found = candidates.FirstOrDefault(System.IO.File.Exists);
        return !string.IsNullOrEmpty(found) ? new Core.Tools.JsonToolResolver(found) : new Core.Tools.PassthroughToolResolver();
    }

    /* :: :: Methods :: END :: */
    // //
}
