using RemakeEngine.Core;
using RemakeEngine.Utils;

namespace RemakeEngine.Interface.CLI;

public partial class CliApp {
    private readonly RemakeEngine.Core.OperationsEngine _engine;

    public CliApp(RemakeEngine.Core.OperationsEngine engine) => _engine = engine;

    public int Run(string[] args) {
        // Strip global flags that Program.cs already handled, like --root PATH
        if (args.Length > 0) {
            var list = new List<string>(args);
            for (int i = 0; i < list.Count;) {
                if (list[i] == "--root") {
                    list.RemoveAt(i);
                    if (i < list.Count)
                        list.RemoveAt(i); // remove path following --root
                    continue;
                }
                i++;
            }
            args = list.ToArray();
        }

        if (args.Length == 0) {
            return RunInteractiveMenu();
        }

        var cmd = args[0].ToLowerInvariant();
        switch (cmd) {
			case "help":
			case "-h":
			case "--help":
				PrintHelp();
				return 0;
			case "--cli":
            case "--menu":
                return RunInteractiveMenu();
            case "--list-games":
                return ListGames();
            case "--list-ops":
                return ListOps(GetArg(args, 1, "<game> required for list-ops"));
            default:
                Console.Error.WriteLine($"Unknown command '{args[0]}'.");
                PrintHelp();
                return 2;
        }
    }

    private int ListGames() {
        var games = _engine.ListGames();
        if (games.Count == 0) {
            Console.WriteLine("No games found in RemakeRegistry/Games.");
            return 0;
        }
        foreach (var (name, obj) in games) {
            if (obj is Dictionary<string, object?> dict && dict.TryGetValue("game_root", out var root))
                Console.WriteLine($"- {name}  (root: {root})");
        }
        return 0;
    }

    private void ShowDownloadMenu() {
        while (true) {
            Console.Clear();
            Console.WriteLine("Download module:");
            var items = new List<string> {
                "From registry (RemakeRegistry/register.json)…",
                "From Git URL…",
                "Back"
            };
            Console.WriteLine("? Choose a source:");
            var idx = SelectFromMenu(items);
            if (idx < 0 || items[idx] == "Back")
                return;

            var choice = items[idx];
            if (choice.StartsWith("From registry")) {
                // Load registry entries and list modules
                var regs = _engine.GetRegisteredModules();
                if (regs.Count == 0) {
                    Console.WriteLine("No modules in registry. Press any key to go back…");
                    Console.ReadKey(true);
                    continue;
                }

                var names = regs.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
                names.Add("Back");
                Console.Clear();
                Console.WriteLine("Select a module to download:");
                var mIdx = SelectFromMenu(names);
                if (mIdx < 0 || names[mIdx] == "Back")
                    continue;

                var name = names[mIdx];
                if (!regs.TryGetValue(name, out var obj) || obj is not Dictionary<string, object?> mod) {
                    Console.WriteLine("Invalid module entry. Press any key…");
                    Console.ReadKey(true);
                    continue;
                }
                var url = mod.TryGetValue("url", out var u) ? u?.ToString() : null;
                if (string.IsNullOrWhiteSpace(url)) {
                    Console.WriteLine("Selected module has no URL. Press any key…");
                    Console.ReadKey(true);
                    continue;
                }
                _engine.DownloadModule(url!);
                // After download, return to previous menu so games list can refresh
                return;
            }

            if (choice.StartsWith("From Git URL")) {
                var url = PromptText("Enter Git URL of the module");
                if (!string.IsNullOrWhiteSpace(url))
                    _engine.DownloadModule(url);
                return;
            }
        }
    }

