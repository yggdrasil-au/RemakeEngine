using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EngineNet.Interface.CLI;

public partial class CliApp {
    private readonly Core.OperationsEngine _engine;

    public CliApp(Core.OperationsEngine engine) => _engine = engine;

    public Int32 Run(String[] args) {
        // Strip global flags that Program.cs already handled, like --root PATH
        if (args.Length > 0) {
            List<String> list = new List<String>(args);
            for (Int32 i = 0; i < list.Count;) {
                if (list[i] == "--root") {
                    list.RemoveAt(i);
                    if (i < list.Count) {
                        list.RemoveAt(i); // remove path following --root
                    }

                    continue;
                }
                i++;
            }
            args = list.ToArray();
        }

        if (args.Length == 0) {
            return RunInteractiveMenu();
        }

        if (IsInlineOperationInvocation(args)) {
            return RunInlineOperation(args);
        }

        String cmd = args[0].ToLowerInvariant();
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

    private Int32 ListGames() {
        Dictionary<String, Object?> games = _engine.ListGames();
        if (games.Count == 0) {
            Console.WriteLine("No games found in RemakeRegistry/Games.");
            return 0;
        }
        foreach ((String name, Object? obj) in games) {
            if (obj is Dictionary<String, Object?> dict && dict.TryGetValue("game_root", out Object? root)) {
                Console.WriteLine($"- {name}  (root: {root})");
            }
        }
        return 0;
    }

    private void ShowDownloadMenu() {
        while (true) {
            Console.Clear();
            Console.WriteLine("Download module:");
            List<String> items = new List<String> {
                "From registry (RemakeRegistry/register.json)…",
                "From Git URL…",
                "Back"
            };
            Console.WriteLine("? Choose a source:");
            Int32 idx = SelectFromMenu(items);
            if (idx < 0 || items[idx] == "Back") {
                return;
            }

            String choice = items[idx];
            if (choice.StartsWith("From registry")) {
                // Load registry entries and list modules
                IReadOnlyDictionary<String, Object?> regs = _engine.GetRegisteredModules();
                if (regs.Count == 0) {
                    Console.WriteLine("No modules in registry. Press any key to go back…");
                    Console.ReadKey(true);
                    continue;
                }

                List<String> names = regs.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
                names.Add("Back");
                Console.Clear();
                Console.WriteLine("Select a module to download:");
                Int32 mIdx = SelectFromMenu(names);
                if (mIdx < 0 || names[mIdx] == "Back") {
                    continue;
                }

                String name = names[mIdx];
                if (!regs.TryGetValue(name, out Object? obj) || obj is not Dictionary<String, Object?> mod) {
                    Console.WriteLine("Invalid module entry. Press any key…");
                    Console.ReadKey(true);
                    continue;
                }
                String? url = mod.TryGetValue("url", out Object? u) ? u?.ToString() : null;
                if (String.IsNullOrWhiteSpace(url)) {
                    Console.WriteLine("Selected module has no URL. Press any key…");
                    Console.ReadKey(true);
                    continue;
                }
                _engine.DownloadModule(url!);
                // After download, return to previous menu so games list can refresh
                return;
            }

            if (choice.StartsWith("From Git URL")) {
                String url = PromptText("Enter Git URL of the module");
                if (!String.IsNullOrWhiteSpace(url)) {
                    _engine.DownloadModule(url);
                }

                return;
            }
        }
    }

    private Boolean ExecuteOp(String game, IDictionary<String, Object?> games, Dictionary<String, Object?> op, Dictionary<String, Object?> answers, Dictionary<String, String>? autoPromptResponses = null) {
        String? type = (op.TryGetValue("script_type", out Object? st) ? st?.ToString() : null)?.ToLowerInvariant();

        // Use embedded handlers for engine/lua/js/bms to avoid external dependencies
        if (type == "engine" || type == "lua" || type == "js" || type == "bms") {
            // Route in-process SDK events to our terminal renderer and suppress raw @@REMAKE@@ lines
            Action<Dictionary<String, Object?>>? prevSink = Core.ScriptEngines.Helpers.EngineSdk.LocalEventSink;
            Boolean prevMute = Core.ScriptEngines.Helpers.EngineSdk.MuteStdoutWhenLocalSink;
            Dictionary<String, String> prevAutoResponses = new(Core.ScriptEngines.Helpers.EngineSdk.AutoPromptResponses);
            try {
                // Set auto-prompt responses if provided
                if (autoPromptResponses != null && autoPromptResponses.Count > 0) {
                    Core.ScriptEngines.Helpers.EngineSdk.AutoPromptResponses.Clear();
                    foreach (KeyValuePair<String, String> kv in autoPromptResponses) {
                        Core.ScriptEngines.Helpers.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
                    }
                }
                
                Core.ScriptEngines.Helpers.EngineSdk.LocalEventSink = TerminalUtils.OnEvent;
                Core.ScriptEngines.Helpers.EngineSdk.MuteStdoutWhenLocalSink = true;
                return _engine.RunSingleOperationAsync(game, games, op, answers).GetAwaiter().GetResult();
            } finally {
                // Restore previous auto-prompt responses
                Core.ScriptEngines.Helpers.EngineSdk.AutoPromptResponses.Clear();
                foreach (KeyValuePair<String, String> kv in prevAutoResponses) {
                    Core.ScriptEngines.Helpers.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
                }
                
                Core.ScriptEngines.Helpers.EngineSdk.LocalEventSink = prevSink;
                Core.ScriptEngines.Helpers.EngineSdk.MuteStdoutWhenLocalSink = prevMute;
            }
        }

        // Default: build and execute as external command (e.g., python)
        List<String> parts = _engine.BuildCommand(game, games, op, answers);
        if (parts.Count < 2) {
            return false;
        }

        String title = op.TryGetValue("Name", out Object? n) ? n?.ToString() ?? Path.GetFileName(parts[1]) : Path.GetFileName(parts[1]);
        return _engine.ExecuteCommand(
            parts,
            title,
            onOutput: TerminalUtils.OnOutput,
            onEvent: TerminalUtils.OnEvent,
            stdinProvider: TerminalUtils.StdinProvider,
            envOverrides: new Dictionary<String, Object?> { ["TERM"] = "dumb" }
        );
    }

    private static String PromptText(String title) {
        Console.Write($"{title}: ");
        try {
            return Console.ReadLine() ?? String.Empty;
        } catch { return String.Empty; }
    }

    private static Int32 SelectFromMenu(IList<String> items, Boolean highlightSeparators = false) {
        if (items.Count == 0) {
            return -1;
        }

        if (!CanUseInteractiveMenu(items.Count)) {
            return SelectFromMenuFallback(items, highlightSeparators);
        }

        Int32 index = 0;
        Int32 renderTop = Console.CursorTop;

        while (true) {
            Console.CursorVisible = false;

            try {
                Console.SetCursorPosition(0, renderTop);
            } catch (ArgumentOutOfRangeException) {
                Console.CursorVisible = true;
                return SelectFromMenuFallback(items, highlightSeparators);
            }

            for (Int32 i = 0; i < items.Count; i++) {
                String line = items[i];
                Boolean isSep = line == "---------------";
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

            ConsoleKeyInfo keyInfo = Console.ReadKey(true);
            switch (keyInfo.Key) {
                case ConsoleKey.DownArrow:
                    do {
                        index = (index + 1) % items.Count;
                    } while (items[index] == "---------------");
                    break;
                case ConsoleKey.UpArrow:
                    do {
                        index = (index - 1 + items.Count) % items.Count;
                    } while (items[index] == "---------------");
                    break;
                case ConsoleKey.Escape:
                    Console.CursorVisible = true;
                    return -1;
                case ConsoleKey.Enter:
                    Console.CursorVisible = true;
                    return index;
            }
        }
    }

    private static Boolean CanUseInteractiveMenu(Int32 itemCount) {
        try {
            if (Console.IsOutputRedirected || Console.IsInputRedirected) {
                return false;
            }

            Int32 bufferHeight = Console.BufferHeight;
            Int32 windowHeight = Console.WindowHeight;
            Int32 cursorTop = Console.CursorTop;

            if (itemCount >= bufferHeight) {
                return false;
            }

            if (cursorTop + itemCount >= bufferHeight) {
                return false;
            }

            if (itemCount + 1 >= windowHeight) {
                return false;
            }

            return true;
        } catch {
            return false;
        }
    }

    private static Int32 SelectFromMenuFallback(IList<String> items, Boolean highlightSeparators) {
        List<Int32> selectable = new();

        Console.WriteLine();
        Console.WriteLine("Terminal is too small for the interactive menu. Enter the option number instead:");

        Int32 displayIndex = 1;
        for (Int32 i = 0; i < items.Count; i++) {
            String line = items[i];
            Boolean isSep = line == "---------------";
            if (isSep) {
                if (highlightSeparators) {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine(line);
                    Console.ResetColor();
                } else {
                    Console.WriteLine(line);
                }
                continue;
            }

            Console.WriteLine($"{displayIndex}. {line}");
            selectable.Add(i);
            displayIndex++;
        }

        while (true) {
            Console.Write("Selection (blank to cancel): ");
            String? input = Console.ReadLine();
            if (String.IsNullOrWhiteSpace(input)) {
                return -1;
            }

            if (Int32.TryParse(input.Trim(), out Int32 choice) && choice >= 1 && choice <= selectable.Count) {
                return selectable[choice - 1];
            }

            Console.WriteLine("Invalid selection. Please enter a valid number.");
        }
    }

    private Int32 ListOps(String game) {
        Dictionary<String, Object?> games = _engine.ListGames();
        if (!games.TryGetValue(game, out Object? g)) {
            Console.Error.WriteLine($"Game '{game}' not found.");
            return 1;
        }
        String opsFile = (g is Dictionary<String, Object?> gdict && gdict.TryGetValue("ops_file", out Object? of) && of is String s) ? s : throw new ArgumentException($"Game '{game}' missing ops_file.");
        Dictionary<String, List<Dictionary<String, Object?>>> doc = _engine.LoadOperations(opsFile);
        foreach (String group in doc.Keys) {
            Console.WriteLine(group);
        }

        return 0;
    }

    private static void CollectAnswersForOperation(Dictionary<String, Object?> op, Dictionary<String, Object?> answers, Boolean defaultsOnly) {
        if (!op.TryGetValue("prompts", out Object? promptsObj) || promptsObj is not IList<Object?> prompts) {
            return;
        }

        // Helper to set an empty value based on prompt type
        static Object? EmptyForType(String t) => t switch {
            "confirm" => false,
            "checkbox" => new List<Object?>(),
            _ => null
        };

        if (defaultsOnly) {
            // In defaultsOnly mode, we don't prompt. Apply defaults while respecting conditions.
            foreach (Object? p in prompts) {
                if (p is not Dictionary<String, Object?> prompt) {
                    continue;
                }

                String name = prompt.TryGetValue("Name", out Object? n) ? n?.ToString() ?? "" : "";
                String type = prompt.TryGetValue("type", out Object? t) ? t?.ToString() ?? "" : "";
                if (String.IsNullOrEmpty(name) || String.IsNullOrEmpty(type)) {
                    continue;
                }

                // Evaluate condition if present using current 'answers' state
                if (prompt.TryGetValue("condition", out Object? condObj) && condObj is String condName) {
                    if (!answers.TryGetValue(condName, out Object? condVal)) {
                        // If condition value not yet present, attempt to seed from its default (if a matching prompt exists earlier or later)
                        // Find the prompt with Name == condName and use its default if any
                        foreach (Object? q in prompts) {
                            if (q is Dictionary<String, Object?> qp && (qp.TryGetValue("Name", out Object? qn) ? qn?.ToString() : null) == condName) {
                                if (!answers.ContainsKey(condName) && qp.TryGetValue("default", out Object? cd)) {
                                    answers[condName] = cd;
                                }

                                break;
                            }
                        }
                    }
                    if (!answers.TryGetValue(condName, out Object? cv) || cv is not Boolean cb || !cb) {
                        // Condition is false -> set empty value and skip
                        answers[name] = EmptyForType(type);
                        continue;
                    }
                }

                answers[name] = prompt.TryGetValue("default", out Object? defVal) ? defVal : EmptyForType(type);
            }
            return;
        }

        // Interactive mode: walk prompts in order, honoring conditions
        foreach (Object? p in prompts) {
            if (p is not Dictionary<String, Object?> prompt) {
                continue;
            }

            String name = prompt.TryGetValue("Name", out Object? n) ? n?.ToString() ?? "" : "";
            String type = prompt.TryGetValue("type", out Object? tt) ? tt?.ToString() ?? "" : "";
            if (String.IsNullOrEmpty(name) || String.IsNullOrEmpty(type)) {
                continue;
            }

            // If there's a condition and it's false, skip asking and assign an empty value
            if (prompt.TryGetValue("condition", out Object? cond) && cond is String condName) {
                if (!answers.TryGetValue(condName, out Object? condVal) || condVal is not Boolean b || !b) {
                    answers[name] = EmptyForType(type);
                    continue;
                }
            }

            switch (type) {
                case "confirm": {
                    // Show default hint when available
                    String defHint = prompt.TryGetValue("default", out Object? dv) && dv is Boolean db ? (db ? "Y" : "N") : "N";
                    Console.Write($"{name} [y/N] (default {defHint}): ");
                    String? c = Console.ReadLine();
                    Boolean val = c != null && c.Trim().Length > 0
                        ? c.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase)
                        : (prompt.TryGetValue("default", out Object? d) && d is Boolean bd && bd);
                    answers[name] = val;
                    break; }

                case "checkbox": {
                    // Present choices if available
                    if (prompt.TryGetValue("choices", out Object? ch) && ch is IList<Object?> choices && choices.Count > 0) {
                        Console.WriteLine($"{name} - choose one or more (comma-separated). Choices: {String.Join(", ", choices.Select(x => x?.ToString()))}");
                    } else {
                        Console.WriteLine($"{name} (comma-separated values): ");
                    }
                    String line = Console.ReadLine() ?? String.Empty;
                    List<Object?> selected = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Cast<Object?>().ToList();
                    // If user entered nothing, fall back to default if provided
                    if (selected.Count == 0 && prompt.TryGetValue("default", out Object? def) && def is IList<Object?> defList) {
                        selected = defList.Select(x => (Object?)x).ToList();
                    }

                    answers[name] = selected;
                    break; }

                case "text":
                default: {
                    Console.Write($"{name}: ");
                    String? v = Console.ReadLine();
                    answers[name] = String.IsNullOrEmpty(v) && prompt.TryGetValue("default", out Object? defVal) ? defVal : v;
                    break; }
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
            engine [--root PATH] --game_module <name|path> --script <action> [--script_type <type>] [--args ""...""]

            Environment:
            TOOLS_JSON   Path to tools mapping JSON (defaults to Tools/tools.json)
        ");
    }

    private static String GetArg(String[] args, Int32 index, String error) {
        return args.Length <= index ? throw new ArgumentException(error) : args[index];
    }
}
