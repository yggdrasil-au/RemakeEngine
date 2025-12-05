
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace EngineNet.Interface.Terminal;

internal partial class TUI {

    /* :: :: Constructor, Var :: START :: */
    private readonly Core.Engine _engine;
    internal TUI(Core.Engine engine) {
        _engine = engine;
    }

    internal async System.Threading.Tasks.Task<int> RunInteractiveMenuAsync() {
        try {
            // 1) Pick a game, or offer to download a module if none exist
            Dictionary<string, Core.Utils.GameModuleInfo> modules = _engine.Modules(Core.Utils.ModuleFilter.Installed);
            while (modules.Count == 0) {
                System.Console.Clear();
                System.Console.WriteLine("No games found in EngineApps/Games.");
                List<string> actions = new List<string> { "Download module...", "Exit" };
                System.Console.WriteLine("? Choose an action:");
                int aidx = SelectFromMenu(actions);
                if (aidx < 0 || actions[aidx] == "Exit") {
                    return 0;
                }

                if (actions[aidx].StartsWith("Download")) {
                    ShowDownloadMenu();
                    modules = _engine.Modules(Core.Utils.ModuleFilter.All);
                }
            }
            // Allow managing modules from the game selection menu
            string gameName;
            while (true) {
                System.Console.Clear();
                System.Console.WriteLine("Select a game:");
                List<string> gameMenu = new List<string>();
                List<string> gameKeyMap = new List<string>();
                foreach (KeyValuePair<string, Core.Utils.GameModuleInfo> kv in modules) {
                    Core.Utils.GameModuleInfo m = kv.Value;
                    string display = $"{m.Name}  [{m.DescribeState()}]";
                    gameMenu.Add(display);
                    gameKeyMap.Add(m.Name);
                }
                gameMenu.Add("---------------");
                gameMenu.Add("Download module...");
                gameMenu.Add("Exit");
                int gidx = SelectFromMenu(gameMenu, highlightSeparators: true);
                if (gidx < 0 || gameMenu[gidx] == "Exit") {
                    return 0;
                }

                string gsel = gameMenu[gidx];
                if (gsel.StartsWith("Download module")) {
                        Core.Diagnostics.Log("[TUI::RunInteractiveMenuAsync()] User selected Download module from game menu.");
                    ShowDownloadMenu();
                    // only refresh installed modules after download
                    modules = _engine.Modules(Core.Utils.ModuleFilter.Installed);
                    continue; // show game list again
                }
                // Map selection index to actual module key
                if (gidx >= 0 && gidx < gameKeyMap.Count) {
                    gameName = gameKeyMap[gidx];
                } else {
                    // Fallback: treat selection as raw name (legacy)
                    gameName = gsel;
                }
                break;
            }

            // 2) Load operations list and render menu
            if (!modules.TryGetValue(gameName, out Core.Utils.GameModuleInfo? moduleInfo) || moduleInfo is not Core.Utils.GameModuleInfo info) {
                    Core.Diagnostics.Log("[TUI::RunInteractiveMenuAsync()] Selected game not found.");
                return 1;
            }
            if (info.OpsFile is null) {
                    Core.Diagnostics.Log("[TUI::RunInteractiveMenuAsync()] Selected game is missing ops_file.");
                return 1;
            }
            List<Dictionary<string, object?>>? allOps = Core.Engine.LoadOperationsList(info.OpsFile);
            if (allOps is null) {
                Core.Diagnostics.Log("[TUI::RunInteractiveMenuAsync()] Failed to load operations list.");
                System.Console.WriteLine($"Failed to load operations file for '{gameName}'.\nPress any key to exit...");
                System.Console.ReadKey(true);
                Core.Diagnostics.Log("[TUI::RunInteractiveMenuAsync()] Exiting due to failed ops load.");
                return 1;
            }

            List<Dictionary<string, object?>> initOps = allOps.FindAll(op => op.TryGetValue(key: "init", out object? i) && i is bool b && b);
            List<Dictionary<string, object?>> regularOps = allOps.FindAll(op => !op.ContainsKey(key: "init") || !(op[key: "init"] is bool bb && bb));

            // Auto-run init operations once when a game is selected
            if (initOps.Count > 0) {
                System.Console.Clear();
                System.Console.WriteLine(value: $"Running {initOps.Count} initialization operation(s) for {gameName}\n");
                bool okAllInit = true;
                foreach (Dictionary<string, object?> op in initOps) {
                    Dictionary<string, object?> answers = new Dictionary<string, object?>();
                    // Initialization runs non-interactively; use defaults when provided
                    CollectAnswersForOperation(op, answers, defaultsOnly: true);
                    bool ok = new Utils().ExecuteOp(_engine, gameName, modules, op, answers);
                    okAllInit &= ok;
                }
                //didRunInit = true;
                System.Console.WriteLine(okAllInit
                    ? "Initialization completed successfully. Press any key to continue..."
                    : "One or more init operations failed. Press any key to continue...");
                System.Console.ReadKey(intercept: true);
            }

            while (true) {
                System.Console.Clear();
                System.Console.WriteLine(value: $"--- Operations for: {gameName}");
                List<string> menu = new List<string>();
                menu.Add(item: "Run All");
                menu.Add(item: "---------------");
                foreach (Dictionary<string, object?> op in regularOps) {
                    string name;

                    // First choice: explicit "Name" entry if it's a non-empty string
                    if (op.TryGetValue(key: "Name", out object? n) &&
                        n is string s &&
                        !string.IsNullOrWhiteSpace(s)) {
                        name = s;
                    } else {
                        // Fallback: derive from "script" filename, or "(unnamed)"
                        string? scriptPath = null;

                        if (op.TryGetValue(key: "script", out object? sc)) {
                            scriptPath = sc?.ToString();
                        }

                        name = System.IO.Path.GetFileName(
                            string.IsNullOrWhiteSpace(scriptPath) ? "(unnamed)" : scriptPath
                        );
                    }

                    menu.Add(name);
                }
                menu.Add(item: "---------------");
                menu.Add(item: "Change Game");
                menu.Add(item: "Exit");

                System.Console.WriteLine(value: "? Select an operation: (Use arrow keys)");
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

                        Core.RunAllResult result = await _engine.RunAllAsync(gameName, onOutput: Utils.OnOutput, onEvent: Utils.OnEvent, stdinProvider: null);

                        System.Console.WriteLine(result.Success
                            ? $"Completed successfully. ({result.SucceededOperations}/{result.TotalOperations} operations succeeded). Press any key to continue..."
                            : $"One or more operations failed. ({result.SucceededOperations}/{result.TotalOperations} operations succeeded). Press any key to continue...");
                        System.Console.ReadKey(true);
                        continue;
                    } catch (System.Exception ex) {
                        Core.Diagnostics.Bug($"[TUI::RunAll()] Error during Run All: {ex.Message}");
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
                    bool ok = new Utils().ExecuteOp(_engine, gameName, modules, op, answers);
                    System.Console.WriteLine(ok ? "Completed successfully. Press any key to continue..." : "Operation failed. Press any key to continue...");
                    System.Console.ReadKey(true);
                }
            }
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"[TUI::RunInteractiveMenuAsync()] Error: {ex}");
            System.Console.WriteLine($"Error: {ex.Message}\nPress any key to exit...");
            System.Console.ReadKey(true);
            return -1;
        }
    }

}
