
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace EngineNet.Interface.Terminal;

internal partial class TUI {

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
            if (!op.TryGetValue("prompts", out object? promptsObj) || promptsObj is not IList<object?> prompts) {
                return true;
            }

            // Helper to set an empty value based on prompt type
            static object? EmptyForType(string t) => t switch {
                "confirm" => false,
                "checkbox" => new List<object?>(),
                "select" => null,
                _ => null
            };

            if (defaultsOnly) {
                // In defaultsOnly mode, we don't prompt. Apply defaults while respecting conditions.
                foreach (object? p in prompts) {
                    if (p is not Dictionary<string, object?> prompt) {
                        continue;
                    }

                    string name = prompt.TryGetValue("Name", out object? n) ? n?.ToString() ?? "" : (prompt.TryGetValue("name", out object? n2) ? n2?.ToString() ?? "" : "");
                    string type = prompt.TryGetValue("type", out object? t) ? t?.ToString() ?? "" : (prompt.TryGetValue("Type", out object? t2) ? t2?.ToString() ?? "" : "");
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) {
                        continue;
                    }

                    // Evaluate condition if present using current 'answers' state
                    if (prompt.TryGetValue("condition", out object? condObj) && condObj is string condName) {
                        if (!answers.TryGetValue(condName, out object? condVal)) {
                            // If condition value not yet present, attempt to seed from its default (if a matching prompt exists earlier or later)
                            // Find the prompt with Name == condName and use its default if any
                            foreach (object? q in prompts) {
                                if (q is Dictionary<string, object?> qp && (qp.TryGetValue("Name", out object? qn) ? qn?.ToString() : (qp.TryGetValue("name", out object? qn2) ? qn2?.ToString() : null)) == condName) {
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
                return true;
            }

            // Interactive mode: walk prompts in order, honoring conditions
            foreach (object? p in prompts) {
                if (p is not Dictionary<string, object?> prompt) {
                    continue;
                }

                string? name = prompt.TryGetValue("Name", out object? n) ? n?.ToString() : (prompt.TryGetValue("name", out object? n2) ? n2?.ToString() : null);
                string? type = prompt.TryGetValue("type", out object? tt) ? tt?.ToString() : (prompt.TryGetValue("Type", out object? tt2) ? tt2?.ToString() : null);
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) {
                    continue;
                }

                // Prefer 'message' or 'Message' or 'prompt' for the display label
                string displayLabel = "unnamed";
                if (prompt.TryGetValue("message", out object? m) && m != null) displayLabel = m.ToString()!;
                else if (prompt.TryGetValue("Message", out object? m2) && m2 != null) displayLabel = m2.ToString()!;
                else if (prompt.TryGetValue("prompt", out object? m3) && m3 != null) displayLabel = m3.ToString()!;
                else if (!string.IsNullOrEmpty(name)) displayLabel = name;

                // If there's a condition and it's false, skip asking and assign an empty value
                if (prompt.TryGetValue("condition", out object? cond) && cond is string condName && (!answers.TryGetValue(condName, out object? condVal) || condVal is not bool b || !b)) {
                    answers[name] = EmptyForType(type);
                    continue;
                }

                switch (type) {
                    case "select": {
                        List<string> choicesList = new();
                        HashSet<int>? disabled = null;

                        if (prompt.TryGetValue("choices_provider", out object? providerObj) && providerObj?.ToString() == "registry_modules") {
                            var result = GetRegistryModulesChoices();
                            choicesList = result.choices;
                            disabled = result.disabled;
                        } else if (prompt.TryGetValue("choices", out object? chObj) && chObj is IList<object?> chList) {
                            choicesList = chList.Select(x => x?.ToString() ?? "").ToList();
                        }

                        if (choicesList.Count == 0) {
                            System.Console.WriteLine($"No choices available for {displayLabel}.");
                            answers[name] = null;
                            break;
                        }

                        System.Console.WriteLine($"{displayLabel}:");
                        int selIdx = SelectFromMenu(choicesList, highlightSeparators: false, disabledIndices: disabled);
                        if (selIdx < 0) return false; // Cancelled via Escape
                        answers[name] = choicesList[selIdx];
                        break;
                    }
                    case "confirm": {
                        // Show default hint when available
                        string defHint = prompt.TryGetValue("default", out object? dv) && dv is bool db ? (db ? "Y" : "N") : "N";
                        System.Console.Write($"{displayLabel} [y/N] (default {defHint}): ");
                        string? c = ReadLineWithCancel(out bool cancelled);
                        if (cancelled) return false;

                        bool val = c != null && c.Trim().Length > 0
                            ? c.Trim().StartsWith("y", System.StringComparison.OrdinalIgnoreCase)
                            : (prompt.TryGetValue("default", out object? d) && d is bool bd && bd);
                        answers[name] = val;
                        break;
                    }

                    case "checkbox": {
                        // Present choices if available
                        if (prompt.TryGetValue("choices", out object? ch) && ch is IList<object?> choices && choices.Count > 0) {
                            System.Console.WriteLine($"{displayLabel} - choose one or more (comma-separated). Choices: {string.Join(", ", choices.Select(x => x?.ToString()))}");
                        } else {
                            System.Console.WriteLine($"{displayLabel} (comma-separated values): ");
                        }
                        string? line = ReadLineWithCancel(out bool cancelled);
                        if (cancelled) return false;

                        line ??= string.Empty;
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
                        System.Console.Write($"{displayLabel}: ");
                        string? v = ReadLineWithCancel(out bool cancelled);
                        if (cancelled) return false;

                        answers[name] = string.IsNullOrEmpty(v) && prompt.TryGetValue("default", out object? defVal) ? defVal : v;
                        break;
                    }
                }
            }
            return true;
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
