
using System.Collections.Generic;
using System.Diagnostics;

namespace EngineNet.Interface.Terminal;

internal partial class TUI {

    /* :: :: Constructor, Var :: START :: */
    private readonly Core.Engine.Engine _engine;
    internal TUI(Core.Engine.Engine engine) {
        _engine = engine;
    }

    /* :: :: Constructor, Var :: END :: */
    // //
    /* :: :: Methods :: START :: */

    /// <summary>
    /// Run the interactive terminal user interface menu
    /// - allows selecting a game and operations to run
    /// - runs initialization operations automatically once per game selection
    /// - appends completion time summaries after operations
    /// </summary>
    /// <returns></returns>
    internal async System.Threading.Tasks.Task<int> RunInteractiveMenuAsync(string? msg = null) {
        try {
            // get all modules that exist on disk
            Dictionary<string, Core.Utils.GameModuleInfo> modules = _engine.Modules(Core.Utils.ModuleFilter.Installed);
            // get internal modules
            Dictionary<string, Core.Utils.GameModuleInfo> internalModules = _engine.Modules(Core.Utils.ModuleFilter.Internal);

            // Create a combined dictionary for lookup and execution
            Dictionary<string, Core.Utils.GameModuleInfo> allAvailableModules = new(modules);
            foreach (var kv in internalModules) {
                // Internal modules overwrite installed if there's a name collision
                allAvailableModules[kv.Key] = kv.Value;
            }

            // Allow managing modules from the game selection menu
            string gameName;
            while (true) {
                SafeClear();
                if (msg is not null) {
                    System.Console.WriteLine(msg);
                }
                System.Console.WriteLine("Select a game:");

                List<string> gameMenu = new List<string>();
                List<string> gameKeyMap = new List<string>();
                //List<Core.Utils.GameModuleInfo> internalModulesList = new List<Core.Utils.GameModuleInfo>();

                // Build menu with states
                // foreach module, display '<Name> [<isRegistered>, <isInstalled (always true here)>, <isBuilt>]'
                foreach (KeyValuePair<string, Core.Utils.GameModuleInfo> kv in modules) {
                    Core.Utils.GameModuleInfo m = kv.Value;
                    // Skip internal modules in modules list; add them after game modules below separator
                    /*if (m.IsInternal) {
                        internalModulesList.Add(m);
                        continue;
                    }*/
                    string display = $"{m.Name}  [{m.DescribeState()}]";
                    gameMenu.Add(display);
                    gameKeyMap.Add(m.Name);
                }
                gameMenu.Add("---------------"); // separator before internal modules
                gameKeyMap.Add("---"); // placeholder for separator

                // Add internal modules after game modules
                foreach (Core.Utils.GameModuleInfo m in internalModules.Values) {
                    gameMenu.Add(m.Name);
                    gameKeyMap.Add(m.Name);
                }

                gameMenu.Add("Exit");
                gameKeyMap.Add("Exit"); // align with Exit index
                // Prompt for selection
                int gidx = SelectFromMenu(gameMenu, highlightSeparators: true);
                if (gidx < 0 || gameMenu[gidx] == "Exit") {
                    return 0;
                }

                // Get selected game name
                string gsel = gameMenu[gidx];

                // Map selection index to actual module key
                if (gidx >= 0 && gidx < gameKeyMap.Count) {
                    gameName = gameKeyMap[gidx];
                    Core.Diagnostics.Trace($"[TUI::RunInteractiveMenuAsync()] Selected game: {gameName}");
                } else {
                    // Fallback: treat selection as raw name
                    gameName = gsel;
                    Core.Diagnostics.Trace($"[TUI::RunInteractiveMenuAsync()] Warning: could not map selected index {gidx} to module key; using raw selection '{gsel}'");
                }
                break; // exit game selection loop
            }

            // 2) Load operations list and render menu

            Core.Utils.GameModuleInfo? info = null;
            // check the module exists in combined modules dictionary
            if (!allAvailableModules.TryGetValue(gameName, out var moduleInfo) || (info = moduleInfo) is null) {
                Core.Diagnostics.Log("[TUI::RunInteractiveMenuAsync()] Selected game not found.");
                // return to menu selection
                //return 1;
                return await RunInteractiveMenuAsync("Selected game not found. Please choose again.");
            }
            // load operations list from ops_file
            if (info.OpsFile is null) {
                Core.Diagnostics.Log("[TUI::RunInteractiveMenuAsync()] Selected game is missing ops_file.");
                //return 1;
                return await RunInteractiveMenuAsync("Selected game is missing operations file. Please choose again.");
            }
            Core.Services.OperationsService.PreparedOperations preparedOps = _engine.OperationsService.LoadAndPrepare(info.OpsFile);
            if (!preparedOps.IsLoaded) {
                string message = preparedOps.ErrorMessage ?? "Failed to load operations list.";
                Core.Diagnostics.Log($"[TUI::RunInteractiveMenuAsync()] {message}");
                System.Console.WriteLine($"{message} Press any key to exit...");
                SafeReadKey(true);
                Core.Diagnostics.Log("[TUI::RunInteractiveMenuAsync()] Exiting due to failed ops load.");
                return 1;
            }

            if (preparedOps.Warnings.Count > 0) {
                foreach (string warning in preparedOps.Warnings) {
                    Core.Diagnostics.Log($"[TUI::RunInteractiveMenuAsync()] Warning: {warning}");
                }
            }

            // Auto-run init operations once when a game is selected
            if (preparedOps.InitOperations.Count > 0) {
                SafeClear();
                System.Console.WriteLine(value: $"Running {preparedOps.InitOperations.Count} initialization operation(s) for {gameName}\n");
                Stopwatch initStopwatch = Stopwatch.StartNew();
                bool okAllInit = true;
                foreach (Core.Services.OperationsService.PreparedOperation op in preparedOps.InitOperations) {
                    Dictionary<string, object?> answers = new Dictionary<string, object?>();
                    // Initialization runs non-interactively; use defaults when provided
                    CollectAnswersForOperation(op.Operation, answers, defaultsOnly: true);
                    bool ok = new Utils().ExecuteOp(_engine, gameName, allAvailableModules, op.Operation, answers);
                    okAllInit &= ok;
                }
                initStopwatch.Stop();
                //didRunInit = true;
                Core.Diagnostics.Trace($"[TUI::RunInteractiveMenuAsync()] Completed init operations for {gameName} in {FormatElapsed(initStopwatch.Elapsed)}. Success: {okAllInit}");
                System.Console.WriteLine(okAllInit
                    ? $"Initialization completed successfully. Time: {FormatElapsed(initStopwatch.Elapsed)}. Press any key to continue..."
                    : $"One or more init operations failed. Time: {FormatElapsed(initStopwatch.Elapsed)}. Press any key to continue...");
                SafeReadKey(intercept: true);
            }

            // operations menu
            while (true) {
                SafeClear();
                System.Console.WriteLine(value: $"--- Operations for: {gameName}");
                List<string> menu = new List<string>();

                int opStartIndex = 0;

                // show a 'Play' option if isBuilt is true for the module, indicating the game is ready to run
                if (info.IsBuilt) {
                    menu.Add("Play");
                    menu.Add("---------------");
                    opStartIndex += 2;
                }

                // Show "Run All" only for non-internal modules and if there are operations with run-all flags
                bool showRunAll = !info.IsInternal && preparedOps.HasRunAll;
                if (showRunAll) {
                    menu.Add(item: "Run All");
                    menu.Add(item: "---------------");
                    opStartIndex += 2;
                }

                // list regular operations
                // for each operation, display its "Name" entry if exists, or just display 'unnamed'
                foreach (Core.Services.OperationsService.PreparedOperation op in preparedOps.RegularOperations) {
                    string name = op.DisplayName;
                    if (op.HasDuplicateId) {
                        name = $"[dup-id] {name}";
                    } else if (op.HasInvalidId) {
                        name = $"[invalid-id] {name}";
                    }

                    menu.Add(item: name);
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
                if (selection == "Play") {
                    SafeClear();
                    System.Console.WriteLine($"Launching game '{gameName}'...\n");

                    // Route in-process SDK events (e.g. from Lua scripts) to our terminal renderer
                    System.Action<Dictionary<string, object?>>? prevSink = Core.UI.EngineSdk.LocalEventSink;
                    bool prevMute = Core.UI.EngineSdk.MuteStdoutWhenLocalSink;

                    Core.UI.EngineSdk.LocalEventSink = Utils.OnEvent;
                    Core.UI.EngineSdk.MuteStdoutWhenLocalSink = true;

                    try {
                        bool launched = await _engine.GameLauncher.LaunchGameAsync(name: gameName);
                        System.Console.WriteLine(launched
                            ? "\nGame finished or launched successfully. Press any key to continue..."
                            : "\nFailed to launch game. Press any key to continue...");
                    } finally {
                        Core.UI.EngineSdk.LocalEventSink = prevSink;
                        Core.UI.EngineSdk.MuteStdoutWhenLocalSink = prevMute;
                    }

                    SafeReadKey(true);
                    continue;
                }
                if (selection == "Run All") {
                    try {
                        // 1. Initialize Advanced UI
                        TuiRenderer.Initialize();
                        TuiRenderer.Log($"Running operations for {gameName}...", ConsoleColor.Cyan);

                        Stopwatch runAllStopwatch = Stopwatch.StartNew();

                        // 2. Pass our custom StdinProvider that works with the Renderer
                        Core.ProcessRunner.StdinProvider rendererInput = () => TuiRenderer.ReadLineCustom("Input >", false);

                        Core.Engine.RunAllResult result = await _engine.RunAllAsync(
                            gameName,
                            onOutput: Utils.OnOutput, // Make sure OnOutput calls OnEvent -> TuiRenderer
                            onEvent: Utils.OnEvent,
                            stdinProvider: rendererInput
                        );
                        runAllStopwatch.Stop();

                        TuiRenderer.Log(result.Success ? "Completed successfully." : "One or more operations failed.",
                                        result.Success ? ConsoleColor.Green : ConsoleColor.Red);

                        TuiRenderer.Log($"({result.SucceededOperations}/{result.TotalOperations} operations succeeded). Time: {FormatElapsed(runAllStopwatch.Elapsed)}.", ConsoleColor.White);
                        TuiRenderer.Log("Press any key to continue...", ConsoleColor.White);
                        Console.ReadKey(true);

                        continue;
                    } catch (System.Exception ex) {
                        TuiRenderer.Log($"Error: {ex.Message}", ConsoleColor.Red);
                        Core.Diagnostics.Bug($"[TUI::RunAll()] Error during Run All: {ex.Message}");
                        Console.ReadKey(true);
                        continue;
                    } finally {
                        // 3. Return to standard menu mode
                        TuiRenderer.Shutdown();
                    }
                }

                // Otherwise, run a single operation (by index within regular ops)
                int opIndex = idx - opStartIndex;
                if (opIndex >= 0 && opIndex < preparedOps.RegularOperations.Count) {
                    Dictionary<string, object?> op = preparedOps.RegularOperations[opIndex].Operation;
                    Dictionary<string, object?> answers = new Dictionary<string, object?>();

                    // Initialize Renderer for interactive prompts and execution
                    TuiRenderer.Initialize();
                    try {
                        // For manual single-op run, prompt interactively
                        if (CollectAnswersForOperation(op, answers, defaultsOnly: false)) {
                            TuiRenderer.Log($"Running: {selection}\n", ConsoleColor.Cyan);
                            Stopwatch opStopwatch = Stopwatch.StartNew();
                            bool ok = new Utils().ExecuteOp(_engine, gameName, allAvailableModules, op, answers);
                            opStopwatch.Stop();

                            TuiRenderer.Log(ok
                                ? $"Completed successfully. Time: {FormatElapsed(opStopwatch.Elapsed)}."
                                : $"Operation failed. Time: {FormatElapsed(opStopwatch.Elapsed)}.",
                                ok ? ConsoleColor.Green : ConsoleColor.Red);

                            TuiRenderer.Log("Press any key to continue...", ConsoleColor.White);
                            Console.ReadKey(true);
                        }
                    } catch (System.Exception ex) {
                        TuiRenderer.Log($"Error: {ex.Message}", ConsoleColor.Red);
                        Console.ReadKey(true);
                    } finally {
                        TuiRenderer.Shutdown();
                    }
                }
            }
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"[TUI::RunInteractiveMenuAsync()] Error: {ex}");
            System.Console.WriteLine($"Error: {ex.Message}\nPress any key to exit...");
            SafeReadKey(true);
            return -1;
        }
    }

}
