using System;
using System.Collections.Generic;
using System.IO;
using RemakeEngine.Core;
using RemakeEngine.Interface.CLI;
using RemakeEngine.Tools;
using System.Linq;

namespace RemakeEngine;

internal static class Program {
    public static int Main(string[] args) {
        try {
            var root = GetRootPath(args) ?? TryFindProjectRoot(Directory.GetCurrentDirectory())
                      ?? TryFindProjectRoot(AppContext.BaseDirectory)
                      ?? Directory.GetCurrentDirectory();
            var configPath = Path.Combine(root, "project.json");

            // Auto-create a minimal project.json if missing
            if (!File.Exists(configPath)) {
                try {
                    Directory.CreateDirectory(root);
                    var minimal = "{\n  \"RemakeEngine\": {\n    \"Config\": { \"project_path\": \"" + root.Replace("\\", "\\\\") + "\" },\n    \"Directories\": {},\n    \"Tools\": {}\n  }\n}";
                    File.WriteAllText(configPath, minimal);
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"Created default project.json at {configPath}");
                    Console.ResetColor();
                } catch (Exception ex) {
                    Console.Error.WriteLine($"WARN: Could not create project.json — {ex.Message}");
                }
            }

            // Tool resolver: prefer TOOLS_JSON env or Tools/tools.json
            IToolResolver tools = CreateToolResolver(root);

            var engineConfig = new EngineConfig(configPath);
            var engine = new OperationsEngine(root, tools, engineConfig);

            // Interface selection:
            // - GUI if no args or ONLY arg is --gui
            // - Otherwise CLI (CLI handles additional args itself)
            var onlyGuiFlag = args.Length == 1 && string.Equals(args[0], "--gui", StringComparison.OrdinalIgnoreCase);
            if (args.Length == 0 || onlyGuiFlag) {
                return RemakeEngine.Interface.GUI.Avalonia.AvaloniaGui.Run(engine);
            }

            // Any other args -> CLI. Strip --gui if someone passed it along with others.
            var cliArgs = args.Where(a => !string.Equals(a, "--gui", StringComparison.OrdinalIgnoreCase)).ToArray();
            return new CliApp(engine).Run(cliArgs);
        } catch (Exception ex) {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    private static string? GetRootPath(string[] args) {
        for (int i = 0; i < args.Length; i++) {
            if (args[i] == "--root" && i + 1 < args.Length)
                return args[i + 1];
        }
        return null;
    }

    // Walk upwards from a starting directory to find a folder containing RemakeRegistry/Games
    private static string? TryFindProjectRoot(string? startDir) {
        try {
            var dir = string.IsNullOrWhiteSpace(startDir) ? null : System.IO.Path.GetFullPath(startDir!);
            while (!string.IsNullOrEmpty(dir)) {
                var reg = System.IO.Path.Combine(dir!, "RemakeRegistry");
                var games = System.IO.Path.Combine(reg, "Games");
                if (Directory.Exists(games))
                    return dir!;
                var parent = Directory.GetParent(dir!);
                if (parent is null) break;
                dir = parent.FullName;
            }
        } catch { /* ignore */ }
        return null;
    }

    private static IToolResolver CreateToolResolver(string root) {
        var envPath = Environment.GetEnvironmentVariable("TOOLS_JSON");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            return new JsonToolResolver(envPath);

        // Prefer Tools.local.json if present, then Tools.json
        var RemakeRegistryDir = Path.Combine(root, "RemakeRegistry");
        var candidates = new[]
        {
            Path.Combine(root, "Tools.local.json"),
            Path.Combine(root, "tools.local.json"),
            Path.Combine(RemakeRegistryDir, "Tools.json"),
            Path.Combine(RemakeRegistryDir, "tools.json"),
        };
        var found = candidates.FirstOrDefault(File.Exists);
        if (!string.IsNullOrEmpty(found))
            return new JsonToolResolver(found);

        return new PassthroughToolResolver();
    }
}
