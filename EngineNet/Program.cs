using System;
using System.IO;

namespace EngineNet;

public static class Program {

    /* :: :: Vars :: START :: */
    // ensure Avalonia VS preview can find GUI
    public static AppBuilder BuildAvaloniaApp() => Interface.GUI.AvaloniaGui.BuildAvaloniaApp();

    /* :: :: Vars :: END :: */
    // //
    /* :: :: Main :: START :: */

    public static async System.Threading.Tasks.Task<int> Main(string[] args) {
        try {
            string root = GetRootPath(args) ?? TryFindProjectRoot(System.IO.Directory.GetCurrentDirectory())
                                            ?? TryFindProjectRoot(System.AppContext.BaseDirectory)
                                            ?? System.IO.Directory.GetCurrentDirectory();
            string configPath = System.IO.Path.Combine(root, "project.json");

            // Auto-create a minimal project.json if missing
            if (!System.IO.File.Exists(configPath)) {
                try {
                    System.IO.Directory.CreateDirectory(root);
                    string minimal = "{\n  \"RemakeEngine\": {\n    \"Config\": { \"project_path\": \"" + root.Replace("\\", "\\\\") + "\" },\n    \"Directories\": {},\n    \"Tools\": {}\n  }\n}";
                    await System.IO.File.WriteAllTextAsync(configPath, minimal);
                    Direct.Console.ForegroundColor = System.ConsoleColor.DarkYellow;
                    Program.Direct.Console.WriteLine($"Created default project.json at {configPath}");
                    Direct.Console.ResetColor();
                } catch (System.Exception ex) {
                    Program.Direct.Console.WriteLine($"WARN: Could not create project.json - {ex.Message}");
                }
            }

            Tools.IToolResolver tools = CreateToolResolver(root);

            EngineConfig engineConfig = new EngineConfig(configPath);
            Core.OperationsEngine engine = new Core.OperationsEngine(root, tools, engineConfig);

            // Interface selection:
            // - GUI if no args or ONLY arg is --gui
            // - Otherwise CLI (CLI handles additional args itself)
            bool onlyGuiFlag = args.Length == 1 && string.Equals(args[0], "--gui", System.StringComparison.OrdinalIgnoreCase);
            if (args.Length == 0 || onlyGuiFlag) {
                return Interface.GUI.AvaloniaGui.Run(engine);
            }

            // if not gui run CLIApp with all args, it then uses CLI or TUI as needed
            return await new Interface.CommandLine.App(engine).Run(args);
        } catch (System.Exception ex) {
            // Print full exception (message + stack trace) to help diagnose runtime errors
            Program.Direct.Console.WriteLine($"Engine Error: {ex}");
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

    // Walk upwards from a starting directory to find a folder containing RemakeRegistry/Games, this is the project root
    private static string? TryFindProjectRoot(string? startDir) {
        try {
            string? dir;
            if (string.IsNullOrWhiteSpace(startDir)) {
                dir = null;
            } else {
                dir = System.IO.Path.GetFullPath(startDir!);
            }
            while (!string.IsNullOrEmpty(dir)) {
                string reg = System.IO.Path.Combine(dir!, "RemakeRegistry");
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
            Program.Direct.Console.WriteLine($"Error finding project root: {e.Message}");
        }
        return null;
    }

    // Create a tool resolver based on available config files
    private static Tools.IToolResolver CreateToolResolver(string root) {
        // Prefer Tools.local.json if present, then Tools.json
        string RemakeRegistryDir = System.IO.Path.Combine(root, "RemakeRegistry");
        string[] candidates = new[] {
            System.IO.Path.Combine(root, "Tools.local.json"), System.IO.Path.Combine(root, "tools.local.json"),
            System.IO.Path.Combine(RemakeRegistryDir, "Tools.json"), System.IO.Path.Combine(RemakeRegistryDir, "tools.json"),
        };
        string? found = candidates.FirstOrDefault(System.IO.File.Exists);
        return !string.IsNullOrEmpty(found) ? new Tools.JsonToolResolver(found) : new Tools.PassthroughToolResolver();
    }

    internal static class Direct {
        internal static class Console {
            internal static global::System.ConsoleColor ForegroundColor {
                get => global::System.Console.ForegroundColor;
                set => global::System.Console.ForegroundColor = value;
            }

            // Clear
            internal static void Clear() =>
                global::System.Console.Clear();
            internal static string? ReadLine() =>
                global::System.Console.ReadLine();

            internal static void WriteLine(string message = "") {
                global::System.Console.WriteLine(message);
            }
            internal static void Write(char message) =>
                global::System.Console.Write(message);

            internal static void Write(string message) =>
                global::System.Console.Write(message);

            internal static void ResetColor() =>
                global::System.Console.ResetColor();

            internal static global::System.IO.TextReader In => global::System.Console.In;
            internal static global::System.IO.TextWriter Out => global::System.Console.Out;

            internal static void SetCursorPosition(int left, int top) =>
                global::System.Console.SetCursorPosition(left, top);

            internal static void SetIn(global::System.IO.TextReader reader) =>
                global::System.Console.SetIn(reader);


            internal static int WindowWidth {
                get => global::System.Console.WindowWidth;
            }

            internal static int CursorTop {
                get => global::System.Console.CursorTop;
                set => global::System.Console.CursorTop = value;
            }

        }
    }


    /* :: :: Methods :: END :: */
    // //
}
