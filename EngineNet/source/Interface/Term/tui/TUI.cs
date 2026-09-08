

namespace EngineNet.Term;
using EngineNet.Shared.IO;
using Interface;
using Core.Data;

public partial class TUI {

    /* :: :: Constructor, Var :: START :: */
    private readonly MiniEngineFace Engine;

    public TUI(MiniEngineFace engine) {
        Engine = engine;
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
    public async System.Threading.Tasks.Task<int> RunAsync(System.Threading.CancellationToken cancellationToken = default(CancellationToken), string? msg = null) {
        try {
            // get all modules that exist on disk
            Core.Data.GameModules modules = Engine.GameRegistry_GetModules(Core.Data.ModuleFilter.Installed);
            // get public modules
            Core.Data.GameModules internalModules = Engine.GameRegistry_GetModules(Core.Data.ModuleFilter.Internal);

            // Create a combined dictionary for lookup and execution
            Core.Data.GameModules allAvailableModules = new(modules);
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
                //List<Core.Data.GameModuleInfo> internalModulesList = new List<Core.Data.GameModuleInfo>();

                // Build menu with states
                // foreach module, display '<Name> [<isRegistered>, <isInstalled (always true here)>, <isBuilt>]'
                foreach (var item in modules.Values.Select(m => (Display: $"{m.Name}  [{m.DescribeState()}]", m.Name))) {
                    gameMenu.Add(item.Display);
                    gameKeyMap.Add(item.Name);
                }
                gameMenu.Add("---------------"); // separator before public modules
                gameKeyMap.Add("---"); // placeholder for separator

                // Add public modules after game modules
                foreach (Core.Data.GameModuleInfo m in internalModules.Values) {
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
                    Shared.IO.Diagnostics.Trace($"[TUI::RunAsync()] Selected game: {gameName}");
                } else {
                    // Fallback: treat selection as raw name
                    gameName = gsel;
                    Shared.IO.Diagnostics.Trace($"[TUI::RunAsync()] Warning: could not map selected index {gidx} to module key; using raw selection '{gsel}'");
                }
                break; // exit game selection loop
            }

            // 2) Load operations list and render menu

            Core.Data.ModuleOperationSession operationSession = Engine.OperationsService_LoadModuleSession(gameName, allAvailableModules);
            Core.Data.GameModuleInfo? info = operationSession.Module;
            Core.Data.PreparedOperations preparedOps = operationSession.PreparedOperations;
            if (info is null) {
                Shared.IO.Diagnostics.Log("[TUI::RunAsync()] Selected game not found.");
                return await RunAsync(msg: preparedOps.ErrorMessage ?? "Selected game not found. Please choose again.");
            }
            if (!preparedOps.IsLoaded) {
                string message = preparedOps.ErrorMessage ?? "Failed to load operations list.";
                Shared.IO.Diagnostics.Log($"[TUI::RunAsync()] {message}");
                System.Console.WriteLine($"{message} Press any key to exit...");
                SafeReadKey(true);
                Shared.IO.Diagnostics.Log("[TUI::RunAsync()] Exiting due to failed ops load.");
                return 1;
            }

            if (preparedOps.Warnings.Count > 0) {
                foreach (string warning in preparedOps.Warnings) {
                    Shared.IO.Diagnostics.Log($"[TUI::RunAsync()] Warning: {warning}");
                }
            }

            // Auto-run init operations once when a game is selected
            if (preparedOps.InitOperations.Count > 0) {
                SafeClear();
                System.Console.WriteLine(value: $"Running {preparedOps.InitOperations.Count} initialization operation(s) for {gameName}\n");
                System.Diagnostics.Stopwatch initStopwatch = System.Diagnostics.Stopwatch.StartNew();
                bool okAllInit = true;
                foreach (Core.Data.PreparedOperation op in preparedOps.InitOperations) {
                    Core.Data.PromptAnswers promptAnswers = new Core.Data.PromptAnswers();
                    // Initialization runs non-interactively; use defaults when provided
                    await CollectAnswersForOperation(op.Operation, promptAnswers, defaultsOnly: true);
                    TuiRenderer.ResetContext(clearLogs: false);
                    TuiRenderer.SetCancellationMode(TuiRenderer.CancellationMode.Disabled);
                    bool ok = await new Utils().ExecuteOpAsync(Engine, gameName, allAvailableModules, op.Operation, promptAnswers, cancellationToken: cancellationToken);
                    okAllInit &= ok;
                    TuiRenderer.SetCancellationMode(TuiRenderer.CancellationMode.PromptsOnly);
                }
                initStopwatch.Stop();

                Shared.IO.Diagnostics.Trace($"[TUI::RunAsync()] Completed init operations for {gameName} in {FormatElapsed(initStopwatch.Elapsed)}. Success: {okAllInit}");
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

                // Show "Run All" only for non-public modules and if there are operations with run-all flags
                bool showRunAll = operationSession.CanRunAll;
                if (showRunAll) {
                    menu.Add(item: "Run All");
                    menu.Add(item: "---------------");
                    opStartIndex += 2;
                }

                // ---------------

                // if operation_execution.log (in gameroot) contains a run of this operation's ID, append '[completed] or '[failed]' to the name (using most recent successful run)
                // determine based on id number, this can be any numberical value, if no id is present, log as 'No ID' and skip the check

                // first we read the entire log
                // then filter out old runs of the same operation ID, leaving only the most recent run
                // then create a dictionary of operation ID to completion status (success/fail) based on the most recent run to be iterated in the next foreach loop

                // list regular operations
                // for each operation, display its "Name" entry if exists, or just display 'unnamed'
                foreach (Core.Data.SessionOperation sessionOperation in operationSession.RegularOperations) {
                    Core.Data.PreparedOperation op = sessionOperation.Operation;
                    string name = op.DisplayName;
                    if (op.HasDuplicateId) {
                        name = $"[dup-id] {name}";
                    }
                    else if (op.HasInvalidId) {
                        name = $"[invalid-id] {name}";
                    }

                    if (sessionOperation.Status == Core.Data.OperationExecutionStatus.Succeeded) {
                        name += " [completed]";
                    } else if (sessionOperation.Status == Core.Data.OperationExecutionStatus.Failed) {
                        name += " [failed]";
                    }

                    menu.Add(item: name);
                }

                // ---------------

                menu.Add(item: "---------------");
                menu.Add(item: "Change Game");
                menu.Add(item: "Exit");

                System.Console.WriteLine(value: "? Select an operation: (Use arrow keys)");
                int idx = SelectFromMenu(menu, highlightSeparators: true);
                if (idx < 0) {
                    // if idx < 0 (eg. Pressed Escape), return to the game selection menu
                    return await RunAsync();
                }

                string selection = menu[idx];
                switch (selection) {
                    case "Change Game":
                        // Restart the full menu loop by re-picking game
                        return await RunAsync();
                    case "Exit":
                        return 0;
                    case "Play": {
                        SafeClear();
                        System.Console.WriteLine($"Launching game '{gameName}'...\n");

                        // Route in-process SDK events (e.g. from Lua scripts) to our terminal renderer
                        System.Action<Dictionary<string, object?>>? prevSink = Shared.IO.UI.EngineSdk.LocalEventSink;
                        bool prevMute = Shared.IO.UI.EngineSdk.MuteStdoutWhenLocalSink;

                        Shared.IO.UI.EngineSdk.LocalEventSink = Utils.OnEvent;
                        Shared.IO.UI.EngineSdk.MuteStdoutWhenLocalSink = true;

                        try {
                            bool launched = await Engine.GameLauncher_LaunchGameAsync(name: gameName, cancellationToken: cancellationToken);
                            System.Console.WriteLine(launched
                                ? "\nGame finished or launched successfully. Press any key to continue..."
                                : "\nFailed to launch game. Press any key to continue...");
                        } finally {
                            Shared.IO.UI.EngineSdk.LocalEventSink = prevSink;
                            Shared.IO.UI.EngineSdk.MuteStdoutWhenLocalSink = prevMute;
                        }

                        SafeReadKey(true);
                        continue;
                    }
                    case "Run All": {
                        using var runAllCts = new System.Threading.CancellationTokenSource();
                        try {
                            // 1. Initialize Advanced UI
                            TuiRenderer.Initialize(runAllCts);
                            TuiRenderer.SetCancellationMode(TuiRenderer.CancellationMode.PromptsOnly);
                            TuiRenderer.ResetContext(clearLogs: false);
                            TuiRenderer.Log($"Running operations for {gameName}...", ConsoleColor.Cyan);

                            System.Diagnostics.Stopwatch runAllStopwatch = System.Diagnostics.Stopwatch.StartNew();

                            // 2. Pass our custom StdinProvider that works with the Renderer
                            Core.ProcessRunner.StdinProvider rendererInput = () => TuiRenderer.ReadLineCustom("Input >", false);

                            Core.Operations.RunAllResult result = await Engine.RunAllAsync(
                                gameName,
                                onOutput: Utils.OnOutput, // Make sure OnOutput calls OnEvent -> TuiRenderer
                                onEvent: Utils.OnEvent,
                                stdinProvider: rendererInput,
                                cancellationToken: runAllCts.Token
                            );
                            runAllStopwatch.Stop();

                            TuiRenderer.Log(result.Success ? "Completed successfully." : "One or more operations failed.",
                                result.Success ? ConsoleColor.Green : ConsoleColor.Red);

                            TuiRenderer.Log($"({result.SucceededOperations}/{result.TotalOperations} operations succeeded). Time: {FormatElapsed(runAllStopwatch.Elapsed)}.", ConsoleColor.White);
                            TuiRenderer.Log("Press any key to continue...", ConsoleColor.White);
                            TuiRenderer.WaitForKey();

                            continue;
                        } catch (System.Exception ex) {
                            TuiRenderer.Log($"Error: {ex.Message}", ConsoleColor.Red);
                            Shared.IO.Diagnostics.Bug($"[TUI::RunAll()] Error during Run All: {ex.Message}");
                            TuiRenderer.WaitForKey();
                            continue;
                        } finally {
                            // 3. Return to standard menu mode
                            TuiRenderer.SetCancellationMode(TuiRenderer.CancellationMode.PromptsOnly);
                            TuiRenderer.Shutdown();
                        }

                    }
                }

                // Otherwise, run a single operation (by index within regular ops)
                int opIndex = idx - opStartIndex;
                if (opIndex < 0 || opIndex >= preparedOps.RegularOperations.Count){
                    continue; // invalid selection (eg. separator or out of bounds), just refresh menu
                }
                // Run the selected operation
                {
                    Dictionary<string, object?> op = preparedOps.RegularOperations[opIndex].Operation;
                    var answers = new Core.Data.PromptAnswers();

                    // Create a linked token source so Escape only cancels this specific operation run
                    using var opCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                    // Initialize Renderer for interactive prompts and execution WITH the token source
                    TuiRenderer.Initialize(opCts);
                    TuiRenderer.SetCancellationMode(TuiRenderer.CancellationMode.PromptsOnly);
                    try {
                        // Pass opCts.Token instead of the parent token
                        Task<bool> promptTask = CollectAnswersForOperation(op, answers, defaultsOnly: false, cancellationToken: opCts.Token);
                        if (await promptTask) {
                            TuiRenderer.Log($"Running: {selection}\n", ConsoleColor.Cyan);
                            System.Diagnostics.Stopwatch opStopwatch = System.Diagnostics.Stopwatch.StartNew();
                            TuiRenderer.ResetContext(clearLogs: false);
                            TuiRenderer.SetCancellationMode(TuiRenderer.CancellationMode.Disabled);
                            bool ok = await new Utils().ExecuteOpAsync(Engine, gameName, allAvailableModules, op, answers, cancellationToken: opCts.Token);
                            opStopwatch.Stop();

                            TuiRenderer.Log(ok
                                    ? $"Completed successfully. Time: {FormatElapsed(opStopwatch.Elapsed)}."
                                    : $"Operation failed. Time: {FormatElapsed(opStopwatch.Elapsed)}.",
                                ok ? ConsoleColor.Green : ConsoleColor.Red);

                            TuiRenderer.Log("Press any key to continue...", ConsoleColor.White);
                            TuiRenderer.WaitForKey();
                        }
                    } catch (System.Exception ex) {
                        TuiRenderer.Log($"Error: {ex.Message}", ConsoleColor.Red);
                        TuiRenderer.WaitForKey();
                    } finally {
                        TuiRenderer.SetCancellationMode(TuiRenderer.CancellationMode.PromptsOnly);
                        TuiRenderer.Shutdown();
                    }
                }
            }
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[TUI::RunAsync()] Error: {ex}");
            System.Console.WriteLine($"Error: {ex.Message}\nPress any key to exit...");
            SafeReadKey(true);
            return -1;
        }
    }


