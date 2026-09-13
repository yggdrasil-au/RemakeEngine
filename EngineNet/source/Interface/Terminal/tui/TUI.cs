

namespace EngineNet.Terminal;
using Interface;
using Core.Data;

public sealed class TUI {

    private readonly MiniEngineFace Engine;

    public TUI(MiniEngineFace engine) {
        Engine = engine;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default(CancellationToken), string? msg = null) {
        try {
            GameModules modules = Engine.GameRegistry_GetModules(filter: ModuleFilter.Installed);
            GameModules internalModules = Engine.GameRegistry_GetModules(filter: ModuleFilter.Internal);

            GameModules allAvailableModules = new(dictionary: modules);
            foreach (KeyValuePair<string, GameModuleInfo> kv in internalModules) {
                allAvailableModules[key: kv.Key] = kv.Value;
            }

            string gameName;
            while (true) {
                try {
                    SafeClear();
                    if (msg is not null) {
                        System.Console.WriteLine(msg);
                    }
                    System.Console.WriteLine("Select a game:");

                    List<string> gameMenu = new List<string>();
                    List<string> gameKeyMap = new List<string>();

                    // Build menu with states
                    // foreach module, display '<Name> [<isRegistered>, <isInstalled (always true here)>, <isBuilt>]'
                    foreach ((string Display, string Name) item in modules.Values.Select(selector: m => (Display: $"{m.Name}  [{m.DescribeState()}]", m.Name))) {
                        gameMenu.Add(item: item.Display);
                        gameKeyMap.Add(item: item.Name);
                    }
                    gameMenu.Add(item: "---------------"); // separator before public modules
                    gameKeyMap.Add(item: "---"); // placeholder for separator

                    // Add public modules after game modules
                    foreach (Core.Data.GameModuleInfo m in internalModules.Values) {
                        gameMenu.Add(item: m.Name);
                        gameKeyMap.Add(item: m.Name);
                    }

                    gameMenu.Add(item: "Exit");
                    gameKeyMap.Add(item: "Exit"); // align with Exit index
                    // Prompt for selection
                    int gidx = SelectFromMenu(items: gameMenu, highlightSeparators: true, cancellationToken: cancellationToken);
                    if (gidx < 0 || gameMenu[index: gidx] == "Exit") {
                        return 0;
                    }

                    // Get selected game name
                    string gsel = gameMenu[index: gidx];

                    // Map selection index to actual module key
                    if (gidx >= 0 && gidx < gameKeyMap.Count) {
                        gameName = gameKeyMap[index: gidx];
                        Shared.IO.Diagnostics.Trace($"Selected game: {gameName}");
                    } else {
                        // Fallback: treat selection as raw name
                        gameName = gsel;
                        Shared.IO.Diagnostics.Trace($"Warning: could not map selected index {gidx} to module key; using raw selection '{gsel}'");
                    }
                    break; // exit game selection loop
                } catch (OperationCanceledException) {
                    Shared.IO.Diagnostics.Log("Exiting gracefully due to cancellation during game selection.");
                    throw;
                } catch (Exception ex) {
                    Shared.IO.Diagnostics.Bug($"Error during game selection: {ex.Message}");
                    System.Console.WriteLine($"Error during game selection: {ex.Message}\nPress any key to try again...");
                    SafeReadKey(intercept: true, cancellationToken: cancellationToken);
                }
            }

            ModuleOperationSession operationSession = Engine.OperationsService_LoadModuleSession(gameName: gameName, games: allAvailableModules);
            GameModuleInfo? info = operationSession.Module;
            PreparedOperations preparedOps = operationSession.PreparedOperations;

            if (info is null) {
                Shared.IO.Diagnostics.Log("Selected game not found.");
                return await RunAsync(msg: preparedOps.ErrorMessage ?? "Selected game not found. Please choose again.");
            }
            if (!preparedOps.IsLoaded) {
                string message = preparedOps.ErrorMessage ?? "Failed to load operations list.";
                Shared.IO.Diagnostics.Log($"{message}");
                System.Console.WriteLine($"{message} Press any key to exit...");
                SafeReadKey(intercept: true, cancellationToken: cancellationToken);
                Shared.IO.Diagnostics.Log("Exiting due to failed ops load.");
                return 1;
            }

            if (preparedOps.Warnings.Count > 0) {
                foreach (string warning in preparedOps.Warnings) {
                    Shared.IO.Diagnostics.Log($"Warning: {warning}");
                }
            }

            // Auto-run init operations once when a game is selected
            if (preparedOps.InitOperations.Count > 0) {
                SafeClear();
                System.Console.WriteLine($"Running {preparedOps.InitOperations.Count} initialization operation(s) for {gameName}\n");
                System.Diagnostics.Stopwatch initStopwatch = System.Diagnostics.Stopwatch.StartNew();
                bool okAllInit = true;
                foreach (Core.Data.PreparedOperation op in preparedOps.InitOperations) {
                    Core.Data.PromptAnswers promptAnswers = new Core.Data.PromptAnswers();
                    // Initialization runs non-interactively; use defaults when provided
                    await CollectAnswersForOperation(op: op.Operation, answers: promptAnswers, defaultsOnly: true);
                    TuiRenderer.ResetContext(clearLogs: false);
                    TuiRenderer.SetCancellationMode(mode: TuiRenderer.CancellationMode.Disabled);
                    bool ok = await new Utils().ExecuteOpAsync(Engine: Engine, game: gameName, games: allAvailableModules, op: op.Operation, promptAnswers: promptAnswers, cancellationToken: cancellationToken);
                    okAllInit &= ok;
                    TuiRenderer.SetCancellationMode(mode: TuiRenderer.CancellationMode.PromptsOnly);
                }
                initStopwatch.Stop();

                Shared.IO.Diagnostics.Trace($"Completed init operations for {gameName} in {FormatElapsed(elapsed: initStopwatch.Elapsed)}. Success: {okAllInit}");
                System.Console.WriteLine(okAllInit
                    ? $"Initialization completed successfully. Time: {FormatElapsed(elapsed: initStopwatch.Elapsed)}. Press any key to continue..."
                    : $"One or more init operations failed. Time: {FormatElapsed(elapsed: initStopwatch.Elapsed)}. Press any key to continue...");
                SafeReadKey(intercept: true, cancellationToken: cancellationToken);
            }

            // operations menu
            while (true) {
                SafeClear();
                System.Console.WriteLine($"--- Operations for: {gameName}");
                List<string> menu = new List<string>();
                int opStartIndex = 0;

                // show a 'Play' option if isBuilt is true for the module, indicating the game is ready to run
                if (info.IsBuilt) {
                    menu.Add(item: "Play");
                    menu.Add(item: "---------------");
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
                    } else if (op.HasInvalidId) {
                        name = $"[invalid-id] {name}";
                    }

                    if (sessionOperation.Status == OperationExecutionStatus.Succeeded) {
                        name += " [completed]";
                    } else if (sessionOperation.Status == OperationExecutionStatus.Failed) {
                        name += " [failed]";
                    }

                    menu.Add(item: name);
                }

                // ---------------

                menu.Add(item: "---------------");
                menu.Add(item: "Change Game");
                menu.Add(item: "Exit");

                System.Console.WriteLine("? Select an operation: (Use arrow keys)");
                int idx = SelectFromMenu(items: menu, highlightSeparators: true, cancellationToken: cancellationToken);
                if (idx < 0) {
                    // if idx < 0 (eg. Pressed Escape), return to the game selection menu
                    return await RunAsync();
                }

                string selection = menu[index: idx];
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

                        SafeReadKey(intercept: true, cancellationToken: cancellationToken);
                        continue;
                    }
                    case "Run All": {
                        using CancellationTokenSource runAllCts = CancellationTokenSource.CreateLinkedTokenSource(token: cancellationToken);
                        //using CancellationTokenSource runAllCts = new CancellationTokenSource();

                        // Register global intercept for Lua script prompts while TUI runs
                        Func<string, bool, string?>? oldPromptHandler = Shared.IO.UI.EngineSdk.ExternalPromptHandler;
                        Shared.IO.UI.EngineSdk.ExternalPromptHandler = (msg, sec) => TuiRenderer.ReadLineCustom(label: $"{msg} >", isSecret: sec);

                        try {
                            TuiRenderer.Initialize(cts: runAllCts);
                            TuiRenderer.SetCancellationMode(mode: TuiRenderer.CancellationMode.PromptsOnly);
                            TuiRenderer.ResetContext(clearLogs: false);
                            TuiRenderer.Log($"Running operations for {gameName}...", color: ConsoleColor.Cyan);

                            System.Diagnostics.Stopwatch runAllStopwatch = System.Diagnostics.Stopwatch.StartNew();
                            Core.ProcessRunner.StdinProvider rendererInput = () => TuiRenderer.ReadLineCustom(label: "Input >", isSecret: false);

                            Core.Operations.RunAllResult result = await Engine.RunAllAsync(
                                gameName: gameName,
                                onOutput: Utils.OnOutput,
                                onEvent: Utils.OnEvent,
                                stdinProvider: rendererInput,
                                cancellationToken: runAllCts.Token
                            );
                            runAllStopwatch.Stop();

                            TuiRenderer.Log(result.Success ? "Completed successfully." : "One or more operations failed.",
                                color: result.Success ? ConsoleColor.Green : ConsoleColor.Red);

                            TuiRenderer.Log($"({result.SucceededOperations}/{result.TotalOperations} operations succeeded). Time: {FormatElapsed(elapsed: runAllStopwatch.Elapsed)}.", color: ConsoleColor.White);
                            TuiRenderer.WaitForKey();
                            continue;
                        } catch (System.Exception ex) {
                            TuiRenderer.Log($"Error: {ex.Message}", color: ConsoleColor.Red);
                            Shared.IO.Diagnostics.Bug($"Error during Run All: {ex.Message}");
                            TuiRenderer.WaitForKey();
                            continue;
                        } finally {
                            Shared.IO.UI.EngineSdk.ExternalPromptHandler = oldPromptHandler;
                            TuiRenderer.SetCancellationMode(mode: TuiRenderer.CancellationMode.PromptsOnly);
                            TuiRenderer.Shutdown();
                        }
                    }
                }

