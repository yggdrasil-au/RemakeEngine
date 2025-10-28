namespace EngineNet.Interface.TUI;

internal class App {

    private readonly Core.OperationsEngine _engine;

    public App(Core.OperationsEngine engine) {
        _engine = engine;
    }

    public async System.Threading.Tasks.Task<int> RunInteractiveMenuAsync() {
        // 1) Pick a game, or offer to download a module if none exist
        Dictionary<string, object?> games = _engine.ListGames();
        while (games.Count == 0) {
            System.Console.Clear();
            System.Console.WriteLine("No games found in RemakeRegistry/Games.");
            List<string> actions = new List<string> { "Download module...", "Exit" };
            System.Console.WriteLine("? Choose an action:");
            int aidx = SelectFromMenu(actions);
            if (aidx < 0 || actions[aidx] == "Exit") {
                return 0;
            }

            if (actions[aidx].StartsWith("Download")) {
                ShowDownloadMenu();
                games = _engine.ListGames();
            }
        }
        // Allow managing modules from the game selection menu
        string gameName;
        while (true) {
            System.Console.Clear();
            System.Console.WriteLine("Select a game:");
            List<string> gameMenu = new List<string>(games.Keys);
            gameMenu.Add("---------------");
            gameMenu.Add("Download module...");
            gameMenu.Add("Exit");
            int gidx = SelectFromMenu(gameMenu, highlightSeparators: true);
            if (gidx < 0 || gameMenu[gidx] == "Exit") {
                return 0;
            }

            string gsel = gameMenu[gidx];
            if (gsel.StartsWith("Download module")) {
                ShowDownloadMenu();
                games = _engine.ListGames();
                continue; // show game list again
            }
            gameName = gsel;
            break;
        }

        // 2) Load operations list and render menu
        if (!games.TryGetValue(gameName, out object? infoObj) || infoObj is not Dictionary<string, object?> info) {
            System.Console.Error.WriteLine("Selected game not found.");
            return 1;
        }
        if (!info.TryGetValue("ops_file", out object? of) || of is not string opsFile) {
            System.Console.Error.WriteLine("Selected game is missing ops_file.");
            return 1;
        }
        List<Dictionary<string, object?>> allOps = _engine.LoadOperationsList(opsFile);
        List<Dictionary<string, object?>> initOps = allOps.FindAll(op => op.TryGetValue("init", out object? i) && i is bool b && b);
        List<Dictionary<string, object?>> regularOps = allOps.FindAll(op => !op.ContainsKey("init") || !(op["init"] is bool bb && bb));
        bool didRunInit = false;

        // Auto-run init operations once when a game is selected
        if (initOps.Count > 0) {
            System.Console.Clear();
            System.Console.WriteLine($"Running {initOps.Count} initialization operation(s) for {gameName}\n");
            bool okAllInit = true;
            foreach (Dictionary<string, object?> op in initOps) {
                Dictionary<string, object?> answers = new Dictionary<string, object?>();
                // Initialization runs non-interactively; use defaults when provided
                CollectAnswersForOperation(op, answers, defaultsOnly: true);
                bool ok = new Utils().ExecuteOp(_engine, gameName, games, op, answers);
                okAllInit &= ok;
            }
            didRunInit = true;
            System.Console.WriteLine(okAllInit
                ? "Initialization completed successfully. Press any key to continue..."
                : "One or more init operations failed. Press any key to continue...");
            System.Console.ReadKey(true);
        }

        while (true) {
            System.Console.Clear();
            System.Console.WriteLine($"--- Operations for: {gameName}");
            List<string> menu = new List<string>();
            menu.Add("Run All");
            menu.Add("---------------");
            foreach (Dictionary<string, object?> op in regularOps) {
                string name = op.TryGetValue("Name", out object? n) && n is string s && !string.IsNullOrWhiteSpace(s)
                    ? s : System.IO.Path.GetFileName(op.TryGetValue("script", out object? sc) ? sc?.ToString() ?? "(unnamed)" : "(unnamed)");
                menu.Add(name);
            }
            menu.Add("---------------");
            menu.Add("Change Game");
            menu.Add("Exit");

            System.Console.WriteLine("? Select an operation: (Use arrow keys)");
            int idx = SelectFromMenu(menu, highlightSeparators: true);
            if (idx < 0) {
                return 0; // canceled
            }

            string selection = menu[idx];
            if (selection == "Change Game") {
                // Restart the full menu loop by re-picking game
                return await RunInteractiveMenuAsync();
            }
            if (selection == "Exit") {
                return 0;
            }
            if (selection == "Run All") {
                try {
                    System.Console.Clear();
                    System.Console.WriteLine($"Running operations for {gameName}...\n");

                    string lastPrompt = "Input required";
                    Core.RunAllResult result = await _engine.RunAllAsync(
                        gameName,
                        onOutput: (line, streamName) => {
                            System.ConsoleColor prev = System.Console.ForegroundColor;
                            try {
                                System.Console.ForegroundColor = streamName == "stderr" ? System.ConsoleColor.Red : System.ConsoleColor.Gray;
                                System.Console.WriteLine(line);
                            } finally {
                                System.Console.ForegroundColor = prev;
                            }
                        },
                        onEvent: (evt) => {
                            if (!evt.TryGetValue("event", out object? evtType)) {
                                return;
                            }

                            string? typ = evtType?.ToString();
                            System.ConsoleColor prev = System.Console.ForegroundColor;

                            switch (typ) {
                                case "print":
                                    string msg = evt.TryGetValue("message", out object? m) ? m?.ToString() ?? string.Empty : string.Empty;
                                    string colorName = evt.TryGetValue("color", out object? c) ? c?.ToString() ?? string.Empty : string.Empty;
                                    bool newline = true;
                                    try {
                                        if (evt.TryGetValue("newline", out object? nl) && nl is not null) {
                                            newline = System.Convert.ToBoolean(nl);
                                        }
                                    } catch {
                                        // ignored
                                    }
                                    try {
                                        System.Console.ForegroundColor = MapEventColor(colorName);
                                        if (newline) {
                                            System.Console.WriteLine(msg);
                                        } else {
                                            System.Console.Write(msg);
                                        }
                                    } finally {
                                        System.Console.ForegroundColor = prev;
                                    }
                                    break;
                                case "prompt":
                                    lastPrompt = evt.TryGetValue("message", out object? pm) ? pm?.ToString() ?? "Input required" : "Input required";
                                    System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                                    System.Console.WriteLine($"? {lastPrompt}");
                                    System.Console.ForegroundColor = prev;
                                    break;
                                case "warning":
                                    System.Console.ForegroundColor = System.ConsoleColor.Yellow;
                                    System.Console.WriteLine($"⚠ {evt.GetValueOrDefault("message", "")}");
                                    System.Console.ForegroundColor = prev;
                                    break;
                                case "error":
                                    System.Console.ForegroundColor = System.ConsoleColor.Red;
                                    System.Console.WriteLine($"✖ {evt.GetValueOrDefault("message", "")}");
                                    System.Console.ForegroundColor = prev;
                                    break;
                            }
                        },
                        stdinProvider: null  // Let System.Console.ReadLine() work normally for TUI
                    );

                    didRunInit = true; // Mark init as done after run-all completes

                    System.Console.WriteLine(result.Success
                        ? $"Completed successfully. ({result.SucceededOperations}/{result.TotalOperations} operations succeeded). Press any key to continue..."
                        : $"One or more operations failed. ({result.SucceededOperations}/{result.TotalOperations} operations succeeded). Press any key to continue...");
                    System.Console.ReadKey(true);
                    continue;
                } catch (System.Exception ex) {
                    System.Console.WriteLine($"Error during Run All: {ex.Message}");
                }
            }

            // Otherwise, run a single operation (by index within regular ops)
            int opIndex = idx - 2; // skip first two menu items
            if (opIndex >= 0 && opIndex < regularOps.Count) {
                Dictionary<string, object?> op = regularOps[opIndex];
                Dictionary<string, object?> answers = new Dictionary<string, object?>();
                // For manual single-op run, prompt interactively
                CollectAnswersForOperation(op, answers, defaultsOnly: false);
                System.Console.Clear();
                System.Console.WriteLine($"Running: {selection}\n");
                bool ok = new Utils().ExecuteOp(_engine, gameName, games, op, answers);
                System.Console.WriteLine(ok ? "Completed successfully. Press any key to continue..." : "Operation failed. Press any key to continue...");
                System.Console.ReadKey(true);
            }
        }
    }