    /// <summary>
    /// Formats elapsed time for completion summaries.
    /// </summary>
    private static string FormatElapsed(System.TimeSpan elapsed) {
        if (elapsed.TotalSeconds < 60) {
            return $"{elapsed.TotalSeconds:0.0}s";
        } else if (elapsed.TotalMinutes < 60) {
            return $"{elapsed.Minutes}m {elapsed.Seconds:D2}s";
        } else {
            return $"{(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m {elapsed.Seconds:D2}s";
        }
    }

    /// <summary>
    /// Safely clears the console if it is not redirected and supported.
    /// </summary>
    private static void SafeClear() {
        try {
            if (!System.Console.IsOutputRedirected) {
                System.Console.Clear();
            }
        } catch (System.Exception e) {
            Shared.IO.Diagnostics.Bug($"[TUI.private.cs::SafeClear()] Error clearing console: {e.Message}");
        }
    }

    /// <summary>
    /// Safely reads a key from the console if it is not redirected.
    /// Returns an empty ConsoleKeyInfo if redirection is detected or on error.
    /// </summary>
    /// <param name="intercept">Whether to intercept the key</param>
    /// <returns></returns>
    private static System.ConsoleKeyInfo SafeReadKey(bool intercept = false) {
        try {
            if (!System.Console.IsInputRedirected) {
                return System.Console.ReadKey(intercept);
            }
        } catch (System.Exception e) {
            Shared.IO.Diagnostics.Bug($"[TUI.private.cs::SafeReadKey()] Error reading key: {e.Message}");
            // ignore
        }
        return new System.ConsoleKeyInfo('\0', 0, false, false, false);
    }

