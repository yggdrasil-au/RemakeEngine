namespace EngineNet.Interface.Terminal;

using EngineNet.Shared.IO.UI;
using Interface;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Utility methods for CLI/TUI handling, can also be used by GUI if needed
/// </summary>
public sealed class Utils {
    private static readonly JsonSerializerOptions s_jsonOpts = new() {
        WriteIndented = false,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    private static readonly Lock s_consoleLock = new();

    private static int s_activePanels;
    private static readonly Dictionary<string, List<string>> s_panelStatus = new();
    private static bool s_rendererInitializedByEvent;

    private static void ResetTaskProgressState() {
        s_activePanels = 0;
        s_panelStatus.Clear();
        s_rendererInitializedByEvent = false;
    }

    /// <summary>
    /// Enables the aggregate operation progress line for a run-all sequence.
    /// </summary>
    public static void BeginOperationProgress() {
        lock (s_consoleLock) {
            TuiRenderer.UpdateScriptProgress(current: 0, total: 1, label: "Starting...");
            UpdateTuiStatus();
        }
    }

    /// <summary>
    /// Removes the aggregate operation progress line after a run-all sequence finishes.
    /// </summary>
    public static void EndOperationProgress() {
        lock (s_consoleLock) {
            TuiRenderer.ClearScriptProgress();
            UpdateTuiStatus();
        }
    }

    /// <summary>
    /// Execute a single operation in the terminal interface, handling events and output appropriately.
    /// </summary>
    internal async System.Threading.Tasks.Task<bool> ExecuteOpAsync(
        MiniEngineFace Engine,
        string game,
        Core.Data.GameModules games,
        Dictionary<string, object?> op,
        Core.Data.PromptAnswers promptAnswers,
        Dictionary<string, string>? autoPromptResponses = null,
        System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken),
        Core.Abstractions.IProcessRunner.OutputHandler? onOutput = null,
        Core.Abstractions.IProcessRunner.EventHandler? onEvent = null,
        Core.Abstractions.IProcessRunner.StdinProvider? stdinProvider = null
    ) {
        try {
            Core.Abstractions.IProcessRunner.OutputHandler outputHandler = onOutput ?? OnOutput;
            Core.Abstractions.IProcessRunner.EventHandler eventHandler = onEvent ?? OnEvent;
            Core.Abstractions.IProcessRunner.StdinProvider inputProvider = stdinProvider ?? StdinProvider;
            string? script_type = (op.TryGetValue(key: "script_type", out object? st) ? st?.ToString() : null)
                ?.ToLowerInvariant();

            // Use embedded handlers for engine/lua/js/bms to avoid external dependencies
            if (Core.Utils.ScriptConstants.IsSupported(script_type: script_type)) {
                // Route in-process SDK events to our terminal renderer
                System.Action<Dictionary<string, object?>>? prevSink = Shared.IO.UI.EngineSdk.LocalEventSink;
                bool prevMute = Shared.IO.UI.EngineSdk.MuteStdoutWhenLocalSink;
                Dictionary<string, string> prevAutoResponses = new(dictionary: Shared.IO.UI.EngineSdk.AutoPromptResponses);
                try {
                    // Set auto-prompt responses if provided
                    if (autoPromptResponses is { Count: > 0 }) {
                        Shared.IO.UI.EngineSdk.AutoPromptResponses.Clear();
                        foreach (KeyValuePair<string, string> kv in autoPromptResponses) {
                            Shared.IO.UI.EngineSdk.AutoPromptResponses[key: kv.Key] = kv.Value;
                        }
                    }

                    Shared.IO.UI.EngineSdk.LocalEventSink = evt => eventHandler(evt: evt);
                    Shared.IO.UI.EngineSdk.MuteStdoutWhenLocalSink = true;
                    return await Engine.RunSingleOperationAsync(
                        currentGame: game,
                        games: games,
                        op: op,
                        promptAnswers: promptAnswers,
                        cancellationToken: cancellationToken
                    );
                }
                finally {
                    // Restore previous auto-prompt responses
                    Shared.IO.UI.EngineSdk.AutoPromptResponses.Clear();
                    foreach (KeyValuePair<string, string> kv in prevAutoResponses) {
                        Shared.IO.UI.EngineSdk.AutoPromptResponses[key: kv.Key] = kv.Value;
                    }

                    Shared.IO.UI.EngineSdk.LocalEventSink = prevSink;
                    Shared.IO.UI.EngineSdk.MuteStdoutWhenLocalSink = prevMute;
                }
            }

            Shared.IO.Diagnostics.Log($"Routing operation of type '{script_type}' to external command execution");

            // Default: build and execute as external command (e.g., python)
            List<string> parts =
                Engine.CommandService_BuildCommand(currentGame: game, games: games, engineData: Engine.EngineConfig_Data, op: op, promptAnswers: promptAnswers);
            if (parts.Count < 2) {
                return false;
            }

            string title = op.TryGetValue(key: "Name", out object? n)
                ? n?.ToString() ?? System.IO.Path.GetFileName(path: parts[index: 1])
                : System.IO.Path.GetFileName(path: parts[index: 1]);
            return Engine.CommandService_ExecuteCommand(
                commandParts: parts,
                title: title,
                onOutput: outputHandler,
                onEvent: eventHandler,
                stdinProvider: inputProvider,
                envOverrides: new Dictionary<string, object?> { [key: "TERM"] = "dumb" }
            );
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Error executing operation: {ex.Message}");
            return false;
        }
    }

