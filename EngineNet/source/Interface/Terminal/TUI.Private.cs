
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace EngineNet.Interface.Terminal;

internal partial class TUI {

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
#if DEBUG
            Trace.WriteLine("[TUI.private.cs::CanUseInteractiveMenu()] Error checking console capabilities.");
#endif
            return false;
        }
    }

    private static int SelectFromMenu(IList<string> items, bool highlightSeparators = false) {
        try {
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
        } catch (System.Exception) {
#if DEBUG
            Trace.WriteLine("[TUI.private.cs::SelectFromMenu()] Error in SelectFromMenu");
#endif
            return -1;
        }
    }

    private static int SelectFromMenuFallback(IList<string> items, bool highlightSeparators) {
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
#if DEBUG
            Trace.WriteLine("[TUI.private.cs::SelectFromMenuFallback()] Error in SelectFromMenuFallback");
#endif
            return -1;
        }
    }

    // git Download Menu
    private void ShowDownloadMenu() {
        try {
            while (true) {
                System.Console.Clear();
                System.Console.WriteLine("Download module:");
                List<string> items = new List<string> {
                    "From local registry (EngineApps\\Registries\\Modules)...",
                    "From Git URL...",
                    "Back"
                };
                System.Console.WriteLine("? Choose a source:");
                int idx = SelectFromMenu(items);
                // Handle cancel/back
                if (idx < 0 || items[idx] == "Back") {
#if DEBUG
                    Trace.WriteLine("[TUI.private.cs::ShowDownloadMenu()] User cancelled or selected Back in download menu, returning to previous menu.");
#endif
                    return;
                }

                string choice = items[idx];
                if (choice.StartsWith("From local registry")) {
                    // Load registry entries and list modules
                    // filter to get only modules that are registered but not installed.
                    IReadOnlyDictionary<string, Core.Utils.GameModuleInfo> regs = _engine.Modules(Core.Utils.ModuleFilter.Uninstalled);

                    if (regs.Count == 0) {
                        System.Console.WriteLine("No uninstalled modules found in registry. Press any key to go back...");
                        System.Console.ReadKey(true);
                        continue;
                    }

                    List<string> names = regs.Keys.OrderBy(k => k, System.StringComparer.OrdinalIgnoreCase).ToList();
                    names.Add("Back");
                    System.Console.Clear();
                    System.Console.WriteLine("Select a module to download:");
                    int mIdx = SelectFromMenu(names);
                    if (mIdx < 0 || names[mIdx] == "Back") {
#if DEBUG
                        Trace.WriteLine("[TUI.private.cs::ShowDownloadMenu()] User cancelled or selected Back in registry module list, returning to download menu.");
#endif
                        continue;
                    }

                    string name = names[mIdx];
                    if (!regs.TryGetValue(name, out Core.Utils.GameModuleInfo? obj)) {
                        System.Console.WriteLine("Invalid module entry. Press any key...");
                        System.Console.ReadKey(true);
                        continue;
                    }
                    string? url = obj.Url;
                    if (string.IsNullOrWhiteSpace(url)) {
                        System.Console.WriteLine("Selected module has no URL. Press any key...");
                        System.Console.ReadKey(true);
                        continue;
                    }
                    _engine.DownloadModule(url);
                    // After download, return to previous menu so games list can refresh
                    return;
                } else if (choice.StartsWith("From Git URL")) {
                    string url = PromptText("Enter Git URL of the module");
                    if (!string.IsNullOrWhiteSpace(url)) {
                        _engine.DownloadModule(url);
                    }

                    return;
                } else if (choice == "Back") {
                    return;
                } else {
#if DEBUG
                    Trace.WriteLine($"[TUI.private.cs::ShowDownloadMenu()] Unexpected choice in ShowDownloadMenu: {choice}");
#endif
                    return;
                }
            }
        } catch (System.Exception ex) {
#if DEBUG
            Trace.WriteLine($"[TUI.private.cs::ShowDownloadMenu()] Error in ShowDownloadMenu: {ex.Message}");
#endif
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
#if DEBUG
            Trace.WriteLine($"[TUI.private.cs::PromptUser()] Error during interactive prompts: {ex.Message}");
#endif
        }
    }

}
