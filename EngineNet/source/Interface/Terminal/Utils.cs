
using EngineNet.Shared.IO.UI;

namespace EngineNet.Interface.Terminal;


/// <summary>
/// Utility methods for CLI/TUI handling, can also be used by GUI if needed
/// </summary>
public class Utils {

    private static readonly System.Text.Json.JsonSerializerOptions s_jsonOpts = new() {
        WriteIndented = false,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    private static readonly Lock s_consoleLock = new();

    private static int s_activePanels;
    private static readonly Dictionary<string, List<string>> s_panelStatus = new();
    private static bool s_rendererInitializedByEvent;

    private static void ResetProgressState() {
        s_activePanels = 0;
        s_panelStatus.Clear();
        s_rendererInitializedByEvent = false;
    }

    /// <summary>
    /// Execute a single operation in the terminal interface, handling events and output appropriately.
    /// </summary>
    /// <param name="Engine"></param>
    /// <param name="game"></param>
    /// <param name="games"></param>
    /// <param name="op"></param>
    /// <param name="promptAnswers"></param>
    /// <param name="autoPromptResponses"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    internal async System.Threading.Tasks.Task<bool> ExecuteOpAsync(
        MiniEngineFace Engine,
        string game,
        Core.Data.GameModules games,
        Dictionary<string, object?> op,
        Core.Data.PromptAnswers promptAnswers,
        Dictionary<string, string>? autoPromptResponses = null,
        System.Threading.CancellationToken cancellationToken = default(CancellationToken)
    ) {
        try {
            string? script_type = (op.TryGetValue("script_type", out object? st) ? st?.ToString() : null)?.ToLowerInvariant();

            // Use embedded handlers for engine/lua/js/bms to avoid external dependencies
            if (Core.Utils.ScriptConstants.IsSupported(script_type)) {
                // Route in-process SDK events to our terminal renderer
                System.Action<Dictionary<string, object?>>? prevSink = Shared.IO.UI.EngineSdk.LocalEventSink;
                bool prevMute = Shared.IO.UI.EngineSdk.MuteStdoutWhenLocalSink;
                var prevAutoResponses = new Dictionary<string, string>(Shared.IO.UI.EngineSdk.AutoPromptResponses);
                try {
                    // Set auto-prompt responses if provided
                    if (autoPromptResponses is { Count: > 0 }) {
                        Shared.IO.UI.EngineSdk.AutoPromptResponses.Clear();
                        foreach (KeyValuePair<string, string> kv in autoPromptResponses) {
                            Shared.IO.UI.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
                        }
                    }

                    Shared.IO.UI.EngineSdk.LocalEventSink = OnEvent;
                    Shared.IO.UI.EngineSdk.MuteStdoutWhenLocalSink = true;
                    return await Engine.RunSingleOperationAsync(
                        game,
                        games,
                        op,
                        promptAnswers,
                        cancellationToken: cancellationToken
                    );
                } finally {
                    // Restore previous auto-prompt responses
                    Shared.IO.UI.EngineSdk.AutoPromptResponses.Clear();
                    foreach (KeyValuePair<string, string> kv in prevAutoResponses) {
                        Shared.IO.UI.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
                    }

                    Shared.IO.UI.EngineSdk.LocalEventSink = prevSink;
                    Shared.IO.UI.EngineSdk.MuteStdoutWhenLocalSink = prevMute;
                }
            }

            Shared.IO.Diagnostics.Log($"[Utils.cs::ExecuteOp()] Routing operation of type '{script_type}' to external command execution");


            // Default: build and execute as external command (e.g., python)
            List<string> parts = Engine.CommandService_BuildCommand(game, games, Engine.EngineConfig_Data, op, promptAnswers);
            if (parts.Count < 2) {
                return false;
            }

            string title = op.TryGetValue("Name", out object? n) ? n?.ToString() ?? System.IO.Path.GetFileName(parts[1]) : System.IO.Path.GetFileName(parts[1]);
            return Engine.CommandService_ExecuteCommand(
                parts,
                title,
                onOutput: OnOutput,
                onEvent: OnEvent,
                stdinProvider: StdinProvider,
                envOverrides: new Dictionary<string, object?> { ["TERM"] = "dumb" }
            );
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[Utils.cs::ExecuteOp()] Error executing operation: {ex.Message}");
            return false;
        }
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

    private static string StdinProvider() {
        return TuiRenderer.ReadLineCustom("Input >", false);
    }

    public static void OnOutput(string line, string stream) {
        OnEvent(new Dictionary<string, object?> {
            ["event"] = EngineSdk.Events.Print,
            ["message"] = line,
            ["color"] = stream == "stderr" ? "red" : "gray"
        });
    }

    // --- Handlers to bridge SDK events <-> CLI ---

    public static void OnEvent(Dictionary<string, object?> evt) {
        // TuiRenderer handles locking internally
        LogEvent(evt);
        if (!evt.TryGetValue("event", out object? typObj)) return;

        string? typ = typObj?.ToString();

        switch (typ) {
            case EngineSdk.Events.Print:
                string msg = evt.TryGetValue("message", out object? m) ? m?.ToString() ?? "" : "";
                string colorName = evt.TryGetValue("color", out object? c) ? c?.ToString() ?? "gray" : "gray";
                TuiRenderer.Log(msg, MapColor(colorName));
                break;

            case EngineSdk.Events.Warning:
                TuiRenderer.Log($"[WARN] {evt.GetValueOrDefault("message", "")}", ConsoleColor.Yellow);
                break;

            case EngineSdk.Events.Error:
                TuiRenderer.Log($"[ERR] {evt.GetValueOrDefault("message", "")}", ConsoleColor.Red);
                break;

            case EngineSdk.Events.Prompt:
            case EngineSdk.Events.ColorPrompt: {
                string pMsg = evt.TryGetValue("message", out object? pm) ? pm?.ToString() ?? "Input required" : "Input required";
                TuiRenderer.Log($"? {pMsg}", ConsoleColor.Cyan);
                break;
            }

            case EngineSdk.Events.Confirm: {
                string cMsg = evt.TryGetValue("message", out object? cm) ? cm?.ToString() ?? "Confirm?" : "Confirm?";
                bool def = evt.TryGetValue("default", out object? d) && d is true;
                TuiRenderer.Log($"? {cMsg} [{(def ? "y/N" : "Y/n")}]", ConsoleColor.Cyan);
                break;
            }

            case EngineSdk.Events.ProgressPanelStart: {
                lock (s_consoleLock) {
                    if (s_activePanels <= 0) {
                        s_panelStatus.Clear();
                        s_rendererInitializedByEvent = false;
                    }

                    s_activePanels++;
                    if (!EngineNet.Shared.State.IsCli && !TuiRenderer.IsActive) {
                        TuiRenderer.Initialize();
                        s_rendererInitializedByEvent = true;
                    }

                    UpdateTuiStatus();
                }
                break;
            }

            case EngineSdk.Events.ProgressPanel: {
                string id = evt.TryGetValue("id", out object? idObj) ? idObj?.ToString() ?? "p1" : "p1";
                List<string> lines = BuildTuiProgressLines(evt);
                lock (s_consoleLock) {
                    s_panelStatus[id] = lines;
                    if (EngineNet.Shared.State.IsCli) {
                        if (lines.Count > 0) {
                            try {
                                int w;
                                try { w = System.Console.WindowWidth; } catch { w = 80; }
                                string text = lines[0];
                                if (text.Length > w - 1) text = text.Substring(0, w - 1);
                                System.Console.Write($"\r{text.PadRight(w - 1)}");
                            } catch {
                                // Silent catch to prevent crashing if console window access fails
                            }
                        }
                    } else {
                        UpdateTuiStatus();
                    }
                }
                break;
            }

            case EngineSdk.Events.ProgressPanelEnd: {
                string id = evt.TryGetValue("id", out object? idObj) ? idObj?.ToString() ?? "p1" : "p1";
                lock (s_consoleLock) {
                    if (s_panelStatus.TryGetValue(id, out var lastLines) && lastLines.Count > 0) {
                        // Log the FIRST line (the progress bar) to the log area so it sticks in history
                        if (EngineNet.Shared.State.IsCli) {
                            System.Console.WriteLine(); // Newline to clear the fixed \r line
                        } else {
                            TuiRenderer.Log(lastLines[0], ConsoleColor.Cyan);
                        }
                    }

                    s_activePanels--;
                    s_panelStatus.Remove(id);
                    if (!EngineNet.Shared.State.IsCli) {
                        UpdateTuiStatus();
                    }

                    if (s_activePanels <= 0) {
                        bool shouldShutdownRenderer = !EngineNet.Shared.State.IsCli && s_rendererInitializedByEvent;
                        ResetProgressState();
                        if (shouldShutdownRenderer) {
                            TuiRenderer.Shutdown();
                        }
                    }
                }
                break;
            }

            case EngineSdk.Events.RunAllOpEnd:
            case EngineSdk.Events.RunAllComplete:
                TuiRenderer.Log($"✔ Operation completed via run-all: {evt.GetValueOrDefault("name", "Unnamed")}", ConsoleColor.Green);
                break;

            case EngineSdk.Events.RunAllOpStart:
            case EngineSdk.Events.RunAllStart:
                TuiRenderer.Log($"✔ Operation started via run-all: {evt.GetValueOrDefault("name", "Unnamed")}", ConsoleColor.Green);
                break;

            default:
                // Log unknown events to debug
                Shared.IO.Diagnostics.Log($"[Utils.cs::OnEvent()] Unhandled event type: {typ}");
                break;
        }
    }

    private static void UpdateTuiStatus() {
        var allLines = new List<string>();
        foreach (var panelLines in s_panelStatus.Values) {
            allLines.AddRange(panelLines);
        }
        TuiRenderer.UpdateStatus(allLines);
    }

    private static void LogEvent(IReadOnlyDictionary<string, object?> evt) {
        try {
            Dictionary<string, object?> safe = CloneForLogging(evt);
            string json = JsonSerializer.Serialize(safe, s_jsonOpts);
            Shared.IO.Diagnostics.Log($"[Utils.cs::OnEvent()] {json}");
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[Utils.cs::OnEvent()] <serialization failed: {ex.Message}>");
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
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[Utils.cs::CloneForLogging()] Clone serialization catch triggered: {ex}");
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
                string key = entry.Key.ToString() ?? string.Empty;
                nested[key] = CloneValue(entry.Value);
            }
            return nested;
        }

        if (value is IEnumerable enumerable and not string) {
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
        switch (value) {
            case null:
                return null;

            case IReadOnlyDictionary<string, object?> roDict: {
                Dictionary<string, object?> nested = new Dictionary<string, object?>(System.StringComparer.Ordinal);
                foreach (KeyValuePair<string, object?> kv in roDict) {
                    nested[kv.Key] = SafeStringify(kv.Value);
                }
                return nested;
            }

            case IEnumerable enumerable when value is not string: {
                List<object?> list = new List<object?>();
                foreach (object? item in enumerable) {
                    list.Add(SafeStringify(item));
                }
                return list;
            }

            default:
                return value.ToString();
        }
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
            int width;
            try {
                int buf = System.Math.Max(20, System.Console.BufferWidth);
                // Keep the bar a reasonable fraction of buffer width
                width = System.Math.Clamp(buf - 40, 10, 60);
            } catch { width = 30; }
            int filled = (int)System.Math.Round(percent * width);
            var bar = new System.Text.StringBuilder(width + 48);
            // Truncate label to keep line short; Draw method still clamps
            string lbl = label;
            try {
                int maxLabel = System.Math.Max(8, System.Math.Min(30, System.Console.BufferWidth - (width + 20)));
                if (lbl.Length > maxLabel) lbl = lbl.Substring(0, maxLabel - 3) + "...";
            } catch { /* ignore */ }
            bar.Append(lbl);
            bar.Append(' ');
            bar.Append('[');
            for (int i = 0; i < width; i++) {
                if (i < filled - 1) bar.Append('=');
                else if (i == filled - 1) bar.Append('>');
                else bar.Append(' ');
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
                foreach (object jobObj in activeJobs) {
                    if (jobObj is not IReadOnlyDictionary<string, object?> job)
                        continue;

                    string tool = (job.TryGetValue("tool", out object? t) ? t?.ToString() : "...") ?? "...";
                    string file = (job.TryGetValue("file", out object? f) ? f?.ToString() : "...") ?? "...";
                    string elapsed = (job.TryGetValue("elapsed", out object? e) ? e?.ToString() : "...") ?? "...";

                    int maxFile;
                    try { maxFile = System.Math.Max(18, System.Console.BufferWidth - 20); } catch { maxFile = 50; }
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
}
