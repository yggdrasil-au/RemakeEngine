using System;
using System.Collections.Generic;
using System.IO;
using RemakeEngine.Core;

namespace RemakeEngine.Interface.GUI;

public sealed class TerminalGui
{
    private readonly OperationsEngine _engine;
    public TerminalGui(OperationsEngine engine) => _engine = engine;

    public int Run()
    {
        Console.WriteLine("RemakeEngine GUI (Terminal)");
        Console.WriteLine();

        var games = _engine.ListGames();
        if (games.Count == 0)
        {
            Console.WriteLine("No games found.");
            return 0;
        }

        var gameName = Pick("Select a game:", new List<string>(games.Keys));
        if (!games.TryGetValue(gameName, out var gobj) || gobj is not Dictionary<string, object?> gdict)
        {
            Console.WriteLine("Selected game not found.");
            return 1;
        }
        if (!gdict.TryGetValue("ops_file", out var of) || of is not string opsFile)
        {
            Console.WriteLine("Selected game has no ops_file.");
            return 1;
        }
        var doc = _engine.LoadOperations(opsFile);
        var groups = new List<string>(doc.Keys);
        var group = Pick("Select an operation group:", groups);

        var ops = doc[group];
        var answers = new Dictionary<string, object?>();
        foreach (var op in ops)
            CollectAnswersForOperation(op, answers);

        Console.WriteLine($"\nRunning group '{group}' for game '{gameName}'...\n");
        var ok = _engine.RunOperationGroupAsync(gameName, games, group, ops, answers).GetAwaiter().GetResult();
        Console.WriteLine(ok ? "Success." : "One or more operations failed.");
        return ok ? 0 : 1;
    }

    private static string Pick(string title, IList<string> options)
    {
        while (true)
        {
            Console.WriteLine(title);
            for (int i = 0; i < options.Count; i++)
                Console.WriteLine($"  {i + 1}. {options[i]}");
            Console.Write("Enter number: ");
            var input = Console.ReadLine();
            if (int.TryParse(input, out var idx) && idx >= 1 && idx <= options.Count)
                return options[idx - 1];
            Console.WriteLine("Invalid selection. Try again.\n");
        }
    }

    private static void CollectAnswersForOperation(Dictionary<string, object?> op, Dictionary<string, object?> answers)
    {
        if (!op.TryGetValue("prompts", out var promptsObj) || promptsObj is not IList<object?> prompts)
            return;
        foreach (var p in prompts)
        {
            if (p is not Dictionary<string, object?> prompt) continue;
            var name = prompt.TryGetValue("Name", out var n) ? n?.ToString() ?? "" : "";
            var type = prompt.TryGetValue("type", out var t) ? t?.ToString() ?? "" : "";
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) continue;

            switch (type)
            {
                case "confirm":
                    Console.Write($"{name} [y/N]: ");
                    var c = Console.ReadLine();
                    answers[name] = c != null && c.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);
                    break;

                case "checkbox":
                    Console.WriteLine($"{name} (comma-separated values): ");
                    var line = Console.ReadLine() ?? string.Empty;
                    answers[name] = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;

                case "text":
                default:
                    Console.Write($"{name}: ");
                    answers[name] = Console.ReadLine();
                    break;
            }
        }
    }
}