    private bool ExecuteOp(string game, IDictionary<string, object?> games, Dictionary<string, object?> op, Dictionary<string, object?> answers) {
        var type = (op.TryGetValue("script_type", out var st) ? st?.ToString() : null)?.ToLowerInvariant();

        // Use embedded handlers for engine/lua/js to avoid external dependencies
        if (type == "engine" || type == "lua" || type == "js") {
            // Route in-process SDK events to our terminal renderer and suppress raw @@REMAKE@@ lines
            var prevSink = EngineSdk.LocalEventSink;
            var prevMute = EngineSdk.MuteStdoutWhenLocalSink;
            try {
                EngineSdk.LocalEventSink = RemakeEngine.Utils.TerminalUtils.OnEvent;
                EngineSdk.MuteStdoutWhenLocalSink = true;
                return _engine.RunSingleOperationAsync(game, games, op, answers).GetAwaiter().GetResult();
            } finally {
                EngineSdk.LocalEventSink = prevSink;
                EngineSdk.MuteStdoutWhenLocalSink = prevMute;
            }
        }

        // Default: build and execute as external command (e.g., python)
        var parts = _engine.BuildCommand(game, games, op, answers);
        if (parts.Count < 2)
            return false;
        var title = op.TryGetValue("Name", out var n) ? n?.ToString() ?? Path.GetFileName(parts[1]) : Path.GetFileName(parts[1]);
        return _engine.ExecuteCommand(
            parts,
            title,
            onOutput: RemakeEngine.Utils.TerminalUtils.OnOutput,
            onEvent: RemakeEngine.Utils.TerminalUtils.OnEvent,
            stdinProvider: RemakeEngine.Utils.TerminalUtils.StdinProvider,
            envOverrides: new Dictionary<string, object?> { ["TERM"] = "dumb" }
        );
    }

    private static string PromptText(string title) {
        Console.Write($"{title}: ");
        try {
            return Console.ReadLine() ?? string.Empty;
        } catch { return string.Empty; }
    }

    private static int SelectFromMenu(IList<string> items, bool highlightSeparators = false) {
        int index = 0;
        ConsoleKey key;
        do {
            // Render
            Console.CursorVisible = false;
            for (int i = 0; i < items.Count; i++) {
                var line = items[i];
                var isSep = line == "---------------";
                if (i == index) {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"> {line}");
                    Console.ResetColor();
                } else {
                    if (isSep && highlightSeparators) {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"  {line}");
                        Console.ResetColor();
                    } else {
                        Console.WriteLine($"  {line}");
                    }
                }
            }

            var keyInfo = Console.ReadKey(true);
            key = keyInfo.Key;
            if (key == ConsoleKey.DownArrow) {
                do {
                    index = (index + 1) % items.Count;
                } while (items[index] == "---------------");
            } else if (key == ConsoleKey.UpArrow) {
                do {
                    index = (index - 1 + items.Count) % items.Count;
                } while (items[index] == "---------------");
            } else if (key == ConsoleKey.Escape) {
                return -1;
            }

            // Move cursor up to re-render menu over the same lines
            Console.SetCursorPosition(0, Console.CursorTop - items.Count);
        } while (key != ConsoleKey.Enter);

        Console.CursorVisible = true;
        return index;
    }

    private int ListOps(string game) {
        var games = _engine.ListGames();
        if (!games.TryGetValue(game, out var g)) {
            Console.Error.WriteLine($"Game '{game}' not found.");
            return 1;
        }
        var opsFile = (g is Dictionary<string, object?> gdict && gdict.TryGetValue("ops_file", out var of) && of is string s) ? s : throw new ArgumentException($"Game '{game}' missing ops_file.");
        var doc = _engine.LoadOperations(opsFile);
        foreach (var group in doc.Keys)
            Console.WriteLine(group);
        return 0;
    }


    private static void CollectAnswersForOperation(Dictionary<string, object?> op, Dictionary<string, object?> answers, bool defaultsOnly) {
        if (!op.TryGetValue("prompts", out var promptsObj) || promptsObj is not IList<object?> prompts)
            return;

        foreach (var p in prompts) {
            if (p is not Dictionary<string, object?> prompt)
                continue;
            var name = prompt.TryGetValue("Name", out var n) ? n?.ToString() ?? "" : "";
            var type = prompt.TryGetValue("type", out var t) ? t?.ToString() ?? "" : "";
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type))
                continue;

            if (defaultsOnly) {
                // Use explicit default when available; otherwise leave unset
                if (prompt.TryGetValue("default", out var defVal))
                    answers[name] = defVal;
                continue;
            }

            switch (type) {
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

    private static void PrintHelp() {
        Console.WriteLine(@"RemakeEngine CLI (C#)
			Usage:
			engine [--root PATH] --menu
			engine [--root PATH] --list-games
			engine [--root PATH] --list-ops <game>
			engine [--root PATH] --gui

			Environment:
			TOOLS_JSON   Path to tools mapping JSON (defaults to Tools/tools.json)
		");
    }

    private static string GetArg(string[] args, int index, string error) {
        if (args.Length <= index)
            throw new ArgumentException(error);
        return args[index];
    }
}
