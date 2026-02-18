
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;

namespace EngineNet.Interface.Terminal;

internal partial class TUI {

    /// <summary>
    /// Formats elapsed time for completion summaries.
    /// </summary>
    private static string FormatElapsed(System.TimeSpan elapsed) {
        if (elapsed.TotalSeconds < 60) {
            return $"{elapsed.TotalSeconds:0.0}s";
        }

        if (elapsed.TotalMinutes < 60) {
            return $"{elapsed.Minutes}m {elapsed.Seconds:D2}s";
        }

        return $"{(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m {elapsed.Seconds:D2}s";
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
            Core.Diagnostics.Bug($"[TUI.private.cs::SafeClear()] Error clearing console: {e.Message}");
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
            Core.Diagnostics.Bug($"[TUI.private.cs::SafeReadKey()] Error reading key: {e.Message}");
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
            Core.Diagnostics.Bug($"[TUI.private.cs::SafeSetCursorVisible()] Error setting cursor visibility: {e.Message}");
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
            Core.Diagnostics.Bug("[TUI.private.cs::CanUseInteractiveMenu()] Error checking console capabilities.");
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
                    if (items[i] != "---------------" && !(disabledIndices?.Contains(i) ?? false)) {
                        index = i;
                        break;
                    }
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
            Core.Diagnostics.Bug("[TUI.private.cs::SelectFromMenu()] Error in SelectFromMenu");
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
            List<int> selectable = new();

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
                    int actualIndex = choice - 1; // This is a bit tricky since we skip seps but not disabled in display numbering
                    // Let's adjust selectable logic to be more robust or just find the item by display index if we displayed it
                    // Actually, the current `selectable` logic only adds non-sep and non-disabled items.
                    // But `displayIndex` increments for disabled too.
                    // Let's re-think:

                    // Simple approach: find the i where line wasn't sep and was matching display index
                    int currentDisplay = 1;
                    for (int i = 0; i < items.Count; i++) {
                        if (items[i] == "---------------") continue;
                        if (currentDisplay == choice) {
                            if (disabledIndices?.Contains(i) ?? false) {
                                System.Console.WriteLine("That option is already downloaded and cannot be selected.");
                                break;
                            }
                            return i;
                        }
                        currentDisplay++;
                    }
                }

