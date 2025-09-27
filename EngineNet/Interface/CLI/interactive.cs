using System;
using System.Collections.Generic;
using System.IO;

namespace EngineNet.Interface.CLI;

public partial class CliApp {
    private Int32 RunInteractiveMenu() {
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
                Boolean ok = ExecuteOp(gameName, games, op, answers);
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
                    Boolean ok = ExecuteOp(gameName, games, op, answers);
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
                Boolean ok = ExecuteOp(gameName, games, op, answers);
                Console.WriteLine(ok ? "Completed successfully. Press any key to continue…" : "Operation failed. Press any key to continue…");
                Console.ReadKey(true);
            }
        }
    }
}
