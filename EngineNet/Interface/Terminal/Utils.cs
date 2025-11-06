using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace EngineNet.Interface.Terminal;

/// <summary>
/// Utility methods for CLI/TUI handling, can also be used by GUI if needed
/// </summary>
internal class Utils {

    private static readonly System.Text.Json.JsonSerializerOptions s_jsonOpts = new() {
        WriteIndented = false,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    private static readonly object s_consoleLock = new();
    private static int _progressPanelTop;
    private static int _progressLastLines;

    internal Utils() {
        //
    }


    internal bool ExecuteOp(Core.Engine _engine, string game, Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games, Dictionary<string, object?> op, Dictionary<string, object?> answers, Dictionary<string, string>? autoPromptResponses = null) {
        string? type = (op.TryGetValue("script_type", out object? st) ? st?.ToString() : null)?.ToLowerInvariant();

        // Use embedded handlers for engine/lua/js/bms to avoid external dependencies
        if (type == "engine" || type == "lua" || type == "js" || type == "bms") {
            // Route in-process SDK events to our terminal renderer
            System.Action<Dictionary<string, object?>>? prevSink = Core.Utils.EngineSdk.LocalEventSink;
            bool prevMute = Core.Utils.EngineSdk.MuteStdoutWhenLocalSink;
            Dictionary<string, string> prevAutoResponses = new(Core.Utils.EngineSdk.AutoPromptResponses);
            try {
                // Set auto-prompt responses if provided
                if (autoPromptResponses != null && autoPromptResponses.Count > 0) {
                    Core.Utils.EngineSdk.AutoPromptResponses.Clear();
                    foreach (KeyValuePair<string, string> kv in autoPromptResponses) {
                        Core.Utils.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
                    }
                }

                Core.Utils.EngineSdk.LocalEventSink = OnEvent;
                Core.Utils.EngineSdk.MuteStdoutWhenLocalSink = true;
                return _engine.RunSingleOperationAsync(game, games, op, answers).GetAwaiter().GetResult();
            } finally {
                // Restore previous auto-prompt responses
                Core.Utils.EngineSdk.AutoPromptResponses.Clear();
                foreach (KeyValuePair<string, string> kv in prevAutoResponses) {
                    Core.Utils.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
                }

                Core.Utils.EngineSdk.LocalEventSink = prevSink;
                Core.Utils.EngineSdk.MuteStdoutWhenLocalSink = prevMute;
            }
        }

        // Default: build and execute as external command (e.g., python)
        List<string> parts = _engine.BuildCommand(game, games, op, answers);
        if (parts.Count < 2) {
            return false;
        }

        string title = op.TryGetValue("Name", out object? n) ? n?.ToString() ?? System.IO.Path.GetFileName(parts[1]) : System.IO.Path.GetFileName(parts[1]);
        return _engine.ExecuteCommand(
            parts,
            title,
            onOutput: OnOutput,
            onEvent: OnEvent,
            stdinProvider: StdinProvider,
            envOverrides: new Dictionary<string, object?> { ["TERM"] = "dumb" }
        );
    }

    private static void WriteColored(string message, System.ConsoleColor color) {
        System.ConsoleColor prev = System.Console.ForegroundColor;
        System.Console.ForegroundColor = color;
        System.Console.WriteLine(message);
        System.Console.ForegroundColor = prev;
    }

    private static System.ConsoleColor MapColor(string? name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return System.ConsoleColor.Gray;
        }

        switch (name.Trim().ToLowerInvariant()) {
            case "default":
                return System.ConsoleColor.Gray;
            case "black":
                return System.ConsoleColor.Black;
            case "darkblue":
                return System.ConsoleColor.DarkBlue;
            case "blue":
                return System.ConsoleColor.Blue;
            case "darkgreen":
                return System.ConsoleColor.DarkGreen;
            case "green":
                return System.ConsoleColor.Green;
            case "darkcyan":
                return System.ConsoleColor.DarkCyan;
            case "cyan":
                return System.ConsoleColor.Cyan;
            case "darkred":
                return System.ConsoleColor.DarkRed;
            case "red":
                return System.ConsoleColor.Red;
            case "darkmagenta":
                return System.ConsoleColor.DarkMagenta;
            case "magenta":
                return System.ConsoleColor.Magenta;
            case "darkyellow":
                return System.ConsoleColor.DarkYellow;
            case "yellow":
                return System.ConsoleColor.Yellow;
            case "gray":
            case "grey":
                return System.ConsoleColor.Gray;
            case "darkgray":
            case "darkgrey":
                return System.ConsoleColor.DarkGray;
            case "white":
                return System.ConsoleColor.White;
            default:
                return System.ConsoleColor.Gray;
        }
    }

