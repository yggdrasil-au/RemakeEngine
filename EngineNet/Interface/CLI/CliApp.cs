using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RemakeEngine.Core;

namespace RemakeEngine.Interface.CLI;

public sealed class CliApp
{
    private readonly OperationsEngine _engine;

    public CliApp(OperationsEngine engine) => _engine = engine;

    public int Run(string[] args)
    {
        if (args.Length == 0)
        {
            return RunInteractiveMenu();
        }

        if (args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        var cmd = args[0].ToLowerInvariant();
        switch (cmd)
        {
            case "menu":
                return RunInteractiveMenu();
            case "list-games":
                return ListGames();
            case "list-ops":
                return ListOps(GetArg(args, 1, "<game> required for list-ops"));
            case "run":
                return RunGroup(GetArg(args, 1, "<game> required"), GetArg(args, 2, "<group> required"));
            default:
                Console.Error.WriteLine($"Unknown command '{args[0]}'.");
                PrintHelp();
                return 2;
        }
    }

    private int RunInteractiveMenu()
    {
        // 1) Pick a game
        var games = _engine.ListGames();
        if (games.Count == 0)
        {
            Console.WriteLine("No games found in RemakeRegistry/Games.");
            return 0;
        }
        var gameName = Pick("Select a game:", new List<string>(games.Keys));
        if (string.IsNullOrEmpty(gameName)) return 0;

        // 2) Load operations list and render menu
        if (!games.TryGetValue(gameName, out var infoObj) || infoObj is not Dictionary<string, object?> info)
        {
            Console.Error.WriteLine("Selected game not found.");
            return 1;
        }
        if (!info.TryGetValue("ops_file", out var of) || of is not string opsFile)
        {
            Console.Error.WriteLine("Selected game is missing ops_file.");
            return 1;
        }
        var allOps = _engine.LoadOperationsList(opsFile);
        var initOps = allOps.FindAll(op => op.TryGetValue("init", out var i) && i is bool b && b);
        var regularOps = allOps.FindAll(op => !op.ContainsKey("init") || !(op["init"] is bool bb && bb));

        while (true)
        {
            Console.Clear();
            Console.WriteLine($"--- Operations for: {gameName}");
            var menu = new List<string>();
            menu.Add("Run All");
            menu.Add("---------------");
            foreach (var op in regularOps)
            {
                var name = op.TryGetValue("Name", out var n) && n is string s && !string.IsNullOrWhiteSpace(s)
                    ? s : Path.GetFileName(op.TryGetValue("script", out var sc) ? sc?.ToString() ?? "(unnamed)" : "(unnamed)");
                menu.Add(name);
            }
            menu.Add("---------------");
            menu.Add("Change Game");
            menu.Add("Exit");

            Console.WriteLine("? Select an operation: (Use arrow keys)");
            var idx = SelectFromMenu(menu, highlightSeparators: true);
            if (idx < 0) return 0; // canceled

            var selection = menu[idx];
            if (selection == "Change Game")
            {
                // Restart the full menu loop by re-picking game
                return RunInteractiveMenu();
            }
            if (selection == "Exit")
            {
                return 0;
            }
            if (selection == "Run All")
            {
                // Collect prompts for all selected ops: init + run-all flagged
                var runAll = new List<Dictionary<string, object?>>();
                runAll.AddRange(initOps);
                foreach (var op in regularOps)
                    if (op.TryGetValue("run-all", out var ra) && ra is bool rb && rb)
                        runAll.Add(op);

                Console.Clear();
                Console.WriteLine($"Running {runAll.Count} operations for {gameName}…\n");
                var okAll = true;
                foreach (var op in runAll)
                {
                    var answers = new Dictionary<string, object?>();
                    CollectAnswersForOperation(op, answers);
                    var ok = ExecuteOp(gameName, games, op, answers);
                    okAll &= ok;
                }
                Console.WriteLine(okAll ? "Completed successfully. Press any key to continue…" : "One or more operations failed. Press any key to continue…");
                Console.ReadKey(true);
                continue;
            }

            // Otherwise, run a single operation (by index within regular ops)
            var opIndex = idx - 2; // skip first two menu items
            if (opIndex >= 0 && opIndex < regularOps.Count)
            {
                var op = regularOps[opIndex];
                var answers = new Dictionary<string, object?>();
                CollectAnswersForOperation(op, answers);
                Console.Clear();
                Console.WriteLine($"Running: {selection}\n");
                var ok = ExecuteOp(gameName, games, op, answers);
                Console.WriteLine(ok ? "Completed successfully. Press any key to continue…" : "Operation failed. Press any key to continue…");
                Console.ReadKey(true);
            }
        }
    }

    private bool ExecuteOp(string game, IDictionary<string, object?> games, Dictionary<string, object?> op, Dictionary<string, object?> answers)
    {
        var type = (op.TryGetValue("script_type", out var st) ? st?.ToString() : null)?.ToLowerInvariant();
        if (type == "engine")
        {
            return _engine.ExecuteEngineOperationAsync(game, games, op, answers).GetAwaiter().GetResult();
        }
        var parts = _engine.BuildCommand(game, games, op, answers);
        if (parts.Count < 2) return false;
        var title = op.TryGetValue("Name", out var n) ? n?.ToString() ?? Path.GetFileName(parts[1]) : Path.GetFileName(parts[1]);
        return _engine.ExecuteCommand(
            parts,
            title,
            onOutput: OnOutput,
            onEvent: OnEvent,
            stdinProvider: StdinProvider,
            envOverrides: new Dictionary<string, object?> { ["TERM"] = "dumb" }
        );
    }

    private static string Pick(string title, IList<string> options)
    {
        Console.Clear();
        Console.WriteLine(title);
        var idx = SelectFromMenu(options);
        return idx >= 0 && idx < options.Count ? options[idx] : string.Empty;
    }

    private static int SelectFromMenu(IList<string> items, bool highlightSeparators = false)
    {
        int index = 0;
        ConsoleKey key;
        do
        {
            // Render
            Console.CursorVisible = false;
            for (int i = 0; i < items.Count; i++)
            {
                var line = items[i];
                var isSep = line == "---------------";
                if (i == index)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"> {line}");
                    Console.ResetColor();
                }
                else
                {
                    if (isSep && highlightSeparators)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"  {line}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine($"  {line}");
                    }
                }
            }