    private static System.ConsoleColor MapColor(string? name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return System.ConsoleColor.Gray;
        }

        switch (name.Trim().ToLowerInvariant()) {
            case "default": return System.ConsoleColor.Gray;
            case "black": return System.ConsoleColor.Black;
            case "darkblue": return System.ConsoleColor.DarkBlue;
            case "blue": return System.ConsoleColor.Blue;
            case "darkgreen": return System.ConsoleColor.DarkGreen;
            case "green": return System.ConsoleColor.Green;
            case "darkcyan": return System.ConsoleColor.DarkCyan;
            case "cyan": return System.ConsoleColor.Cyan;
            case "darkred": return System.ConsoleColor.DarkRed;
            case "red": return System.ConsoleColor.Red;
            case "darkmagenta": return System.ConsoleColor.DarkMagenta;
            case "magenta": return System.ConsoleColor.Magenta;
            case "darkyellow": return System.ConsoleColor.DarkYellow;
            case "yellow": return System.ConsoleColor.Yellow;
            case "gray":
            case "grey": return System.ConsoleColor.Gray;
            case "darkgray":
            case "darkgrey": return System.ConsoleColor.DarkGray;
            case "white": return System.ConsoleColor.White;
            default: return System.ConsoleColor.Gray;
        }
    }

    private static string? StdinProvider() {
        return TuiRenderer.ReadLineCustom(label: "Input >", isSecret: false);
    }

    internal static void OnOutput(string line, string stream) {
        OnEvent(evt: new Dictionary<string, object?> {
            [key: "event"] = EngineSdk.Events.Print,
            [key: "message"] = line,
            [key: "color"] = stream == "stderr" ? "red" : "gray"
        });
    }

    // --- Handlers to bridge SDK events <-> CLI ---

    internal static void OnEvent(Dictionary<string, object?> evt) {
        LogEvent(evt: evt);
        if (!evt.TryGetValue(key: "event", out object? typObj)) return;

        string? typ = typObj?.ToString();

        switch (typ) {
            case EngineSdk.Events.Print: {
                string msg = evt.TryGetValue(key: "message", out object? m) ? m?.ToString() ?? "" : "";
                string colorName = evt.TryGetValue(key: "color", out object? c) ? c?.ToString() ?? "gray" : "gray";
                TuiRenderer.Log(msg, color: MapColor(name: colorName));
                break;
            }

            case EngineSdk.Events.Warning: {
                TuiRenderer.Log($"[WARN] {evt.GetValueOrDefault(key: "message", defaultValue: "")}", color: System.ConsoleColor.Yellow);
                break;
            }

            case EngineSdk.Events.Error: {
                TuiRenderer.Log($"[ERR] {evt.GetValueOrDefault(key: "message", defaultValue: "")}", color: System.ConsoleColor.Red);
                break;
            }

            case EngineSdk.Events.Prompt:
            case EngineSdk.Events.ColorPrompt: {
                string pMsg = evt.TryGetValue(key: "message", out object? pm)
                    ? pm?.ToString() ?? "Input required"
                    : "Input required";
                TuiRenderer.Log($"? {pMsg}", color: System.ConsoleColor.Cyan);
                break;
            }

            case EngineSdk.Events.Confirm: {
                string cMsg = evt.TryGetValue(key: "message", out object? cm) ? cm?.ToString() ?? "Confirm?" : "Confirm?";
                bool def = evt.TryGetValue(key: "default", out object? d) && d is true;
                TuiRenderer.Log($"? {cMsg} [{(def ? "y/N" : "Y/n")}]", color: System.ConsoleColor.Cyan);
                break;
            }

            case EngineSdk.Events.ScriptActiveStart: {
                TuiRenderer.Log($"▶ Starting script: {evt.GetValueOrDefault(key: "name", defaultValue: "Unnamed")}",
                    color: System.ConsoleColor.Green);
                break;
            }

            case EngineSdk.Events.ScriptProgress: {
                int current = GetEventInt(evt: evt, key: "current");
                int total = GetEventInt(evt: evt, key: "total");
                string label = evt.TryGetValue(key: "label", out object? l) ? l?.ToString() ?? string.Empty : string.Empty;
                TuiRenderer.UpdateScriptProgress(current: current, total: total, label: label);
                break;
            }

            case EngineSdk.Events.ScriptActiveEnd: {
                TuiRenderer.ClearScriptProgress();
                bool success = evt.TryGetValue(key: "success", out object? s) && System.Convert.ToBoolean(s);
                TuiRenderer.Log("◀ Finished script.", color: success ? System.ConsoleColor.Green : System.ConsoleColor.Red);
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
                string id = evt.TryGetValue(key: "id", out object? idObj) ? idObj?.ToString() ?? "p1" : "p1";
                List<string> lines = BuildTuiProgressLines(payload: evt);
                lock (s_consoleLock) {
                    s_panelStatus[key: id] = lines;
                    if (EngineNet.Shared.State.IsCli) {
                        if (lines.Count > 0) {
                            try {
                                int w;
                                try {
                                    w = System.Console.WindowWidth;
                                }
                                catch {
                                    Shared.IO.Diagnostics.Bug("Failed to get console window width due to an exception.");
                                    w = 80;
                                }

                                string text = lines[index: 0];
                                if (text.Length > w - 1) text = text.Substring(startIndex: 0, length: w - 1);
                                System.Console.Write($"\r{text.PadRight(totalWidth: w - 1)}");
                            }
                            catch {
                                Shared.IO.Diagnostics.Bug("Failed to write progress panel line to console due to an exception.");
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
                string id = evt.TryGetValue(key: "id", out object? idObj) ? idObj?.ToString() ?? "p1" : "p1";
                lock (s_consoleLock) {
                    if (s_panelStatus.TryGetValue(key: id, out List<string>? lastLines) && lastLines.Count > 0) {
                        // Log the FIRST line (the progress bar) to the log area so it sticks in history
                        if (EngineNet.Shared.State.IsCli) {
                            System.Console.WriteLine(); // Newline to clear the fixed \r line
                        } else {
                            TuiRenderer.Log(lastLines[index: 0], color: System.ConsoleColor.Cyan);
                        }
                    }

                    s_activePanels--;
                    s_panelStatus.Remove(key: id);
                    if (!EngineNet.Shared.State.IsCli) {
                        UpdateTuiStatus();
                    }

                    if (s_activePanels <= 0) {
                        bool shouldShutdownRenderer = !EngineNet.Shared.State.IsCli && s_rendererInitializedByEvent;
                        ResetTaskProgressState();
                        if (shouldShutdownRenderer) {
                            TuiRenderer.Shutdown();
                        } else if (!EngineNet.Shared.State.IsCli) {
                            TuiRenderer.ClearStatus();
                        }
                    }
                }

                break;
            }

            case EngineSdk.Events.RunAllStart: {
                TuiRenderer.UpdateScriptProgress(current: 0, total: GetEventInt(evt: evt, key: "total"), label: "Run-All Sequence");
                TuiRenderer.Log($"Starting run-all sequence ({evt.GetValueOrDefault(key: "total", defaultValue: 0)} operations).",
                    color: System.ConsoleColor.Green);
                break;
            }

            case EngineSdk.Events.RunAllOpStart: {
                TuiRenderer.UpdateScriptProgress(current: GetEventInt(evt: evt, key: "index"), total: GetEventInt(evt: evt, key: "total"),
                    label: evt.GetValueOrDefault(key: "name", defaultValue: "Unnamed")?.ToString() ?? string.Empty);
                TuiRenderer.Log($"Starting operation via run-all: {evt.GetValueOrDefault(key: "name", defaultValue: "Unnamed")}",
                    color: System.ConsoleColor.Green);
                break;
            }

            case EngineSdk.Events.RunAllOpEnd: {
                TuiRenderer.UpdateScriptProgress(current: GetEventInt(evt: evt, key: "index") + 1, total: GetEventInt(evt: evt, key: "total"),
                    label: evt.GetValueOrDefault(key: "name", defaultValue: "Unnamed")?.ToString() ?? string.Empty);
                TuiRenderer.Log($"✔ Operation completed via run-all: {evt.GetValueOrDefault(key: "name", defaultValue: "Unnamed")}",
                    color: System.ConsoleColor.Green);
                break;
            }

            case EngineSdk.Events.RunAllComplete: {
                TuiRenderer.ClearScriptProgress();
                TuiRenderer.Log("Run-all sequence completed.", color: System.ConsoleColor.Green);
                break;
            }

            default: {
                // Log unknown events to debug
                Shared.IO.Diagnostics.Log($"Unhandled event type: {typ}");
                break;
            }
        }
    }

    private static void UpdateTuiStatus() {
        List<string> allLines = new();
        foreach (List<string> panelLines in s_panelStatus.Values) {
            allLines.AddRange(collection: panelLines);
        }

        TuiRenderer.UpdateStatus(lines: allLines);
    }

    private static int GetEventInt(IReadOnlyDictionary<string, object?> evt, string key) {
        if (!evt.TryGetValue(key: key, out object? value) || value is null) {
            return 0;
        }

        try {
            return System.Convert.ToInt32(value);
        }
        catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Failed to parse event field '{key}'.", ex: ex);
            return 0;
        }
    }

    private static void LogEvent(IReadOnlyDictionary<string, object?> evt) {
        try {
            Dictionary<string, object?> safe = CloneForLogging(evt: evt);
            string json = JsonSerializer.Serialize(safe, options: s_jsonOpts);
            Shared.IO.Diagnostics.Trace($"{json}");
        }
        catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"<serialization failed: {ex.Message}>");
        }
    }

    private static Dictionary<string, object?> CloneForLogging(IReadOnlyDictionary<string, object?> evt) {
        Dictionary<string, object?> clone = new(capacity: evt.Count, comparer: System.StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> kv in evt) {
            clone[key: kv.Key] = CloneValue(kv.Value);
        }

        try {
            JsonSerializer.Serialize(clone, options: s_jsonOpts);
            return clone;
        }
        catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"Clone serialization catch triggered: {ex}");
            Dictionary<string, object?> safe =
                new(capacity: clone.Count, comparer: System.StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> kv in clone) {
                safe[key: kv.Key] = SafeStringify(kv.Value);
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
            Dictionary<string, object?> nested = new(comparer: System.StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> kv in roDict) {
                nested[key: kv.Key] = CloneValue(kv.Value);
            }

            return nested;
        }

        if (value is IDictionary dict) {
            Dictionary<string, object?> nested = new(comparer: System.StringComparer.Ordinal);
            foreach (DictionaryEntry entry in dict) {
                string key = entry.Key.ToString() ?? string.Empty;
                nested[key: key] = CloneValue(entry.Value);
            }

            return nested;
        }

        if (value is IEnumerable enumerable and not string) {
            List<object?> list = new();
            foreach (object? item in enumerable) {
                list.Add(item: CloneValue(item));
            }

            return list;
        }

        try {
            JsonSerializer.Serialize(value, options: s_jsonOpts);
            return value;
        } catch {
            Shared.IO.Diagnostics.Bug("Failed to serialize value to JSON due to an exception.");
            return value.ToString();
        }
    }

    private static object? SafeStringify(object? value) {
        switch (value) {
            case null:
                return null;

            case IReadOnlyDictionary<string, object?> roDict: {
                Dictionary<string, object?> nested = new(comparer: System.StringComparer.Ordinal);
                foreach (KeyValuePair<string, object?> kv in roDict) {
                    nested[key: kv.Key] = SafeStringify(kv.Value);
                }

                return nested;
            }

            case IEnumerable enumerable when value is not string: {
                List<object?> list = new();
                foreach (object? item in enumerable) {
                    list.Add(item: SafeStringify(item));
                }

                return list;
            }

            default:
                return value.ToString();
        }
    }

    private static List<string> BuildTuiProgressLines(IReadOnlyDictionary<string, object?> payload) {
        List<string> lines = new(capacity: 10);

        // Extract data from payload
        string label = (payload.TryGetValue(key: "label", out object? l) ? l?.ToString() : "Processing") ?? "Processing";
        string spinner = (payload.TryGetValue(key: "spinner", out object? s) ? s?.ToString() : " ") ?? " ";
        int activeTotal = (payload.TryGetValue(key: "active_total", out object? at)
            ? (at as System.IConvertible)?.ToInt32(provider: null)
            : 0) ?? 0;

        IReadOnlyDictionary<string, object?>? stats = payload.TryGetValue(key: "stats", out object? st) ? st as IReadOnlyDictionary<string, object?> : null;
        IEnumerable<object>? activeJobs = payload.TryGetValue(key: "active_jobs", out object? aj) ? aj as IEnumerable<object> : null;

        // 1. Build Progress Bar Line
        if (stats != null) {
            int total = (stats.TryGetValue(key: "total", out object? t) ? (t as System.IConvertible)?.ToInt32(provider: null) : 0) ?? 0;
            int processed = (stats.TryGetValue(key: "processed", out object? p) ? (p as System.IConvertible)?.ToInt32(provider: null) : 0) ?? 0;
            int ok = (stats.TryGetValue(key: "ok", out object? o) ? (o as System.IConvertible)?.ToInt32(provider: null) : 0) ?? 0;
            int skip = (stats.TryGetValue(key: "skip", out object? sk) ? (sk as System.IConvertible)?.ToInt32(provider: null) : 0) ?? 0;
            int err = (stats.TryGetValue(key: "err", out object? e) ? (e as System.IConvertible)?.ToInt32(provider: null) : 0) ?? 0;
            double percent = (stats.TryGetValue(key: "percent", out object? pct)
                ? (pct as System.IConvertible)?.ToDouble(provider: null)
                : 0.0) ?? 0.0;
            int width;
            try {
                int buf = System.Math.Max(val1: 20, val2: System.Console.BufferWidth);
                // Keep the bar a reasonable fraction of buffer width
                width = System.Math.Clamp(buf - 40, min: 10, max: 60);
            } catch {
                width = 30;
            }

            int filled = (int)System.Math.Round(a: percent * width);
            StringBuilder bar = new(capacity: width + 48);

            // Truncate label to keep line short; Draw method still clamps
            string lbl = label;
            try {
                int maxLabel = System.Math.Max(val1: 8, val2: System.Math.Min(val1: 30, val2: System.Console.BufferWidth - (width + 20)));
                if (lbl.Length > maxLabel) lbl = lbl.Substring(startIndex: 0, length: maxLabel - 3) + "...";
            } catch {
                Shared.IO.Diagnostics.Bug("Failed to calculate max label width due to an exception.");
                /* ignore */
            }

            bar.Append(lbl);
            bar.Append(' ');
            bar.Append('[');
            for (int i = 0; i < width; i++) {
                if (i < filled) bar.Append('█');
                else bar.Append('░');
            }

            bar.Append(']');
            bar.Append(' ');
            bar.Append((int)System.Math.Round(a: percent * 100));
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
            lines.Add(item: bar.ToString());
        } else {
            lines.Add(item: label); // Fallback
        }

        // 2. Build Active Jobs Lines
        if (activeTotal == 0) {
            lines.Add(item: "Active: none");
        } else {
            lines.Add(item: $"Active: {activeTotal}");
            if (activeJobs != null) {
                foreach (object jobObj in activeJobs) {
                    if (jobObj is not IReadOnlyDictionary<string, object?> job) {
                        continue;
                    }
                    string tool = (job.TryGetValue(key: "tool", out object? t) ? t?.ToString() : "...") ?? "...";
                    string file = (job.TryGetValue(key: "file", out object? f) ? f?.ToString() : "...") ?? "...";
                    string elapsed = (job.TryGetValue(key: "elapsed", out object? e) ? e?.ToString() : "...") ?? "...";

                    int maxFile;
                    try {
                        maxFile = System.Math.Max(val1: 18, val2: System.Console.BufferWidth - 20);
                    } catch {
                        maxFile = 50;
                    }

                    if (file.Length > maxFile) {
                        file = file.Substring(startIndex: 0, length: maxFile - 3) + "...";
                    }

                    lines.Add(item: $"  {spinner} {tool} · {file} · {elapsed}");
                }
            }

            if (activeTotal > 8) {
                // 8 is the hardcoded Take(max) in the SDK
                lines.Add(item: $"  … and {activeTotal - System.Math.Min(val1: activeTotal, val2: 8)} more");
            }
        }

        return lines;
    }
}