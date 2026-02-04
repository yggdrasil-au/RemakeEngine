
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
    /// <returns></returns>
    private static int SelectFromMenu(IList<string> items, bool highlightSeparators = false) {
        try {
            // no items
            if (items.Count == 0) {
                return -1;
            }

            // if interactive menu is not possible, fall back to numbered input selection
            if (!CanUseInteractiveMenu(items.Count)) {
                return SelectFromNumberedMenu(items, highlightSeparators);
            }

            int index = 0;
            int renderTop = System.Console.CursorTop;

            while (true) {
                SafeSetCursorVisible(false);

                try {
                    System.Console.SetCursorPosition(0, renderTop);
                } catch (System.ArgumentOutOfRangeException) {
                    SafeSetCursorVisible(true);
                    return SelectFromNumberedMenu(items, highlightSeparators);
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

                System.ConsoleKeyInfo keyInfo = SafeReadKey(true);
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
    /// <returns></returns>
    private static int SelectFromNumberedMenu(IList<string> items, bool highlightSeparators) {
        try {
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
        } catch (System.Exception) {
            Core.Diagnostics.Bug("[TUI.private.cs::SelectFromMenuFallback()] Error in SelectFromMenuFallback");
            return -1;
        }
    }

    private static string PromptText(string title) {
        System.Console.Write($"{title}: ");
        try {
            return System.Console.ReadLine() ?? string.Empty;
        } catch {
            return string.Empty;
        }
    }

    private static void CollectAnswersForOperation(Dictionary<string, object?> op, Dictionary<string, object?> answers, bool defaultsOnly) {
        try {
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

                string? name = prompt.TryGetValue("Name", out object? n) ? n?.ToString() : null;
                string? type = prompt.TryGetValue("type", out object? tt) ? tt?.ToString() : null;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) {
                    continue;
                }

                // If there's a condition and it's false, skip asking and assign an empty value
                if ((prompt.TryGetValue("condition", out object? cond) && cond is string condName) && (!answers.TryGetValue(condName, out object? condVal) || condVal is not bool b || !b)) {
                    answers[name] = EmptyForType(type);
                    continue;
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
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"[TUI.private.cs::PromptUser()] Error during interactive prompts: {ex.Message}");
        }
    }

}
