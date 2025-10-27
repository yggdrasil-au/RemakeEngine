
namespace EngineNet;

internal static class Program {

    // ensure Avalonia VS preview can find GUI
    public static AppBuilder BuildAvaloniaApp() => Interface.GUI.AvaloniaGui.BuildAvaloniaApp();


    public static Int32 Main(String[] args) {
        try {
            String root = GetRootPath(args) ?? TryFindProjectRoot(Directory.GetCurrentDirectory())
                                            ?? TryFindProjectRoot(AppContext.BaseDirectory)
                                            ?? Directory.GetCurrentDirectory();
            String configPath = Path.Combine(root, "project.json");

            // Auto-create a minimal project.json if missing
            if (!File.Exists(configPath)) {
                try {
                    Directory.CreateDirectory(root);
                    String minimal = "{\n  \"RemakeEngine\": {\n    \"Config\": { \"project_path\": \"" + root.Replace("\\", "\\\\") + "\" },\n    \"Directories\": {},\n    \"Tools\": {}\n  }\n}";
                    File.WriteAllText(configPath, minimal);
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"Created default project.json at {configPath}");
                    Console.ResetColor();
                } catch (Exception ex) {
                    Console.Error.WriteLine($"WARN: Could not create project.json — {ex.Message}");
                }
            }

            Tools.IToolResolver tools = CreateToolResolver(root);

            EngineConfig engineConfig = new EngineConfig(configPath);
            Core.OperationsEngine engine = new Core.OperationsEngine(root, tools, engineConfig);

            // Interface selection:
            // - GUI if no args or ONLY arg is --gui
            // - Otherwise CLI (CLI handles additional args itself)
            Boolean onlyGuiFlag = args.Length == 1 && String.Equals(args[0], "--gui", StringComparison.OrdinalIgnoreCase);
            if (args.Length == 0 || onlyGuiFlag) {
                return Interface.GUI.AvaloniaGui.Run(engine);
            }

            // if not gui run CLIApp with all args, it then uses CLI or TUI as needed
            return new Interface.CommandLine.App(engine).Run(args);
        } catch (Exception ex) {
            // Print full exception (message + stack trace) to help diagnose runtime errors
            Console.Error.WriteLine($"Engine Error: {ex}");
            return 1;
        }
    }

    internal static String? GetRootPath(String[] args) {
        for (Int32 i = 0; i < args.Length; i++) {
            if (args[i] == "--root" && i + 1 < args.Length) {
                return args[i + 1];
            }
        }
        return null;
    }

    // Walk upwards from a starting directory to find a folder containing RemakeRegistry/Games
    internal static String? TryFindProjectRoot(String? startDir) {
        try {
            String? dir;
            if (String.IsNullOrWhiteSpace(startDir)) {
                dir = null;
            } else {
                dir = Path.GetFullPath(startDir!);
            }
            while (!String.IsNullOrEmpty(dir)) {
                String reg = Path.Combine(dir!, "RemakeRegistry");
                String games = Path.Combine(reg, "Games");
                if (Directory.Exists(games)) {
                    return dir!;
                }

                DirectoryInfo? parent = Directory.GetParent(dir!);
                if (parent is null) {
                    break;
                }

                dir = parent.FullName;
            }
        } catch (Exception e) {
            Console.Error.WriteLine($"Error finding project root: {e.Message}");
        }
        return null;
    }

    internal static Tools.IToolResolver CreateToolResolver(String root) {
        // Prefer Tools.local.json if present, then Tools.json
        String RemakeRegistryDir = Path.Combine(root, "RemakeRegistry");
        String[] candidates = new[] {
            Path.Combine(root, "Tools.local.json"),
            Path.Combine(root, "tools.local.json"),
            Path.Combine(RemakeRegistryDir, "Tools.json"),
            Path.Combine(RemakeRegistryDir, "tools.json"),
        };
        String? found = candidates.FirstOrDefault(File.Exists);
        return !String.IsNullOrEmpty(found) ? new Tools.JsonToolResolver(found) : new Tools.PassthroughToolResolver();
    }
}
