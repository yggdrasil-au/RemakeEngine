using System;
using System.Collections.Generic;
using System.IO;
using RemakeEngine.Core;
using RemakeEngine.Interface.CLI;
using RemakeEngine.Interface.GUI;
using RemakeEngine.Tools;
using System.Linq;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var root = GetRootPath(args) ?? Directory.GetCurrentDirectory();
            var configPath = Path.Combine(root, "project.json");

            // Tool resolver: prefer TOOLS_JSON env or Tools/tools.json
            IToolResolver tools = CreateToolResolver(root);

            var engineConfig = new EngineConfig(configPath);
            var engine = new OperationsEngine(root, tools, engineConfig);

            // Modes: 'cli' -> command-line; default -> GUI
            if (args.Length > 0 && string.Equals(args[0], "cli", StringComparison.OrdinalIgnoreCase))
                return new CliApp(engine).Run(args.Skip(1).ToArray());

            return RemakeEngine.Interface.GUI.WinFormsGui.Run(engine);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    private static string? GetRootPath(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--root" && i + 1 < args.Length)
                return args[i + 1];
        }
        return null;
    }

    private static IToolResolver CreateToolResolver(string root)
    {
        var envPath = Environment.GetEnvironmentVariable("TOOLS_JSON");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            return new JsonToolResolver(envPath);

        // Prefer Tools.local.json if present, then Tools.json
        var toolsDir = Path.Combine(root, "Tools");
        var candidates = new[]
        {
            Path.Combine(toolsDir, "Tools.local.json"),
            Path.Combine(toolsDir, "tools.local.json"),
            Path.Combine(toolsDir, "Tools.json"),
            Path.Combine(toolsDir, "tools.json"),
        };
        foreach (var p in candidates)
        {
            if (File.Exists(p))
                return new JsonToolResolver(p);
        }

        return new PassthroughToolResolver();
    }
}