            var keyInfo = Console.ReadKey(true);
            key = keyInfo.Key;
            if (key == ConsoleKey.DownArrow)
            {
                do { index = (index + 1) % items.Count; } while (items[index] == "---------------");
            }
            else if (key == ConsoleKey.UpArrow)
            {
                do { index = (index - 1 + items.Count) % items.Count; } while (items[index] == "---------------");
            }
            else if (key == ConsoleKey.Escape)
            {
                return -1;
            }

            // Move cursor up to re-render menu over the same lines
            Console.SetCursorPosition(0, Console.CursorTop - items.Count);
        } while (key != ConsoleKey.Enter);

        Console.CursorVisible = true;
        return index;
    }

    // --- Handlers to bridge SDK events <-> CLI ---
    private static string _lastPrompt = "Input required";

    private static void OnOutput(string line, string stream)
    {
        var prev = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = (stream == "stderr") ? ConsoleColor.Red : ConsoleColor.Gray;
            Console.WriteLine(line);
        }
        finally { Console.ForegroundColor = prev; }
    }

    private static void OnEvent(Dictionary<string, object?> evt)
    {
        if (!evt.TryGetValue("event", out var typObj)) return;
        var typ = typObj?.ToString();
        switch (typ)
        {
            case "prompt":
                _lastPrompt = evt.TryGetValue("message", out var m) ? (m?.ToString() ?? "Input required") : "Input required";
                var prev = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"? {_lastPrompt}");
                Console.ForegroundColor = prev;
                break;
            case "warning":
                WriteColored($"⚠ {evt.GetValueOrDefault("message", "")}", ConsoleColor.Yellow);
                break;
            case "error":
                WriteColored($"✖ {evt.GetValueOrDefault("message", "")}", ConsoleColor.Red);
                break;
        }
    }

    private static string? StdinProvider()
    {
        try
        {
            Console.Write("> ");
            return Console.ReadLine();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void WriteColored(string message, ConsoleColor color)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = prev;
    }

    private int ListGames()
    {
        var games = _engine.ListGames();
        if (games.Count == 0)
        {
            Console.WriteLine("No games found in RemakeRegistry/Games.");
            return 0;
        }
        foreach (var (name, obj) in games)
        {
            if (obj is Dictionary<string, object?> dict && dict.TryGetValue("game_root", out var root))
                Console.WriteLine($"- {name}  (root: {root})");
        }
        return 0;
    }

    private int ListOps(string game)
    {
        var games = _engine.ListGames();
        if (!games.TryGetValue(game, out var g))
        {
            Console.Error.WriteLine($"Game '{game}' not found.");
            return 1;
        }
        var opsFile = (g is Dictionary<string, object?> gdict && gdict.TryGetValue("ops_file", out var of) && of is string s) ? s : throw new ArgumentException($"Game '{game}' missing ops_file.");
        var doc = _engine.LoadOperations(opsFile);
        foreach (var group in doc.Keys)
            Console.WriteLine(group);
        return 0;
    }

    private int RunGroup(string game, string group)
    {
        var games = _engine.ListGames();
        if (!games.TryGetValue(game, out var g))
        {
            Console.Error.WriteLine($"Game '{game}' not found.");
            return 1;
        }
        var gdict = (Dictionary<string, object?>)g;
        if (!gdict.TryGetValue("ops_file", out var of) || of is not string opsFile)
        {
            Console.Error.WriteLine($"Game '{game}' missing ops_file.");
            return 1;
        }
        var doc = _engine.LoadOperations(opsFile);
        if (!doc.TryGetValue(group, out var ops))
        {
            Console.Error.WriteLine($"Group '{group}' not in {Path.GetFileName(opsFile)}.");
            return 1;
        }

        // Execute each operation with live output and SDK prompts
        var okAll = true;
        foreach (var op in ops)
        {
            var answers = new Dictionary<string, object?>();
            CollectAnswersForOperation(op, answers);
            var parts = _engine.BuildCommand(game, games, op, answers);
            if (parts.Count < 2) continue;
            var title = op.TryGetValue("Name", out var n) ? n?.ToString() ?? Path.GetFileName(parts[1]) : Path.GetFileName(parts[1]);
            var ok = _engine.ExecuteCommand(
                parts,
                title,
                onOutput: OnOutput,
                onEvent: OnEvent,
                stdinProvider: StdinProvider,
                envOverrides: new Dictionary<string, object?> { ["TERM"] = "dumb" }
            );
            okAll &= ok;
        }
        return okAll ? 0 : 1;
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
                    answers[name] = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Cast<object?>().ToList();
                    break;

                case "text":
                default:
                    Console.Write($"{name}: ");
                    answers[name] = Console.ReadLine();
                    break;
            }
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"RemakeEngine CLI (C#)

Usage:
  engine -- cli                 # interactive menu
  engine -- cli [--root PATH] list-games
  engine -- cli [--root PATH] list-ops <game>
  engine -- cli [--root PATH] run <game> <group>

Environment:
  TOOLS_JSON   Path to tools mapping JSON (defaults to Tools/tools.json)
");
    }

    private static string GetArg(string[] args, int index, string error)
    {
        if (args.Length <= index)
            throw new ArgumentException(error);
        return args[index];
    }
}
