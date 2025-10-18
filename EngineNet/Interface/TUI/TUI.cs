
namespace EngineNet.Interface.TUI;

public class App {

    private readonly Core.OperationsEngine _engine;

    public App(Core.OperationsEngine engine) {
        _engine = engine;
    }

    public Int32 RunInteractiveMenu() {
        // 1) Pick a game, or offer to download a module if none exist
        Dictionary<String, Object?> games = _engine.ListGames();
        while (games.Count == 0) {
            Console.Clear();
            Console.WriteLine("No games found in RemakeRegistry/Games.");
            List<String> actions = new List<String> { "Download module…", "Exit" };
            Console.WriteLine("? Choose an action:");
            Int32 aidx = SelectFromMenu(actions);
            if (aidx < 0 || actions[aidx] == "Exit") {
                return 0;
            }

            if (actions[aidx].StartsWith("Download")) {
                ShowDownloadMenu();
                games = _engine.ListGames();
            }
        }
        // Allow managing modules from the game selection menu
        String gameName;
        while (true) {
            Console.Clear();
            Console.WriteLine("Select a game:");
            List<String> gameMenu = new List<String>(games.Keys);
            gameMenu.Add("---------------");
            gameMenu.Add("Download module…");
            gameMenu.Add("Exit");
            Int32 gidx = SelectFromMenu(gameMenu, highlightSeparators: true);
            if (gidx < 0 || gameMenu[gidx] == "Exit") {
                return 0;
            }

            String gsel = gameMenu[gidx];
            if (gsel.StartsWith("Download module")) {
                ShowDownloadMenu();
                games = _engine.ListGames();
                continue; // show game list again
            }
            gameName = gsel;
            break;
        }

        // 2) Load operations list and render menu
        if (!games.TryGetValue(gameName, out Object? infoObj) || infoObj is not Dictionary<String, Object?> info) {
            Console.Error.WriteLine("Selected game not found.");
            return 1;
        }
        if (!info.TryGetValue("ops_file", out Object? of) || of is not String opsFile) {
            Console.Error.WriteLine("Selected game is missing ops_file.");
            return 1;
        }
        List<Dictionary<String, Object?>> allOps = _engine.LoadOperationsList(opsFile);
        List<Dictionary<String, Object?>> initOps = allOps.FindAll(op => op.TryGetValue("init", out Object? i) && i is Boolean b && b);
        List<Dictionary<String, Object?>> regularOps = allOps.FindAll(op => !op.ContainsKey("init") || !(op["init"] is Boolean bb && bb));
        Boolean didRunInit = false;

        // Auto-run init operations once when a game is selected
        if (initOps.Count > 0) {
            Console.Clear();
            Console.WriteLine($"Running {initOps.Count} initialization operation(s) for {gameName}\n");
            Boolean okAllInit = true;
            foreach (Dictionary<String, Object?> op in initOps) {
                Dictionary<String, Object?> answers = new Dictionary<String, Object?>();
                // Initialization runs non-interactively; use defaults when provided
                CollectAnswersForOperation(op, answers, defaultsOnly: true);
                Boolean ok = new Utils().ExecuteOp(_engine, gameName, games, op, answers);
                okAllInit &= ok;
            }
            didRunInit = true;
            Console.WriteLine(okAllInit
                ? "Initialization completed successfully. Press any key to continue…"
                : "One or more init operations failed. Press any key to continue…");
            Console.ReadKey(true);
        }

        while (true) {
            Console.Clear();
            Console.WriteLine($"--- Operations for: {gameName}");
            List<String> menu = new List<String>();
            menu.Add("Run All");
            menu.Add("---------------");
            foreach (Dictionary<String, Object?> op in regularOps) {
                String name = op.TryGetValue("Name", out Object? n) && n is String s && !String.IsNullOrWhiteSpace(s)
                    ? s : Path.GetFileName(op.TryGetValue("script", out Object? sc) ? sc?.ToString() ?? "(unnamed)" : "(unnamed)");
                menu.Add(name);
            }
            menu.Add("---------------");
            menu.Add("Change Game");
            menu.Add("Exit");

            Console.WriteLine("? Select an operation: (Use arrow keys)");
            Int32 idx = SelectFromMenu(menu, highlightSeparators: true);
            if (idx < 0) {
                return 0; // canceled
            }

            String selection = menu[idx];
            if (selection == "Change Game") {
                // Restart the full menu loop by re-picking game
                return RunInteractiveMenu();
            }
            if (selection == "Exit") {
                return 0;
            }
            if (selection == "Run All") {
                // Collect prompts for all selected ops: init + run-all flagged
                List<Dictionary<String, Object?>> runAll = new List<Dictionary<String, Object?>>();
                if (!didRunInit) {
                    runAll.AddRange(initOps);
                }

                foreach (Dictionary<String, Object?> op in regularOps) {
                    if (op.TryGetValue("run-all", out Object? ra) && ra is Boolean rb && rb) {
                        runAll.Add(op);
                    }
                }

                Console.Clear();
                Console.WriteLine($"Running {runAll.Count} operations for {gameName}…\n");
                Boolean okAll = true;
                foreach (Dictionary<String, Object?> op in runAll) {
                    Dictionary<String, Object?> answers = new Dictionary<String, Object?>();
                    // In run-all mode, do not prompt; prefer defaults when available
                    CollectAnswersForOperation(op, answers, defaultsOnly: true);
                    Boolean ok = new Utils().ExecuteOp(_engine, gameName, games, op, answers);
                    okAll &= ok;
                }
                Console.WriteLine(okAll ? "Completed successfully. Press any key to continue…" : "One or more operations failed. Press any key to continue…");
                Console.ReadKey(true);
                continue;
            }

            // Otherwise, run a single operation (by index within regular ops)
            Int32 opIndex = idx - 2; // skip first two menu items
            if (opIndex >= 0 && opIndex < regularOps.Count) {
                Dictionary<String, Object?> op = regularOps[opIndex];
                Dictionary<String, Object?> answers = new Dictionary<String, Object?>();
                // For manual single-op run, prompt interactively
                CollectAnswersForOperation(op, answers, defaultsOnly: false);
                Console.Clear();
                Console.WriteLine($"Running: {selection}\n");
                Boolean ok = new Utils().ExecuteOp(_engine, gameName, games, op, answers);
                Console.WriteLine(ok ? "Completed successfully. Press any key to continue…" : "Operation failed. Press any key to continue…");
                Console.ReadKey(true);
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

    private static String PromptText(String title) {
        Console.Write($"{title}: ");
        try {
            return Console.ReadLine() ?? String.Empty;
        } catch { return String.Empty; }
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
                    break;
                }

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
                    break;
                }

                case "text":
                default: {
                    Console.Write($"{name}: ");
                    String? v = Console.ReadLine();
                    answers[name] = String.IsNullOrEmpty(v) && prompt.TryGetValue("default", out Object? defVal) ? defVal : v;
                    break;
                }
            }
        }
    }






}