    private static string? StdinProvider() {
        try {
            System.Console.Write("> ");
            return System.Console.ReadLine();
        } catch {
            return string.Empty;
        }
    }

    internal static void OnOutput(string line, string stream) {
        System.ConsoleColor prev = System.Console.ForegroundColor;
        try {
            System.Console.ForegroundColor = stream == "stderr" ? System.ConsoleColor.Red : System.ConsoleColor.Gray;
            System.Console.WriteLine(line);
        } finally { System.Console.ForegroundColor = prev; }
    }

    // --- Handlers to bridge SDK events <-> CLI ---
    private static string _lastPrompt = "Input required";

    internal static void OnEvent(Dictionary<string, object?> evt) {
        LogEvent(evt);
        if (!evt.TryGetValue("event", out object? typObj)) {
            return;
        }

        string? typ = typObj?.ToString();
        System.ConsoleColor prev = System.Console.ForegroundColor;

        switch (typ) {
            case "print":
                string msg = evt.TryGetValue("message", out object? m) ? m?.ToString() ?? string.Empty : string.Empty;
                string colorName = evt.TryGetValue("color", out object? c) ? c?.ToString() ?? string.Empty : string.Empty;
                bool newline = true;
                try {
                    if (evt.TryGetValue("newline", out object? nl) && nl is not null) {
                        newline = System.Convert.ToBoolean(nl);
                    }
                } catch { newline = true; }
                prev = System.Console.ForegroundColor;
                try {
                    System.Console.ForegroundColor = MapColor(colorName);
                    if (newline) {
                        System.Console.WriteLine(msg);
                    } else {
                        System.Console.Write(msg);
                    }
                } finally { System.Console.ForegroundColor = prev; }
                break;

            case "prompt":
                _lastPrompt = evt.TryGetValue("message", out object? mm) ? mm?.ToString() ?? "Input required" : "Input required";
                prev = System.Console.ForegroundColor;
                System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                System.Console.WriteLine($"? {_lastPrompt}");
                System.Console.ForegroundColor = prev;
                break;

            case "warning":
                WriteColored($"⚠ {evt.GetValueOrDefault("message", "")}", System.ConsoleColor.Yellow);
                break;

            case "error":
                WriteColored($"✖ {evt.GetValueOrDefault("message", "")}", System.ConsoleColor.Red);
                break;

            case "progress_panel_start":
                int reserve = 12; // default
                if (evt.TryGetValue("reserve", out object? r) && r is System.IConvertible rc) {
                    try {
                        reserve = rc.ToInt32(null);
                    } catch {
#if DEBUG
// todo add trace writeline
#endif
/* ignore */
}
                }
                HandleProgressPanelStart(reserve);
                break;

            case "progress_panel":
                // We have received a progress panel update
                // Re-render it in the TUI
                HandleProgressPanel(evt);
                break;

            case "progress_panel_end":
                // The panel is finished, release the console
                HandleProgressPanelEnd();
                break;

            case "script_active_start":
            case "script_progress":
            case "script_active_end":
                // Placeholder: Script stage progress is a GUI-only indicator for now.
                // Not implemented in the TUI until it supports richer UI composition.
                // Intentionally do nothing here.
                break;

            default:
                // Unknown event type, throw error, all events must be known
                WriteColored($"✖ Unknown event type: {typ}", System.ConsoleColor.Red);
                Trace.TraceError($"Unknown event type received in Terminal Utils: {typ}");
                break;
        }
    }


    private static void LogEvent(IReadOnlyDictionary<string, object?> evt) {
        try {
            Dictionary<string, object?> safe = CloneForLogging(evt);
            string json = JsonSerializer.Serialize(safe, s_jsonOpts);
            System.Diagnostics.Trace.WriteLine($"[TerminalEvent] {json}");
        } catch (System.Exception ex) {
            try {
                System.Diagnostics.Trace.WriteLine($"[TerminalEvent] <serialization failed: {ex.Message}>");
            } catch {
                // ignore logging failures entirely
            }
        }
    }

    private static Dictionary<string, object?> CloneForLogging(IReadOnlyDictionary<string, object?> evt) {
        Dictionary<string, object?> clone = new Dictionary<string, object?>(evt.Count, System.StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> kv in evt) {
            clone[kv.Key] = CloneValue(kv.Value);
        }

        try {
            JsonSerializer.Serialize(clone, s_jsonOpts);
            return clone;
        } catch {
            Dictionary<string, object?> safe = new Dictionary<string, object?>(clone.Count, System.StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> kv in clone) {
                safe[kv.Key] = SafeStringify(kv.Value);
            }
            return safe;
        }
    }