    private static bool CanUseInteractiveMenu(int itemCount) {
        try {
            if (System.Console.IsOutputRedirected || System.Console.IsInputRedirected) {
                return false;
            }

            int bufferHeight = System.Console.BufferHeight;
            int windowHeight = System.Console.WindowHeight;
            int cursorTop = System.Console.CursorTop;

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


    private static int SelectFromMenu(IList<string> items, bool highlightSeparators = false) {
        if (items.Count == 0) {
            return -1;
        }

        if (!CanUseInteractiveMenu(items.Count)) {
            return SelectFromMenuFallback(items, highlightSeparators);
        }

        int index = 0;
        int renderTop = System.Console.CursorTop;

        while (true) {
            System.Console.CursorVisible = false;

            try {
                System.Console.SetCursorPosition(0, renderTop);
            } catch (System.ArgumentOutOfRangeException) {
                System.Console.CursorVisible = true;
                return SelectFromMenuFallback(items, highlightSeparators);
            }

            for (int i = 0; i < items.Count; i++) {
                string line = items[i];
                bool isSep = line == "---------------";
                if (i == index) {
                    System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                    System.Console.WriteLine($"> {line}");
                    System.Console.ResetColor();
                } else {
                    if (isSep && highlightSeparators) {
                        System.Console.ForegroundColor = System.ConsoleColor.DarkGray;
                        System.Console.WriteLine($"  {line}");
                        System.Console.ResetColor();
                    } else {
                        System.Console.WriteLine($"  {line}");
                    }
                }
            }

            System.ConsoleKeyInfo keyInfo = System.Console.ReadKey(true);
            switch (keyInfo.Key) {
                case System.ConsoleKey.DownArrow:
                    do {
                        index = (index + 1) % items.Count;
                    } while (items[index] == "---------------");
                    break;
                case System.ConsoleKey.UpArrow:
                    do {
                        index = (index - 1 + items.Count) % items.Count;
                    } while (items[index] == "---------------");
                    break;
                case System.ConsoleKey.Escape:
                    System.Console.CursorVisible = true;
                    return -1;
                case System.ConsoleKey.Enter:
                    System.Console.CursorVisible = true;
                    return index;
            }
        }
    }
    private static int SelectFromMenuFallback(IList<string> items, bool highlightSeparators) {
        List<int> selectable = new();

        System.Console.WriteLine();
        System.Console.WriteLine("Terminal is too small for the interactive menu. Enter the option number instead:");

        int displayIndex = 1;
        for (int i = 0; i < items.Count; i++) {
            string line = items[i];
            bool isSep = line == "---------------";
            if (isSep) {
                if (highlightSeparators) {
                    System.Console.ForegroundColor = System.ConsoleColor.DarkGray;
                    System.Console.WriteLine(line);
                    System.Console.ResetColor();
                } else {
                    System.Console.WriteLine(line);
                }
                continue;
            }

            System.Console.WriteLine($"{displayIndex}. {line}");
            selectable.Add(i);
            displayIndex++;
        }

        while (true) {
            System.Console.Write("Selection (blank to cancel): ");
            string? input = System.Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) {
                return -1;
            }

            if (int.TryParse(input.Trim(), out int choice) && choice >= 1 && choice <= selectable.Count) {
                return selectable[choice - 1];
            }

            System.Console.WriteLine("Invalid selection. Please enter a valid number.");
        }
    }

    // git Download Menu
    private void ShowDownloadMenu() {
        while (true) {
            System.Console.Clear();
            System.Console.WriteLine("Download module:");
            List<string> items = new List<string> {
                "From registry (RemakeRegistry/register.json)...",
                "From Git URL...",
                "Back"
            };
            System.Console.WriteLine("? Choose a source:");
            int idx = SelectFromMenu(items);
            if (idx < 0 || items[idx] == "Back") {
                return;
            }

            string choice = items[idx];
            if (choice.StartsWith("From registry")) {
                // Load registry entries and list modules
                IReadOnlyDictionary<string, object?> regs = _engine.GetRegisteredModules();
                if (regs.Count == 0) {
                    System.Console.WriteLine("No modules in registry. Press any key to go back...");
                    System.Console.ReadKey(true);
                    continue;
                }

                List<string> names = regs.Keys.OrderBy(k => k, System.StringComparer.OrdinalIgnoreCase).ToList();
                names.Add("Back");
                System.Console.Clear();
                System.Console.WriteLine("Select a module to download:");
                int mIdx = SelectFromMenu(names);
                if (mIdx < 0 || names[mIdx] == "Back") {
                    continue;
                }

                string name = names[mIdx];
                if (!regs.TryGetValue(name, out object? obj) || obj is not Dictionary<string, object?> mod) {
                    System.Console.WriteLine("Invalid module entry. Press any key...");
                    System.Console.ReadKey(true);
                    continue;
                }
                string? url = mod.TryGetValue("url", out object? u) ? u?.ToString() : null;
                if (string.IsNullOrWhiteSpace(url)) {
                    System.Console.WriteLine("Selected module has no URL. Press any key...");
                    System.Console.ReadKey(true);
                    continue;
                }
                _engine.DownloadModule(url!);
                // After download, return to previous menu so games list can refresh
                return;
            }

            if (choice.StartsWith("From Git URL")) {
                string url = PromptText("Enter Git URL of the module");
                if (!string.IsNullOrWhiteSpace(url)) {
                    _engine.DownloadModule(url);
                }

                return;
            }
        }
    }

    private static string PromptText(string title) {
        System.Console.Write($"{title}: ");
        try {
            return System.Console.ReadLine() ?? string.Empty;
        } catch { return string.Empty; }
    }

    private static void CollectAnswersForOperation(Dictionary<string, object?> op, Dictionary<string, object?> answers, bool defaultsOnly) {
        if (!op.TryGetValue("prompts", out object? promptsObj) || promptsObj is not IList<object?> prompts) {
            return;
        }

        // Helper to set an empty value based on prompt type
        static object? EmptyForType(string t) => t switch {
            "confirm" => false,
            "checkbox" => new List<object?>(),
            _ => null
        };

        if (defaultsOnly) {
            // In defaultsOnly mode, we don't prompt. Apply defaults while respecting conditions.
            foreach (object? p in prompts) {
                if (p is not Dictionary<string, object?> prompt) {
                    continue;
                }

                string name = prompt.TryGetValue("Name", out object? n) ? n?.ToString() ?? "" : "";
                string type = prompt.TryGetValue("type", out object? t) ? t?.ToString() ?? "" : "";
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) {
                    continue;
                }

                // Evaluate condition if present using current 'answers' state
                if (prompt.TryGetValue("condition", out object? condObj) && condObj is string condName) {
                    if (!answers.TryGetValue(condName, out object? condVal)) {
                        // If condition value not yet present, attempt to seed from its default (if a matching prompt exists earlier or later)
                        // Find the prompt with Name == condName and use its default if any
                        foreach (object? q in prompts) {
                            if (q is Dictionary<string, object?> qp && (qp.TryGetValue("Name", out object? qn) ? qn?.ToString() : null) == condName) {
                                if (!answers.ContainsKey(condName) && qp.TryGetValue("default", out object? cd)) {
                                    answers[condName] = cd;
                                }

                                break;
                            }
                        }
                    }
                    if (!answers.TryGetValue(condName, out object? cv) || cv is not bool cb || !cb) {
                        // Condition is false -> set empty value and skip
                        answers[name] = EmptyForType(type);
                        continue;
                    }
                }

                answers[name] = prompt.TryGetValue("default", out object? defVal) ? defVal : EmptyForType(type);
            }
            return;
        }

        // Interactive mode: walk prompts in order, honoring conditions
        foreach (object? p in prompts) {
            if (p is not Dictionary<string, object?> prompt) {
                continue;
            }

            string name = prompt.TryGetValue("Name", out object? n) ? n?.ToString() ?? "" : "";
            string type = prompt.TryGetValue("type", out object? tt) ? tt?.ToString() ?? "" : "";
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) {
                continue;
            }

            // If there's a condition and it's false, skip asking and assign an empty value
            if (prompt.TryGetValue("condition", out object? cond) && cond is string condName) {
                if (!answers.TryGetValue(condName, out object? condVal) || condVal is not bool b || !b) {
                    answers[name] = EmptyForType(type);
                    continue;
                }
            }

            switch (type) {
                case "confirm": {
                    // Show default hint when available
                    string defHint = prompt.TryGetValue("default", out object? dv) && dv is bool db ? (db ? "Y" : "N") : "N";
                    System.Console.Write($"{name} [y/N] (default {defHint}): ");
                    string? c = System.Console.ReadLine();
                    bool val = c != null && c.Trim().Length > 0
                        ? c.Trim().StartsWith("y", System.StringComparison.OrdinalIgnoreCase)
                        : (prompt.TryGetValue("default", out object? d) && d is bool bd && bd);
                    answers[name] = val;
                    break;
                }

                case "checkbox": {
                    // Present choices if available
                    if (prompt.TryGetValue("choices", out object? ch) && ch is IList<object?> choices && choices.Count > 0) {
                        System.Console.WriteLine($"{name} - choose one or more (comma-separated). Choices: {string.Join(", ", choices.Select(x => x?.ToString()))}");
                    } else {
                        System.Console.WriteLine($"{name} (comma-separated values): ");
                    }
                    string line = System.Console.ReadLine() ?? string.Empty;
                    List<object?> selected = line.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries).Cast<object?>().ToList();
                    // If user entered nothing, fall back to default if provided
                    if (selected.Count == 0 && prompt.TryGetValue("default", out object? def) && def is IList<object?> defList) {
                        selected = defList.Select(x => x).ToList();
                    }

                    answers[name] = selected;
                    break;
                }

                case "text":
                default: {
                    System.Console.Write($"{name}: ");
                    string? v = System.Console.ReadLine();
                    answers[name] = string.IsNullOrEmpty(v) && prompt.TryGetValue("default", out object? defVal) ? defVal : v;
                    break;
                }
            }
        }
    }

    /* :: :: Helper Methods :: START :: */

    private static System.ConsoleColor MapEventColor(string? name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return System.ConsoleColor.Gray;
        }

        return name.Trim().ToLowerInvariant() switch {
            "default" => System.ConsoleColor.Gray,
            "black" => System.ConsoleColor.Black,
            "darkblue" => System.ConsoleColor.DarkBlue,
            "blue" => System.ConsoleColor.Blue,
            "darkgreen" => System.ConsoleColor.DarkGreen,
            "green" => System.ConsoleColor.Green,
            "darkcyan" => System.ConsoleColor.DarkCyan,
            "cyan" => System.ConsoleColor.Cyan,
            "darkred" => System.ConsoleColor.DarkRed,
            "red" => System.ConsoleColor.Red,
            "darkmagenta" => System.ConsoleColor.DarkMagenta,
            "magenta" => System.ConsoleColor.Magenta,
            "darkyellow" => System.ConsoleColor.DarkYellow,
            "yellow" => System.ConsoleColor.Yellow,
            "gray" or "grey" => System.ConsoleColor.Gray,
            "darkgray" or "darkgrey" => System.ConsoleColor.DarkGray,
            "white" => System.ConsoleColor.White,
            _ => System.ConsoleColor.Gray
        };
    }

    /* :: :: Helper Methods :: END :: */
}