                int opIndex = idx - opStartIndex;
                if (opIndex < 0 || opIndex >= preparedOps.RegularOperations.Count) {
                    continue;
                }

                {
                    Dictionary<string, object?> op = preparedOps.RegularOperations[index: opIndex].Operation;
                    PromptAnswers answers = new PromptAnswers();
                    using CancellationTokenSource opCts = CancellationTokenSource.CreateLinkedTokenSource(token: cancellationToken);

                    Func<string, bool, string?>? oldPromptHandler = Shared.IO.UI.EngineSdk.ExternalPromptHandler;
                    Shared.IO.UI.EngineSdk.ExternalPromptHandler = (msg, sec) => TuiRenderer.ReadLineCustom(label: $"{msg} >", isSecret: sec);

                    TuiRenderer.Initialize(cts: opCts);
                    TuiRenderer.SetCancellationMode(mode: TuiRenderer.CancellationMode.PromptsOnly);

                    try {
                        Task<bool> promptTask = CollectAnswersForOperation(op: op, answers: answers, defaultsOnly: false, cancellationToken: opCts.Token);
                        if (await promptTask) {
                            TuiRenderer.Log($"Running: {selection}\n", color: ConsoleColor.Cyan);
                            System.Diagnostics.Stopwatch opStopwatch = System.Diagnostics.Stopwatch.StartNew();
                            TuiRenderer.ResetContext(clearLogs: false);
                            TuiRenderer.SetCancellationMode(mode: TuiRenderer.CancellationMode.Disabled);

                            bool ok = await new Utils().ExecuteOpAsync(Engine: Engine, game: gameName, games: allAvailableModules, op: op, promptAnswers: answers, cancellationToken: opCts.Token);
                            opStopwatch.Stop();

                            TuiRenderer.Log(ok
                                    ? $"Completed successfully. Time: {FormatElapsed(elapsed: opStopwatch.Elapsed)}."
                                    : $"Operation failed. Time: {FormatElapsed(elapsed: opStopwatch.Elapsed)}.",
                                color: ok ? ConsoleColor.Green : ConsoleColor.Red);

                            TuiRenderer.WaitForKey();
                        }
                    } catch (System.Exception ex) {
                        TuiRenderer.Log($"Error: {ex.Message}", color: ConsoleColor.Red);
                        TuiRenderer.WaitForKey();
                    } finally {
                        Shared.IO.UI.EngineSdk.ExternalPromptHandler = oldPromptHandler;
                        TuiRenderer.SetCancellationMode(mode: TuiRenderer.CancellationMode.PromptsOnly);
                        TuiRenderer.Shutdown();
                    }
                }
            }
        } catch (OperationCanceledException) {
            Shared.IO.Diagnostics.Log("Exiting gracefully due to cancellation.");
            throw;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Error: {ex}");
            System.Console.WriteLine($"Error: {ex.Message}\nPress any key to exit...");
            SafeReadKey(intercept: true, cancellationToken: cancellationToken);
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
            Shared.IO.Diagnostics.Bug($"Error clearing console: {e.Message}");
        }
    }

    /// <summary>
    /// Safely reads a key from the console if it is not redirected.
    /// Returns an empty ConsoleKeyInfo if redirection is detected or on error.
    /// </summary>
    /// <param name="intercept">Whether to intercept the key</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The key information read from the console, or an empty ConsoleKeyInfo if input is redirected or an error occurs.</returns>
    private static System.ConsoleKeyInfo SafeReadKey(bool intercept = false, CancellationToken cancellationToken = default(CancellationToken)) {
        try {
            if (!System.Console.IsInputRedirected) {
                // Disable OS-level kill signal so Ctrl+C acts as standard input
                bool previousControlCSetting = System.Console.TreatControlCAsInput;
                System.Console.TreatControlCAsInput = true;

                try {
                    while (!System.Console.KeyAvailable) {
                        cancellationToken.ThrowIfCancellationRequested();
                        System.Threading.Thread.Sleep(millisecondsTimeout: 10);
                    }

                    System.ConsoleKeyInfo keyInfo = System.Console.ReadKey(intercept: intercept);

                    // Manually intercept the Ctrl+C keystroke and trigger graceful cancellation
                    if (keyInfo.Key == ConsoleKey.C && keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control)) {
                        throw new OperationCanceledException("Cancellation requested via manual Ctrl+C intercept.");
                    }

                    return keyInfo;
                }
                finally {
                    // Restore the original state so the rest of the application dictates behavior
                    System.Console.TreatControlCAsInput = previousControlCSetting;
                }
            }
        } catch (OperationCanceledException) {
            Shared.IO.Diagnostics.Trace("exiting");
            throw;
        } catch (System.Exception e) {
            Shared.IO.Diagnostics.Bug($"Error reading key: {e.Message}");
        }

        return new ConsoleKeyInfo(keyChar: '\0', key: 0, shift: false, alt: false, control: false);
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
            Shared.IO.Diagnostics.Bug($"Error setting cursor visibility: {e.Message}");
        }
    }

    private static bool CanUseInteractiveMenu() {
        try {
            if (System.Console.IsOutputRedirected || System.Console.IsInputRedirected) {
                return false;
            }
            return System.Console.WindowHeight > 5;
        } catch {
            Shared.IO.Diagnostics.Bug("Error checking console capabilities.");
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
    private static int SelectFromMenu(IList<string> items, bool highlightSeparators = false, HashSet<int>? disabledIndices = null, System.Threading.CancellationToken cancellationToken = default) {
        try {
            if (items.Count == 0) {
                return -1;
            }

            if (!CanUseInteractiveMenu()) {
                return SelectFromNumberedMenu(items: items, highlightSeparators: highlightSeparators, disabledIndices: disabledIndices, cancellationToken: cancellationToken);
            }

            int index = 0;
            while (index < items.Count && (items[index: index] == "---------------" || (disabledIndices?.Contains(item: index) ?? false))) {
                index++;
            }

            if (index >= items.Count) {
                index = -1;
                for (int i = 0; i < items.Count; i++) {
                    if (items[index: i] == "---------------" || (disabledIndices?.Contains(item: i) ?? false)) {
                        continue;
                    }
                    index = i;
                    break;
                }
            }

            int renderTop = System.Console.CursorTop;
            int scrollOffset = 0;

            while (true) {
                SafeSetCursorVisible(visible: false);
                int maxLines;
                int windowWidth;

                try {
                    maxLines = System.Math.Max(val1: 3, val2: System.Console.WindowHeight - renderTop - 3);
                    windowWidth = System.Math.Max(val1: 20, val2: System.Console.WindowWidth);
                    System.Console.SetCursorPosition(left: 0, top: renderTop);
                } catch (System.ArgumentOutOfRangeException) {
                    SafeSetCursorVisible(visible: true);
                    return SelectFromNumberedMenu(items: items, highlightSeparators: highlightSeparators, disabledIndices: disabledIndices);
                }

                if (index != -1) {
                    if (index < scrollOffset) {
                        scrollOffset = index;
                    } else if (index >= scrollOffset + maxLines) {
                        scrollOffset = index - maxLines + 1;
                    }
                }

                if (scrollOffset > 0) {
                    System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                    System.Console.WriteLine("  ▲ (more items above)".PadRight(totalWidth: windowWidth - 1));
                    System.Console.ResetColor();
                } else {
                    System.Console.WriteLine("".PadRight(totalWidth: windowWidth - 1));
                }

                int visibleEnd = System.Math.Min(val1: items.Count, val2: scrollOffset + maxLines);
                for (int i = scrollOffset; i < visibleEnd; i++) {
                    string line = items[index: i];
                    bool isSep = line == "---------------";
                    bool isDisabled = disabledIndices?.Contains(item: i) ?? false;

                    if (i == index) {
                        System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                        System.Console.WriteLine($"> {line}".PadRight(totalWidth: windowWidth - 1));
                        System.Console.ResetColor();
                    } else if (isDisabled) {
                        System.Console.ForegroundColor = System.ConsoleColor.DarkGray;
                        System.Console.WriteLine($"  {line} (Already downloaded)".PadRight(totalWidth: windowWidth - 1));
                        System.Console.ResetColor();
                    } else {
                        if (isSep && highlightSeparators) {
                            System.Console.ForegroundColor = System.ConsoleColor.DarkGray;
                            System.Console.WriteLine($"  {line}".PadRight(totalWidth: windowWidth - 1));
                            System.Console.ResetColor();
                        } else {
                            System.Console.WriteLine($"  {line}".PadRight(totalWidth: windowWidth - 1));
                        }
                    }
                }

                if (visibleEnd < items.Count) {
                    System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                    System.Console.WriteLine("  ▼ (more items below)".PadRight(totalWidth: windowWidth - 1));
                    System.Console.ResetColor();
                } else {
                    System.Console.WriteLine("".PadRight(totalWidth: windowWidth - 1));
                }

                if (index == -1) {
                    System.Console.WriteLine("\nNo selectable options. Press any key to return...".PadRight(totalWidth: windowWidth - 1));
                    SafeReadKey(intercept: true, cancellationToken: cancellationToken);
                    SafeSetCursorVisible(visible: true);
                    return -1;
                }

                ConsoleKeyInfo keyInfo = SafeReadKey(intercept: true, cancellationToken: cancellationToken);
                switch (keyInfo.Key) {
                    case ConsoleKey.DownArrow: {
                        int next = index;
                        do {
                            next = (next + 1) % items.Count;
                        } while (next != index && (items[index: next] == "---------------" || (disabledIndices?.Contains(item: next) ?? false)));
                        index = next;

                        if (index < scrollOffset) {
                            scrollOffset = index;
                        }
                        break;
                    }
                    case ConsoleKey.UpArrow: {
                        int prev = index;
                        do {
                            prev = (prev - 1 + items.Count) % items.Count;
                        } while (prev != index && (items[index: prev] == "---------------" || (disabledIndices?.Contains(item: prev) ?? false)));
                        index = prev;

                        if (index >= scrollOffset + maxLines) {
                            scrollOffset = index - maxLines + 1;
                        }
                        break;
                    }
                    case ConsoleKey.Escape: {
                        SafeSetCursorVisible(visible: true);
                        return -1;
                    }
                    case ConsoleKey.Enter: {
                        SafeSetCursorVisible(visible: true);
                        return index;
                    }
                }
            }
        } catch (OperationCanceledException) {
            Shared.IO.Diagnostics.Log("Exiting gracefully due to cancellation.");
            throw;
        } catch (System.Exception) {
            Shared.IO.Diagnostics.Bug("Error in SelectFromMenu");
            return -1;
        }
    }

    private static int SelectFromNumberedMenu(IList<string> items, bool highlightSeparators, HashSet<int>? disabledIndices = null, System.Threading.CancellationToken cancellationToken = default) {
        try {
            List<int> selectable = new List<int>();

            System.Console.WriteLine();
            System.Console.WriteLine("Terminal is too small for the interactive menu. Enter the option number instead:");

            int displayIndex = 1;
            for (int i = 0; i < items.Count; i++) {
                string line = items[index: i];
                bool isSep = line == "---------------";
                bool isDisabled = disabledIndices?.Contains(item: i) ?? false;

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
                    selectable.Add(item: i);
                }
                displayIndex++;
            }

            if (selectable.Count == 0) {
                System.Console.WriteLine("\nNo selectable options. Press any key to return...");
                SafeReadKey(intercept: true, cancellationToken: cancellationToken);
                return -1;
            }

            while (true) {
                System.Console.Write("Selection (blank or Escape to cancel): ");
                string? input = ReadLineWithCancel(cancelled: out bool cancelled, cancellationToken: cancellationToken);
                if (cancelled || string.IsNullOrWhiteSpace(input)) {
                    return -1;
                }

                if (int.TryParse(s: input.Trim(), result: out int choice) && choice >= 1 && choice <= (displayIndex - 1)) {
                    int currentDisplay = 1;
                    for (int i = 0; i < items.Count; i++) {
                        if (items[index: i] == "---------------") continue;
                        if (currentDisplay == choice) {
                            if (!(disabledIndices?.Contains(item: i) ?? false)) return i;
                            System.Console.WriteLine("That option is already downloaded and cannot be selected.");
                            break;
                        }
                        currentDisplay++;
                    }
                }

                System.Console.WriteLine("Invalid selection. Please enter a valid number.");
            }
        } catch (OperationCanceledException) {
            throw;
        } catch (System.Exception) {
            Shared.IO.Diagnostics.Bug("Error in SelectFromMenuFallback");
            return -1;
        }
    }

    private async Task<bool> CollectAnswersForOperation(Dictionary<string, object?> op, PromptAnswers answers, bool defaultsOnly, CancellationToken cancellationToken = default(CancellationToken)) {
        try {
            static Task<PromptResponse> promptHandler(PromptRequest request, CancellationToken cancellationToken) {
                switch (request.Type) {
                    case "select": {
                        List<string> choicesList = request.Choices.Select(selector: choice => choice.Label).ToList();
                        HashSet<int> disabled = request.Choices
                            .Select(selector: (choice, index) => new { choice.IsDisabled, index })
                            .Where(predicate: entry => entry.IsDisabled)
                            .Select(selector: entry => entry.index)
                            .ToHashSet();

                        if (choicesList.Count == 0) {
                            TuiRenderer.Log($"No choices available for {request.Title}.", color: ConsoleColor.Yellow);
                            if (request.DefaultValue is not null) {
                                return Task.FromResult(result: PromptResponse.UseDefaultValue());
                            }
                            return Task.FromResult(result: PromptResponse.FromValue(null));
                        }

                        TuiRenderer.Log($"{request.Title}:", color: ConsoleColor.Cyan);
                        for (int i = 0; i < choicesList.Count; i++) {
                            TuiRenderer.Log($"{i + 1}. {choicesList[index: i]}{(disabled.Contains(item: i) ? " (Disabled)" : "")}");
                        }

                        string? input = TuiRenderer.ReadLineCustom(label: "Selection # >", isSecret: false);

                        if (input == null) return Task.FromResult(result: PromptResponse.Cancelled());

                        if (string.IsNullOrWhiteSpace(input) || !int.TryParse(s: input, result: out int choiceIdx) ||
                            choiceIdx < 1 || choiceIdx > choicesList.Count) {
                            return Task.FromResult(result: PromptResponse.Cancelled());
                        }

                        int actualIdx = choiceIdx - 1;
                        if (!disabled.Contains(item: actualIdx)) {
                            return Task.FromResult(result: PromptResponse.FromValue(choicesList[index: actualIdx]));
                        }

                        TuiRenderer.Log("Selected item is disabled.", color: ConsoleColor.Red);
                        return Task.FromResult(result: PromptResponse.Cancelled());
                    }
                    case "confirm": {
                        bool defVal = request.DefaultValue is true;
                        string defHint = defVal ? "Y" : "N";
                        string? c = TuiRenderer.ReadLineCustom(label: $"{request.Title} [y/N] (default {defHint}) >", isSecret: false);

                        if (c == null) return Task.FromResult(result: PromptResponse.Cancelled());

                        if (string.IsNullOrWhiteSpace(c)) {
                            return Task.FromResult(result: PromptResponse.UseDefaultValue());
                        }

                        bool val = c.Trim().StartsWith("y", comparisonType: StringComparison.OrdinalIgnoreCase);
                        return Task.FromResult(result: PromptResponse.FromValue(val));
                    }
                    case "checkbox": {
                        if (request.Choices.Count > 0) {
                            TuiRenderer.Log($"{request.Title} - choose one or more (comma-separated).", color: ConsoleColor.Cyan);
                            for (int i = 0; i < request.Choices.Count; i++) {
                                TuiRenderer.Log($"{i + 1}. {request.Choices[index: i].Label}");
                            }
                        } else {
                            TuiRenderer.Log($"{request.Title} (comma-separated values): ", color: ConsoleColor.Cyan);
                        }

                        string? line = TuiRenderer.ReadLineCustom(label: "Values >", isSecret: false);

                        if (line == null) return Task.FromResult(result: PromptResponse.Cancelled());

                        if (string.IsNullOrWhiteSpace(line)) {
                            if (request.DefaultValue is IList<object?>) {
                                return Task.FromResult(result: PromptResponse.UseDefaultValue());
                            }
                            return Task.FromResult(result: PromptResponse.FromValue(new List<object?>()));
                        }

                        List<object?> selected = line.Split(separator: ',', options: StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Cast<object?>().ToList();
                        return Task.FromResult(result: PromptResponse.FromValue(selected));
                    }
                    case "text": {
                        string? v = TuiRenderer.ReadLineCustom(label: $"{request.Title} >", isSecret: false);

                        if (v == null) return Task.FromResult(result: PromptResponse.Cancelled());

                        if (!string.IsNullOrWhiteSpace(v)) {
                            return Task.FromResult(result: PromptResponse.FromValue(v));
                        }

                        if (request.DefaultValue is not null) {
                            return Task.FromResult(result: PromptResponse.UseDefaultValue());
                        }

                        return Task.FromResult(result: PromptResponse.FromValue(string.Empty));
                    }
                    default: {
                        TuiRenderer.Log($"Unsupported prompt type: {request.Type}", color: ConsoleColor.Red);
                        string? vv = TuiRenderer.ReadLineCustom(label: $"{request.Title} >", isSecret: false);

                        if (vv == null) return Task.FromResult(result: PromptResponse.Cancelled());

                        if (!string.IsNullOrWhiteSpace(vv)) {
                            return Task.FromResult(result: PromptResponse.FromValue(vv));
                        }

                        if (request.DefaultValue is not null) {
                            return Task.FromResult(result: PromptResponse.UseDefaultValue());
                        }

                        return Task.FromResult(result: PromptResponse.FromValue(string.Empty));
                    }
                }
            }

            return await Engine.OperationsService_CollectAnswersAsync(op: op, answers: answers, promptHandler: promptHandler, defaultsOnly: defaultsOnly, cancellationToken: cancellationToken);
        } catch (OperationCanceledException) {
            Shared.IO.Diagnostics.Trace("Operation cancelled by user.");
            throw;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Error during interactive prompts: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reads a line from console, returning null if Escape is pressed.
    /// </summary>
    private static string? ReadLineWithCancel(out bool cancelled, System.Threading.CancellationToken cancellationToken = default) {
        try {
            cancelled = false;
            StringBuilder sb = new StringBuilder();
            while (true) {
                ConsoleKeyInfo key = SafeReadKey(intercept: true, cancellationToken: cancellationToken);
                switch (key.Key) {
                    case ConsoleKey.Enter:
                        System.Console.WriteLine();
                        return sb.ToString();
                    case ConsoleKey.Escape:
                        cancelled = true;
                        System.Console.WriteLine();
                        return null;
                    case ConsoleKey.Backspace when sb.Length <= 0:
                        continue;
                    case ConsoleKey.Backspace:
                        sb.Remove(startIndex: sb.Length - 1, length: 1);
                        System.Console.Write("\b \b");
                        break;
                    default: {
                        if (!char.IsControl(c: key.KeyChar)) {
                            sb.Append(key.KeyChar);
                            System.Console.Write(key.KeyChar);
                        }
                        break;
                    }
                }
            }
        } catch {
            Shared.IO.Diagnostics.Bug("Failed to read line from console due to an exception.");
            cancelled = true;
            return null;
        }
    }
}