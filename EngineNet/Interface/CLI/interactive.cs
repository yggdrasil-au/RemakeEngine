namespace RemakeEngine.Interface.CLI;

public partial class CliApp {
    private int RunInteractiveMenu() {
        // 1) Pick a game, or offer to download a module if none exist
        var games = _engine.ListGames();
        while (games.Count == 0) {
            Console.Clear();
            Console.WriteLine("No games found in RemakeRegistry/Games.");
            var actions = new List<string> { "Download module…", "Exit" };
            Console.WriteLine("? Choose an action:");
            var aidx = SelectFromMenu(actions);
            if (aidx < 0 || actions[aidx] == "Exit")
                return 0;
            if (actions[aidx].StartsWith("Download")) {
                ShowDownloadMenu();
                games = _engine.ListGames();
            }
        }
        // Allow managing modules from the game selection menu
        string gameName;
        while (true) {
            Console.Clear();
            Console.WriteLine("Select a game:");
            var gameMenu = new List<string>(games.Keys);
            gameMenu.Add("---------------");
            gameMenu.Add("Download module…");
            gameMenu.Add("Exit");
            var gidx = SelectFromMenu(gameMenu, highlightSeparators: true);
            if (gidx < 0 || gameMenu[gidx] == "Exit")
                return 0;
            var gsel = gameMenu[gidx];
            if (gsel.StartsWith("Download module")) {
                ShowDownloadMenu();
                games = _engine.ListGames();
                continue; // show game list again
            }
            gameName = gsel;
            break;
        }

        // 2) Load operations list and render menu
        if (!games.TryGetValue(gameName, out var infoObj) || infoObj is not Dictionary<string, object?> info) {
            Console.Error.WriteLine("Selected game not found.");
            return 1;
        }
        if (!info.TryGetValue("ops_file", out var of) || of is not string opsFile) {
            Console.Error.WriteLine("Selected game is missing ops_file.");
            return 1;
        }
        var allOps = _engine.LoadOperationsList(opsFile);
        var initOps = allOps.FindAll(op => op.TryGetValue("init", out var i) && i is bool b && b);
        var regularOps = allOps.FindAll(op => !op.ContainsKey("init") || !(op["init"] is bool bb && bb));
        var didRunInit = false;

        // Auto-run init operations once when a game is selected
        if (initOps.Count > 0) {
            Console.Clear();
            Console.WriteLine($"Running {initOps.Count} initialization operation(s) for {gameName}\n");
            var okAllInit = true;
            foreach (var op in initOps) {
                var answers = new Dictionary<string, object?>();
                // Initialization runs non-interactively; use defaults when provided
                CollectAnswersForOperation(op, answers, defaultsOnly: true);
                var ok = ExecuteOp(gameName, games, op, answers);
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
            var menu = new List<string>();
            menu.Add("Run All");
            menu.Add("---------------");
            foreach (var op in regularOps) {
                var name = op.TryGetValue("Name", out var n) && n is string s && !string.IsNullOrWhiteSpace(s)
                    ? s : Path.GetFileName(op.TryGetValue("script", out var sc) ? sc?.ToString() ?? "(unnamed)" : "(unnamed)");
                menu.Add(name);
            }
            menu.Add("---------------");
            menu.Add("Change Game");
            menu.Add("Exit");

            Console.WriteLine("? Select an operation: (Use arrow keys)");
            var idx = SelectFromMenu(menu, highlightSeparators: true);
            if (idx < 0)
                return 0; // canceled

            var selection = menu[idx];
            if (selection == "Change Game") {
                // Restart the full menu loop by re-picking game
                return RunInteractiveMenu();
            }
            if (selection == "Exit") {
                return 0;
            }
            if (selection == "Run All") {
                // Collect prompts for all selected ops: init + run-all flagged
                var runAll = new List<Dictionary<string, object?>>();
                if (!didRunInit)
                    runAll.AddRange(initOps);
                foreach (var op in regularOps)
                    if (op.TryGetValue("run-all", out var ra) && ra is bool rb && rb)
                        runAll.Add(op);

                Console.Clear();
                Console.WriteLine($"Running {runAll.Count} operations for {gameName}…\n");
                var okAll = true;
                foreach (var op in runAll) {
                    var answers = new Dictionary<string, object?>();
                    // In run-all mode, do not prompt; prefer defaults when available
                    CollectAnswersForOperation(op, answers, defaultsOnly: true);
                    var ok = ExecuteOp(gameName, games, op, answers);
                    okAll &= ok;
                }
                Console.WriteLine(okAll ? "Completed successfully. Press any key to continue…" : "One or more operations failed. Press any key to continue…");
                Console.ReadKey(true);
                continue;
            }

            // Otherwise, run a single operation (by index within regular ops)
            var opIndex = idx - 2; // skip first two menu items
            if (opIndex >= 0 && opIndex < regularOps.Count) {
                var op = regularOps[opIndex];
                var answers = new Dictionary<string, object?>();
                // For manual single-op run, prompt interactively
                CollectAnswersForOperation(op, answers, defaultsOnly: false);
                Console.Clear();
                Console.WriteLine($"Running: {selection}\n");
                var ok = ExecuteOp(gameName, games, op, answers);
                Console.WriteLine(ok ? "Completed successfully. Press any key to continue…" : "Operation failed. Press any key to continue…");
                Console.ReadKey(true);
            }
        }
    }
}