                System.Console.WriteLine("Invalid selection. Please enter a valid number.");
            }
        } catch (System.Exception) {
            Core.Diagnostics.Bug("[TUI.private.cs::SelectFromMenuFallback()] Error in SelectFromMenuFallback");
            return -1;
        }
    }

    private string PromptText(string title) {
        System.Console.Write($"{title}: ");
        try {
            return System.Console.ReadLine() ?? string.Empty;
        } catch {
            return string.Empty;
        }
    }

    private (List<string> choices, HashSet<int> disabled) GetRegistryModulesChoices() {
        var registered = _engine.Modules(Core.Utils.ModuleFilter.Registered);
        var installed = _engine.Modules(Core.Utils.ModuleFilter.Installed);

        List<string> choices = new();
        HashSet<int> disabled = new();

        foreach (var kv in registered) {
            choices.Add(kv.Key);
            if (installed.ContainsKey(kv.Key)) {
                disabled.Add(choices.Count - 1);
            }
        }

        return (choices, disabled);
    }

    private bool CollectAnswersForOperation(Dictionary<string, object?> op, Dictionary<string, object?> answers, bool defaultsOnly) {
        try {
            if (_engine is null) {
                return false;
            }

            Core.Services.OperationsService.PromptHandler handler = request => {
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
                                return Task.FromResult(Core.Services.OperationsService.PromptResponse.UseDefaultValue());
                            }
                            return Task.FromResult(Core.Services.OperationsService.PromptResponse.FromValue(null));
                        }

                        // For Select types, we'll use TuiRenderer.Log to list options and ReadLineCustom for input
                        // as standard menus might break the layout. 
                        // Alternatively, we can still use SelectFromMenu if we are careful.
                        // Let's stick to the prompt style for now to be safe.
                        TuiRenderer.Log($"{request.Title}:", ConsoleColor.Cyan);
                        for (int i = 0; i < choicesList.Count; i++) {
                            TuiRenderer.Log($"{i + 1}. {choicesList[i]}{(disabled.Contains(i) ? " (Disabled)" : "")}");
                        }

                        string input = TuiRenderer.ReadLineCustom("Selection # >", false);
                        if (string.IsNullOrWhiteSpace(input)) {
                            return Task.FromResult(Core.Services.OperationsService.PromptResponse.Cancelled());
                        }

                        if (int.TryParse(input, out int choiceIdx) && choiceIdx >= 1 && choiceIdx <= choicesList.Count) {
                            int actualIdx = choiceIdx - 1;
                            if (disabled.Contains(actualIdx)) {
                                TuiRenderer.Log("Selected item is disabled.", ConsoleColor.Red);
                                return Task.FromResult(Core.Services.OperationsService.PromptResponse.Cancelled());
                            }
                            return Task.FromResult(Core.Services.OperationsService.PromptResponse.FromValue(choicesList[actualIdx]));
                        }
                        
                        return Task.FromResult(Core.Services.OperationsService.PromptResponse.Cancelled());
                    }
                    case "confirm": {
                        bool defVal = request.DefaultValue is bool b && b;
                        string defHint = defVal ? "Y" : "N";
                        string c = TuiRenderer.ReadLineCustom($"{request.Title} [y/N] (default {defHint}) >", false);

                        if (string.IsNullOrWhiteSpace(c)) {
                            return Task.FromResult(Core.Services.OperationsService.PromptResponse.UseDefaultValue());
                        }

                        bool val = c.Trim().StartsWith("y", System.StringComparison.OrdinalIgnoreCase);
                        return Task.FromResult(Core.Services.OperationsService.PromptResponse.FromValue(val));
                    }
                    case "checkbox": {
                        if (request.Choices.Count > 0) {
                            TuiRenderer.Log($"{request.Title} - choose one or more (comma-separated).", ConsoleColor.Cyan);
                            for (int i = 0; i < request.Choices.Count; i++) {
                                TuiRenderer.Log($"{i + 1}. {request.Choices[i].Label}");
                            }
                        } else {
                            TuiRenderer.Log($"{request.Title} (comma-separated values): ", ConsoleColor.Cyan);
                        }

                        string line = TuiRenderer.ReadLineCustom("Values >", false);

                        if (string.IsNullOrWhiteSpace(line)) {
                            if (request.DefaultValue is IList<object?>) {
                                return Task.FromResult(Core.Services.OperationsService.PromptResponse.UseDefaultValue());
                            }
                            return Task.FromResult(Core.Services.OperationsService.PromptResponse.FromValue(new List<object?>()));
                        }

                        List<object?> selected = line.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries).Cast<object?>().ToList();
                        return Task.FromResult(Core.Services.OperationsService.PromptResponse.FromValue(selected));
                    }
                    case "text":
                    default: {
                        string v = TuiRenderer.ReadLineCustom($"{request.Title} >", false);

                        if (string.IsNullOrWhiteSpace(v)) {
                            if (request.DefaultValue is not null) {
                                return Task.FromResult(Core.Services.OperationsService.PromptResponse.UseDefaultValue());
                            }
                            return Task.FromResult(Core.Services.OperationsService.PromptResponse.FromValue(string.Empty));
                        }

                        return Task.FromResult(Core.Services.OperationsService.PromptResponse.FromValue(v));
                    }
                }
            };

            return _engine.OperationsService.CollectAnswersAsync(op, answers, handler, defaultsOnly)
                .GetAwaiter()
                .GetResult();
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"[TUI.private.cs::PromptUser()] Error during interactive prompts: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reads a line from console, returning null if Escape is pressed.
    /// </summary>
    private static string? ReadLineWithCancel(out bool cancelled) {
        cancelled = false;
        System.Text.StringBuilder sb = new();
        while (true) {
            var key = SafeReadKey(intercept: true);
            if (key.Key == System.ConsoleKey.Enter) {
                System.Console.WriteLine();
                return sb.ToString();
            }
            if (key.Key == System.ConsoleKey.Escape) {
                cancelled = true;
                System.Console.WriteLine();
                return null;
            }
            if (key.Key == System.ConsoleKey.Backspace) {
                if (sb.Length > 0) {
                    sb.Remove(sb.Length - 1, 1);
                    System.Console.Write("\b \b");
                }
            } else if (!char.IsControl(key.KeyChar)) {
                sb.Append(key.KeyChar);
                System.Console.Write(key.KeyChar);
            }
        }
    }

}