    /// <summary>
    /// Safely sets the cursor visibility if not redirected.
    /// </summary>
    /// <param name="visible"></param>
    private static void SafeSetCursorVisible(bool visible) {
        try {
            if (!System.Console.IsOutputRedirected) {
                System.Console.CursorVisible = visible;
            }
        } catch (System.Exception e) {
            Shared.IO.Diagnostics.Bug($"[TUI.private.cs::SafeSetCursorVisible()] Error setting cursor visibility: {e.Message}");
            // ignore
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
            Shared.IO.Diagnostics.Bug("[TUI.private.cs::CanUseInteractiveMenu()] Error checking console capabilities.");
            return false;
        }
    }

    /// <summary>
    /// Presents an interactive menu to the user to select from a list of items.
    /// </summary>
    /// <param name="items"></param>
    /// <param name="highlightSeparators"></param>
    /// <param name="disabledIndices">Indices that are greyed out and cannot be selected.</param>
    /// <returns></returns>
    private static int SelectFromMenu(IList<string> items, bool highlightSeparators = false, HashSet<int>? disabledIndices = null) {
        try {
            // no items
            if (items.Count == 0) {
                return -1;
            }

            // if interactive menu is not possible, fall back to numbered input selection
            if (!CanUseInteractiveMenu(items.Count)) {
                return SelectFromNumberedMenu(items, highlightSeparators, disabledIndices);
            }

            int index = 0;
            // Ensure initial index is valid and not a separator or disabled
            while (index < items.Count && (items[index] == "---------------" || (disabledIndices?.Contains(index) ?? false))) {
                index++;
            }
            if (index >= items.Count) {
                // Try to find any selectable index if the first one was invalid
                index = -1;
                for (int i = 0; i < items.Count; i++) {
                    if (items[i] == "---------------" || (disabledIndices?.Contains(i) ?? false)) continue;
                    index = i;
                    break;
                }
            }

            int renderTop = System.Console.CursorTop;

            while (true) {
                SafeSetCursorVisible(false);

                try {
                    System.Console.SetCursorPosition(0, renderTop);
                } catch (System.ArgumentOutOfRangeException) {
                    SafeSetCursorVisible(true);
                    return SelectFromNumberedMenu(items, highlightSeparators, disabledIndices);
                }

                for (int i = 0; i < items.Count; i++) {
                    string line = items[i];
                    bool isSep = line == "---------------";
                    bool isDisabled = disabledIndices?.Contains(i) ?? false;

                    if (i == index) {
                        System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                        System.Console.WriteLine($"> {line}");
                        System.Console.ResetColor();
                    } else if (isDisabled) {
                        System.Console.ForegroundColor = System.ConsoleColor.DarkGray;
                        System.Console.WriteLine($"  {line} (Already downloaded)");
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

                if (index == -1) {
                    System.Console.WriteLine("\nNo selectable options. Press any key to return...");
                    SafeReadKey(true);
                    SafeSetCursorVisible(true);
                    return -1;
                }

                System.ConsoleKeyInfo keyInfo = SafeReadKey(true);
                switch (keyInfo.Key) {
                    case System.ConsoleKey.DownArrow:
                        int next = index;
                        do {
                            next = (next + 1) % items.Count;
                        } while (next != index && (items[next] == "---------------" || (disabledIndices?.Contains(next) ?? false)));
                        index = next;
                        break;
                    case System.ConsoleKey.UpArrow:
                        int prev = index;
                        do {
                            prev = (prev - 1 + items.Count) % items.Count;
                        } while (prev != index && (items[prev] == "---------------" || (disabledIndices?.Contains(prev) ?? false)));
                        index = prev;
                        break;
                    case System.ConsoleKey.Escape:
                        SafeSetCursorVisible(true);
                        return -1;
                    case System.ConsoleKey.Enter:
                        SafeSetCursorVisible(true);
                        return index;
                }
            }
        } catch (System.Exception) {
            Shared.IO.Diagnostics.Bug("[TUI.private.cs::SelectFromMenu()] Error in SelectFromMenu");
            return -1;
        }
    }

    /// <summary>
    /// menu selection using numbered input
    /// </summary>
    /// <param name="items"></param>
    /// <param name="highlightSeparators"></param>
    /// <param name="disabledIndices">Indices that are greyed out and cannot be selected.</param>
    /// <returns></returns>
    private static int SelectFromNumberedMenu(IList<string> items, bool highlightSeparators, HashSet<int>? disabledIndices = null) {
        try {
            List<int> selectable = new List<int>();

            System.Console.WriteLine();
            System.Console.WriteLine("Terminal is too small for the interactive menu. Enter the option number instead:");

            int displayIndex = 1;
            for (int i = 0; i < items.Count; i++) {
                string line = items[i];
                bool isSep = line == "---------------";
                bool isDisabled = disabledIndices?.Contains(i) ?? false;

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

                if (isDisabled) {
                    System.Console.ForegroundColor = System.ConsoleColor.DarkGray;
                    System.Console.WriteLine($"{displayIndex}. {line} (Already downloaded)");
                    System.Console.ResetColor();
                } else {
                    System.Console.WriteLine($"{displayIndex}. {line}");
                    selectable.Add(i);
                }
                displayIndex++;
            }

            if (selectable.Count == 0) {
                System.Console.WriteLine("\nNo selectable options. Press any key to return...");
                SafeReadKey(true);
                return -1;
            }

            while (true) {
                System.Console.Write("Selection (blank or Escape to cancel): ");
                string? input = ReadLineWithCancel(out bool cancelled);
                if (cancelled || string.IsNullOrWhiteSpace(input)) {
                    return -1;
                }

                if (int.TryParse(input.Trim(), out int choice) && choice >= 1 && choice <= (displayIndex - 1)) {
                    int currentDisplay = 1;
                    for (int i = 0; i < items.Count; i++) {
                        if (items[i] == "---------------") continue;
                        if (currentDisplay == choice) {
                            if (!(disabledIndices?.Contains(i) ?? false)) return i;
                            System.Console.WriteLine("That option is already downloaded and cannot be selected.");
                            break;
                        }
                        currentDisplay++;
                    }
                }

                System.Console.WriteLine("Invalid selection. Please enter a valid number.");
            }
        } catch (System.Exception) {
            Shared.IO.Diagnostics.Bug("[TUI.private.cs::SelectFromMenuFallback()] Error in SelectFromMenuFallback");
            return -1;
        }
    }


    /// <summary>
    /// Collects answers for an operation by processing its prompts and interacting with the user via the console.
    /// this is where [[operation.prompts]] sections are parsed and executed, allowing for dynamic user input before execution of its operation.
    /// </summary>
    /// <param name="op"></param>
    /// <param name="answers"></param>
    /// <param name="defaultsOnly"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task<bool> CollectAnswersForOperation(Dictionary<string, object?> op, Core.Data.PromptAnswers answers, bool defaultsOnly, CancellationToken cancellationToken = default(CancellationToken)) {
        try {
            // This handler will be called for each prompt defined in the operation's "prompts" section. It will present the prompt to the user and collect their input.
            // this function is used in OperationsService.CollectAnswersAsync, which iterates through all prompts and calls this handler for each, accumulating answers in the provided PromptAnswers object.
            static Task<PromptResponse> promptHandler(PromptRequest request, CancellationToken cancellationToken) {
                switch (request.Type) {
                    case "select": {
                        List<string> choicesList = request.Choices.Select(choice => choice.Label).ToList();
                        HashSet<int> disabled = request.Choices
                            .Select((choice, index) => new { choice.IsDisabled, index })
                            .Where(entry => entry.IsDisabled)
                            .Select(entry => entry.index)
                            .ToHashSet();

                        if (choicesList.Count == 0) {
                            TuiRenderer.Log($"No choices available for {request.Title}.", ConsoleColor.Yellow);
                            if (request.DefaultValue is not null) {
                                return Task.FromResult(Core.Data.PromptResponse.UseDefaultValue());
                            }

                            return Task.FromResult(Core.Data.PromptResponse.FromValue(null));
                        }

                        // For Select types, we'll use TuiRenderer.Log to list options and ReadLineCustom for input
                        // as standard menus might break the layout.
                        // Alternatively, we can still use SelectFromMenu if we are careful.
                        // Let's stick to the prompt style for now to be safe.
                        TuiRenderer.Log($"{request.Title}:", ConsoleColor.Cyan);
                        for (int i = 0; i < choicesList.Count; i++) {
                            TuiRenderer.Log($"{i + 1}. {choicesList[i]}{(disabled.Contains(i) ? " (Disabled)" : "")}");
                        }

                        string? input = TuiRenderer.ReadLineCustom("Selection # >", false);

                        if (input == null) return Task.FromResult(Core.Data.PromptResponse.Cancelled());

                        if (string.IsNullOrWhiteSpace(input) || !int.TryParse(input, out int choiceIdx) ||
                            choiceIdx < 1 || choiceIdx > choicesList.Count) {
                            return Task.FromResult(Core.Data.PromptResponse.Cancelled());
                        }

                        int actualIdx = choiceIdx - 1;
                        if (!disabled.Contains(actualIdx))
                            return Task.FromResult(Core.Data.PromptResponse.FromValue(choicesList[actualIdx]));
                        TuiRenderer.Log("Selected item is disabled.", ConsoleColor.Red);
                        return Task.FromResult(Core.Data.PromptResponse.Cancelled());
                    }
                    case "confirm": {
                        bool defVal = request.DefaultValue is true;
                        string defHint = defVal ? "Y" : "N";
                        string? c = TuiRenderer.ReadLineCustom($"{request.Title} [y/N] (default {defHint}) >", false);

                        // Add null check here
                        if (c == null) return Task.FromResult(Core.Data.PromptResponse.Cancelled());

                        if (string.IsNullOrWhiteSpace(c)) {
                            return Task.FromResult(Core.Data.PromptResponse.UseDefaultValue());
                        }

                        bool val = c.Trim().StartsWith("y", System.StringComparison.OrdinalIgnoreCase);
                        return Task.FromResult(Core.Data.PromptResponse.FromValue(val));
                    }
                    case "checkbox": {
                        if (request.Choices.Count > 0) {
                            TuiRenderer.Log($"{request.Title} - choose one or more (comma-separated).",
                                ConsoleColor.Cyan);
                            for (int i = 0; i < request.Choices.Count; i++) {
                                TuiRenderer.Log($"{i + 1}. {request.Choices[i].Label}");
                            }
                        }
                        else {
                            TuiRenderer.Log($"{request.Title} (comma-separated values): ", ConsoleColor.Cyan);
                        }

                        string? line = TuiRenderer.ReadLineCustom("Values >", false);

                        // Add null check here
                        if (line == null) return Task.FromResult(Core.Data.PromptResponse.Cancelled());

                        if (string.IsNullOrWhiteSpace(line)) {
                            if (request.DefaultValue is IList<object?>) {
                                return Task.FromResult(Core.Data.PromptResponse.UseDefaultValue());
                            }

                            return Task.FromResult(Core.Data.PromptResponse.FromValue(new List<object?>()));
                        }

                        List<object?> selected = line.Split(',',
                                System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
                            .Cast<object?>().ToList();
                        return Task.FromResult(Core.Data.PromptResponse.FromValue(selected));
                    }
                    case "text":
                        string? v = TuiRenderer.ReadLineCustom($"{request.Title} >", false);

                        // Add null check here
                        if (v == null) return Task.FromResult(Core.Data.PromptResponse.Cancelled());

                        if (!string.IsNullOrWhiteSpace(v)) {
                            return Task.FromResult(Core.Data.PromptResponse.FromValue(v));
                        }

                        if (request.DefaultValue is not null) {
                            return Task.FromResult(Core.Data.PromptResponse.UseDefaultValue());
                        }

                        return Task.FromResult(Core.Data.PromptResponse.FromValue(string.Empty));
                    default: {
                        TuiRenderer.Log($"Unsupported prompt type: {request.Type}", ConsoleColor.Red);
                        // for now assume text
                        string? vv = TuiRenderer.ReadLineCustom($"{request.Title} >", false);

                        // Add null check here
                        if (vv == null) return Task.FromResult(Core.Data.PromptResponse.Cancelled());

                        if (!string.IsNullOrWhiteSpace(vv)) {
                            return Task.FromResult(Core.Data.PromptResponse.FromValue(vv));
                        }

                        if (request.DefaultValue is not null) {
                            return Task.FromResult(Core.Data.PromptResponse.UseDefaultValue());
                        }

                        return Task.FromResult(Core.Data.PromptResponse.FromValue(string.Empty));
                    }
                }
            }

            return await Engine.OperationsService_CollectAnswersAsync(op, answers, promptHandler, defaultsOnly, cancellationToken);
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[TUI.private.cs::PromptUser()] Error during interactive prompts: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reads a line from console, returning null if Escape is pressed.
    /// </summary>
    private static string? ReadLineWithCancel(out bool cancelled) {
        cancelled = false;
        System.Text.StringBuilder sb = new StringBuilder();
        while (true) {
            var key = SafeReadKey(intercept: true);
            switch (key.Key) {
                case System.ConsoleKey.Enter:
                    System.Console.WriteLine();
                    return sb.ToString();
                case System.ConsoleKey.Escape:
                    cancelled = true;
                    System.Console.WriteLine();
                    return null;
                case System.ConsoleKey.Backspace when sb.Length <= 0:
                    continue;
                case System.ConsoleKey.Backspace:
                    sb.Remove(sb.Length - 1, 1);
                    System.Console.Write("\b \b");
                    break;
                default: {
                    if (!char.IsControl(key.KeyChar)) {
                        sb.Append(key.KeyChar);
                        System.Console.Write(key.KeyChar);
                    }

                    break;
                }
            }
        }
    }


}