    private static object? CloneValue(object? value) {
        if (value is null) {
            return null;
        }

        switch (value) {
            case string:
            case char:
            case bool:
            case byte:
            case sbyte:
            case short:
            case ushort:
            case int:
            case uint:
            case long:
            case ulong:
            case float:
            case double:
            case decimal:
            case System.Guid:
            case System.DateTime:
            case System.DateTimeOffset:
            case System.TimeSpan:
                return value;
        }

        if (value is JsonElement jsonElement) {
            return jsonElement.Clone();
        }

        if (value is IReadOnlyDictionary<string, object?> roDict) {
            Dictionary<string, object?> nested = new Dictionary<string, object?>(System.StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> kv in roDict) {
                nested[kv.Key] = CloneValue(kv.Value);
            }
            return nested;
        }

        if (value is IDictionary dict) {
            Dictionary<string, object?> nested = new Dictionary<string, object?>(System.StringComparer.Ordinal);
            foreach (DictionaryEntry entry in dict) {
                string key = entry.Key?.ToString() ?? string.Empty;
                nested[key] = CloneValue(entry.Value);
            }
            return nested;
        }

        if (value is IEnumerable enumerable && value is not string) {
            List<object?> list = new List<object?>();
            foreach (object? item in enumerable) {
                list.Add(CloneValue(item));
            }
            return list;
        }

        try {
            JsonSerializer.Serialize(value, s_jsonOpts);
            return value;
        } catch {
            return value.ToString();
        }
    }

    private static object? SafeStringify(object? value) {
        if (value is null) {
            return null;
        }

        if (value is IReadOnlyDictionary<string, object?> roDict) {
            Dictionary<string, object?> nested = new Dictionary<string, object?>(System.StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> kv in roDict) {
                nested[kv.Key] = SafeStringify(kv.Value);
            }
            return nested;
        }

        if (value is IEnumerable enumerable && value is not string) {
            List<object?> list = new List<object?>();
            foreach (object? item in enumerable) {
                list.Add(SafeStringify(item));
            }
            return list;
        }

        return value.ToString();
    }
    
    internal static void HandleProgressPanelStart(int reserve) {
        _progressPanelTop = 0;
        _progressLastLines = 0;
        try {
            lock (s_consoleLock) {
                // We can't clear, as "Run All" output is already printing.
                // We must reserve rows.
                _progressPanelTop = System.Console.CursorTop;
                for (int i = 0; i < reserve; i++) {
                    System.Console.WriteLine();
                }

                try {
                    System.Console.SetCursorPosition(0, _progressPanelTop);
                } catch {
#if DEBUG
// todo add trace writeline
#endif
/* ignore */
}
                _progressLastLines = reserve;
            }
        } catch {
            // As a last resort, keep defaults
        }
    }

    internal static void HandleProgressPanelEnd() {
        // Move cursor to the end of the panel area
        try {
            lock (s_consoleLock) {
                System.Console.SetCursorPosition(0, _progressPanelTop + _progressLastLines);
                System.Console.WriteLine(); // Final newline to move past the panel
            }
        } catch {
            System.Console.WriteLine(); // Fallback
        }
        _progressLastLines = 0;
        _progressPanelTop = 0;
    }

    internal static void HandleProgressPanel(IReadOnlyDictionary<string, object?> payload) {
        // This is the core rendering logic, adapted for the TUI
        // It reads from the event payload and builds/draws the lines

        List<string> lines = BuildTuiProgressLines(payload);
        DrawTuiProgressPanel(lines, ref _progressPanelTop, ref _progressLastLines);
    }

    private static List<string> BuildTuiProgressLines(IReadOnlyDictionary<string, object?> payload) {
        var lines = new List<string>(10);

        // Extract data from payload
        string label = (payload.TryGetValue("label", out object? l) ? l?.ToString() : "Processing") ?? "Processing";
        string spinner = (payload.TryGetValue("spinner", out object? s) ? s?.ToString() : " ") ?? " ";
        int activeTotal = (payload.TryGetValue("active_total", out object? at) ? (at as System.IConvertible)?.ToInt32(null) : 0) ?? 0;

        var stats = payload.TryGetValue("stats", out object? st) ? st as IReadOnlyDictionary<string, object?> : null;
        var activeJobs = payload.TryGetValue("active_jobs", out object? aj) ? aj as IEnumerable<object> : null;

        // 1. Build Progress Bar Line
        if (stats != null) {
            int total = (stats.TryGetValue("total", out object? t) ? (t as System.IConvertible)?.ToInt32(null) : 0) ?? 0;
            int processed = (stats.TryGetValue("processed", out object? p) ? (p as System.IConvertible)?.ToInt32(null) : 0) ?? 0;
            int ok = (stats.TryGetValue("ok", out object? o) ? (o as System.IConvertible)?.ToInt32(null) : 0) ?? 0;
            int skip = (stats.TryGetValue("skip", out object? sk) ? (sk as System.IConvertible)?.ToInt32(null) : 0) ?? 0;
            int err = (stats.TryGetValue("err", out object? e) ? (e as System.IConvertible)?.ToInt32(null) : 0) ?? 0;
            double percent = (stats.TryGetValue("percent", out object? pct) ? (pct as System.IConvertible)?.ToDouble(null) : 0.0) ?? 0.0;

            int width = 30;
            try {
                width = System.Math.Max(10, System.Math.Min(40, System.Console.WindowWidth - 60));
            } catch {
#if DEBUG
// todo add trace writeline
#endif
/* ignore */
}
            int filled = (int)System.Math.Round(percent * width);
            System.Text.StringBuilder bar = new System.Text.StringBuilder(width + 48);
            bar.Append(label);
            bar.Append(' ');
            bar.Append('[');
            for (int i = 0; i < width; i++) {
                bar.Append(i < filled ? '#' : '-');
            }
            bar.Append(']');
            bar.Append(' ');
            bar.Append((int)System.Math.Round(percent * 100));
            bar.Append('%');
            bar.Append(' ');
            bar.Append(processed);
            bar.Append('/');
            bar.Append(total);
            bar.Append(" (ok=");
            bar.Append(ok);
            bar.Append(", skip=");
            bar.Append(skip);
            bar.Append(", err=");
            bar.Append(err);
            bar.Append(')');
            lines.Add(bar.ToString());
        } else {
            lines.Add(label); // Fallback
        }

        // 2. Build Active Jobs Lines
        if (activeTotal == 0) {
            lines.Add("Active: none");
        } else {
            lines.Add($"Active: {activeTotal}");
            if (activeJobs != null) {
                foreach (object? jobObj in activeJobs) {
                    if (jobObj is not IReadOnlyDictionary<string, object?> job)
                        continue;

                    string tool = (job.TryGetValue("tool", out object? t) ? t?.ToString() : "...") ?? "...";
                    string file = (job.TryGetValue("file", out object? f) ? f?.ToString() : "...") ?? "...";
                    string elapsed = (job.TryGetValue("elapsed", out object? e) ? e?.ToString() : "...") ?? "...";

                    int maxFile = 50;
                    try {
                        maxFile = System.Math.Max(18, System.Console.WindowWidth - 20);
                    } catch {
#if DEBUG
// todo add trace writeline
#endif
/* ignore */
}
                    if (file.Length > maxFile) {
                        file = file.Substring(0, maxFile - 3) + "...";
                    }
                    lines.Add($"  {spinner} {tool} · {file} · {elapsed}");
                }
            }
            if (activeTotal > 8) { // 8 is the hardcoded Take(max) in the SDK
                lines.Add($"  … and {activeTotal - System.Math.Min(activeTotal, 8)} more");
            }
        }
        return lines;
    }

    private static void DrawTuiProgressPanel(IReadOnlyList<string> lines, ref int panelTop, ref int lastLines) {
        lock (s_consoleLock) {
            try {
                System.Console.SetCursorPosition(0, panelTop);
                int width;
                try {
                    width = System.Math.Max(20, System.Console.WindowWidth - 1);
                } catch { width = 120; }

                // Draw new lines
                for (int i = 0; i < lines.Count; i++) {
                    string line = lines[i];
                    if (line.Length > width) {
                        line = line.Substring(0, width);
                    }
                    System.Console.Write(line.PadRight(width));
                    if (i < lines.Count - 1) {
                        System.Console.Write('\n');
                    }
                }

                // Clear old lines
                for (int i = lines.Count; i < lastLines; i++) {
                    System.Console.Write('\n');
                    System.Console.Write(new string(' ', System.Math.Max(20, System.Console.WindowWidth - 1)));
                }

                lastLines = lines.Count;
                System.Console.SetCursorPosition(0, panelTop + lastLines);
            } catch {
                try {
                    // Fallback for non-interactive console
                    System.Console.Write("\r" + (lines.Count > 0 ? lines[0] : string.Empty));
                } catch {
                    // ignore
                }
            }
        }
    }
}
