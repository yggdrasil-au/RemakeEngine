
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace EngineNet.Core.Utils;

/// <summary>
/// Lightweight SDK to communicate with the Remake Engine runner via stdout JSON events.
/// Mirrors the behavior of Engine/Utils/Engine_sdk.py for C# tools and scripts.
/// </summary>
public static class EngineSdk {
    /* :: :: Vars :: START :: */
    //public const string Prefix = "@@REMAKE@@ ";

    // Optional in-process event sink. When set, Emit will invoke this delegate with the event payload. If <see cref="MuteStdoutWhenLocalSink"/>
    // is true, stdout emission is suppressed to avoid double-printing.
    public static System.Action<Dictionary<string, object?>>? LocalEventSink { get; set; }
    public static bool MuteStdoutWhenLocalSink { get; set; } = true;

    // Auto-responses for prompts by ID. When a prompt with matching ID is requested, the corresponding response is returned automatically without user interaction.
    public static Dictionary<string, string> AutoPromptResponses { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new() {
        WriteIndented = false,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /* :: :: Vars :: END :: */
    //
    /* :: :: Methods :: START :: */

    /// <summary>
    /// dont use directly.
    /// Emit a structured event line to stdout and flush immediately.

    /// </summary>
    private static void Emit(string @event, IDictionary<string, object?>? data = null) {
        Dictionary<string, object?> payload = new Dictionary<string, object?>(System.StringComparer.Ordinal) {
            ["event"] = @event
        };
        if (data != null) {
            foreach (KeyValuePair<string, object?> kv in data) {
                payload[kv.Key] = kv.Value;
            }
        }

        // Notify in-process sink first (if any)
        if (LocalEventSink != null) {
            try {
                // Pass a shallow copy to avoid accidental modifications by receivers
                LocalEventSink(new Dictionary<string, object?>(payload, System.StringComparer.Ordinal));
            } catch { /* ignore sink errors */ }
            if (MuteStdoutWhenLocalSink) {
                return;
            }
        }

        string json;
        try {
            json = System.Text.Json.JsonSerializer.Serialize(payload, JsonOpts);
        } catch {
            // As a last resort, stringify values to avoid serialization failures
            Dictionary<string, object?> safe = new Dictionary<string, object?>(System.StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> kv in payload) {
                safe[kv.Key] = kv.Value?.ToString();
            }

            json = System.Text.Json.JsonSerializer.Serialize(safe, JsonOpts);
        }

        try {
            //System.Console.Out.Write(Prefix);
            System.Console.Out.WriteLine(json.Replace('\n', ' '));
            System.Console.Out.Flush();
        } catch {
            // Swallow IO errors; there is no recovery if stdout is closed
        }
    }

    /// <summary>
    /// Report a non-fatal warning to the engine UI/log.
    /// </summary>
    public static void Warn(string message) => Emit("warning", new Dictionary<string, object?> { ["message"] = message });

    /// <summary>
    /// Report an error to the engine UI/log (does not exit the process).
    /// </summary>
    public static void Error(string message) => Emit("error", new Dictionary<string, object?> { ["message"] = message });

    /// <summary>
    /// Informational message (non-warning).
    /// </summary>
    public static void Info(string message) => PrintLine(message, color: "cyan");

    /// <summary>
    /// Success message (green).
    /// </summary>
    public static void Success(string message) => PrintLine(message, color: "green");

    /// <summary>
    /// Mark the start of an operation or phase.
    /// </summary>
    public static void Start(string? op = null) => Emit("start", op is null ? null : new Dictionary<string, object?> { ["op"] = op });

    /// <summary>
    /// Mark the end of an operation or phase.
    /// </summary>
    public static void End(bool success = true, int exitCode = 0) => Emit(
        "end",
        new Dictionary<string, object?> { ["success"] = success, ["exit_code"] = exitCode }
    );

    public static string color_prompt(string message, string color, string id = "q1", bool secret = false) {
        // Check for auto-response first
        if (AutoPromptResponses.TryGetValue(id, out string? autoResponse)) {
            Emit("print", new Dictionary<string, object?> {
                ["message"] = $"? {message}",
                ["color"] = color
            });
            Emit("print", new Dictionary<string, object?> {
                ["message"] = $"> {autoResponse} (auto-response)",
                ["color"] = "yellow"
            });
            return autoResponse;
        }

        Emit("color_prompt", new Dictionary<string, object?> { ["id"] = id, ["message"] = message, ["color"] = color, ["secret"] = secret });
        try {
            string? line = System.Console.In.ReadLine();
            return (line ?? string.Empty).TrimEnd('\n');
        } catch {
            return string.Empty;
        }
    }

    /// <summary>
    /// Prompt the user for input. Emits a prompt event, then blocks to read a single line from stdin.
    /// Returns the answer without the trailing newline. May return an empty string.
    /// If an auto-response is available for the prompt ID, returns that instead of prompting.
    /// </summary>
    public static string Prompt(string message, string id = "q1", bool secret = false) {
        // Check for auto-response first
        if (AutoPromptResponses.TryGetValue(id, out string? autoResponse)) {
            Emit("print", new Dictionary<string, object?> {
                ["message"] = $"? {message}",
                ["color"] = "cyan"
            });
            Emit("print", new Dictionary<string, object?> {
                ["message"] = $"> {autoResponse} (auto-response)",
                ["color"] = "yellow"
            });
            return autoResponse;
        }

        Emit("prompt", new Dictionary<string, object?> { ["id"] = id, ["message"] = message, ["secret"] = secret });
        try {
            string? line = System.Console.In.ReadLine();
            return (line ?? string.Empty).TrimEnd('\n');
        } catch {
            return string.Empty;
        }
    }

    /// <summary>
    /// Prompt the user for a boolean confirmation (Yes/No).
    /// Emits a confirm event, then blocks to read a single line from stdin.
    /// Returns true if the user answers 'y', 'yes', or 'true' (case-insensitive).
    /// </summary>
    public static bool Confirm(string message, string id = "q1", bool defaultValue = false) {
        // Check for auto-response first
        if (AutoPromptResponses.TryGetValue(id, out string? autoResponse)) {
            Emit("print", new Dictionary<string, object?> {
                ["message"] = $"? {message} [y/n]",
                ["color"] = "cyan"
            });
            Emit("print", new Dictionary<string, object?> {
                ["message"] = $"> {autoResponse} (auto-response)",
                ["color"] = "yellow"
            });
            return autoResponse.Trim().StartsWith("y", System.StringComparison.OrdinalIgnoreCase) || 
                   autoResponse.Trim().Equals("true", System.StringComparison.OrdinalIgnoreCase);
        }

        Emit("confirm", new Dictionary<string, object?> { ["id"] = id, ["message"] = message, ["default"] = defaultValue });
        try {
            string? line = System.Console.In.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) return defaultValue;
            return line.Trim().StartsWith("y", System.StringComparison.OrdinalIgnoreCase) || 
                   line.Trim().Equals("true", System.StringComparison.OrdinalIgnoreCase) ||
                   line.Trim().Equals("yes", System.StringComparison.OrdinalIgnoreCase);
        } catch {
            return defaultValue;
        }
    }

    // --- Terminal printing helpers ---
    /// <summary>
    /// Emit a colored print event. Color names are case-insensitive and map to typical console colors:
    /// default, gray, darkgray, red, darkred, green, darkgreen, yellow, darkyellow, blue, darkblue, magenta, darkmagenta, cyan, darkcyan, white.
    /// </summary>
    public static void Print(string message, string? color = null, bool newline = false) {
        Dictionary<string, object?> data = new Dictionary<string, object?> {
            ["message"] = message,
            ["color"] = string.IsNullOrWhiteSpace(color) ? null : color,
            ["newline"] = newline
        };
        Emit("print", data);
    }

    /// <summary>
    /// Emit a colored print event using a ConsoleColor.
    /// </summary>
    public static void Print(string message, System.ConsoleColor color, bool newline = false) {
        Print(message, color.ToString(), newline);
    }

    /// <summary>
    /// Emit a colored print event with a trailing newline.
    /// </summary>
    public static void PrintLine(string message, string? color = null) {
        Dictionary<string, object?> data = new Dictionary<string, object?> {
            ["message"] = message,
            ["color"] = string.IsNullOrWhiteSpace(color) ? null : color,
            ["newline"] = true
        };
        Emit("print", data);
    }

    /// <summary>
    /// Emit a colored print event using a ConsoleColor with a trailing newline.
    /// </summary>
    public static void PrintLine(string message, System.ConsoleColor color) {
        Print(message, color.ToString(), true);
    }

    /* :: :: Methods :: END :: */
    //
    /* :: :: inner Classes :: :: */

    /* :: :: Script Progress :: START :: */
    /// <summary>
    /// Lightweight stage-based script progress emitter.
    /// Emits "script_progress" events with current/total/label for GUI consumption.
    /// TUI intentionally treats these as no-ops (placeholder for future enhancement).
    /// </summary>
    public sealed class ScriptProgress {
        private int _processed;
        private int _total;
        private readonly string _label;

        public int Total => _total;
        public int Current => System.Threading.Volatile.Read(ref _processed);
        public string Id { get; }
        public string? Label => _label;

        public ScriptProgress(int total, string id = "s1", string? label = null) {
            _total = System.Math.Max(1, total);
            Id = id;
            _label = label ?? string.Empty;
            _processed = 0;
            EmitProgress();
        }

        public void Update(int inc = 1) {
            int add = System.Math.Max(1, inc);
            int newVal = System.Threading.Interlocked.Add(ref _processed, add);
            if (newVal > _total) {
                System.Threading.Interlocked.Exchange(ref _processed, _total);
            }
            EmitProgress();
        }

        public void Complete() {
            try {
                System.Threading.Interlocked.Exchange(ref _processed, _total);
                EmitProgress();
            } catch {
                                Core.Diagnostics.Bug("Failed to unregister active process for media conversion");
                /* ignore */
            }
        }

        private void EmitProgress() {
            Dictionary<string, object?> data = new Dictionary<string, object?> {
                ["id"] = Id,
                ["current"] = System.Threading.Volatile.Read(ref _processed),
                ["total"] = _total,
                ["label"] = _label
            };
            Emit("script_progress", data);
        }
    }

    /// <summary>
    /// Signal the start of a script run to consumers (e.g., GUI bottom panel).
    /// </summary>
    public static void ScriptActiveStart(string scriptPath) {
        string name = string.Empty;
        try {
            name = System.IO.Path.GetFileName(scriptPath);
        } catch {
            Core.Diagnostics.Bug("EngineSdk.ScriptActiveStart: failed to get file name from path.");
        }
        Emit("script_active_start", new Dictionary<string, object?> {
            ["name"] = string.IsNullOrEmpty(name) ? scriptPath : name,
            ["path"] = scriptPath
        });
    }

    /// <summary>
    /// Signal the end of a script run to consumers.
    /// Always executes by lua script action when the script ends.
    /// Triggers the GUI to jump to 100% and close the panel.
    /// </summary>
    public static void ScriptActiveEnd(bool success = true, int exitCode = 0) {
        Emit("script_active_end", new Dictionary<string, object?> {
            ["success"] = success,
            ["exit_code"] = exitCode
        });
    }
    /* :: :: Script Progress :: END :: */

    /// <summary>
    /// progress handle now backed by SdkConsoleProgress panel events.
    /// Provides the same Update(int) API expected by scripts while rendering
    /// using the improved progress panel in the UI.
    /// </summary>
    public sealed class PanelProgress : System.IDisposable {
        private readonly System.Threading.CancellationTokenSource _cts;
        private readonly System.Threading.Tasks.Task _panelTask;
        private long _processed;
        private long _total;
        private readonly string _label;

        public long Total => _total;
        public long Current => System.Threading.Volatile.Read(ref _processed);
        public string Id { get; }
        public string? Label => _label;

        public PanelProgress(long total, string id = "p1", string? label = null) {
            _total = System.Math.Max(1, total);
            Id = id;
            _label = label ?? string.Empty;
            _processed = 0;
            _cts = new System.Threading.CancellationTokenSource();
            _panelTask = SdkConsoleProgress.StartPanel(
                total: _total,
                snapshot: () => {
                    long p = System.Threading.Volatile.Read(ref _processed);
                    // Use 'ok' equal to processed (clamped to int) for a simple linear flow.
                    int ok = p > int.MaxValue ? int.MaxValue : (int)p;
                    return (processed: p, ok: ok, skip: 0, err: 0);
                },
                activeSnapshot: () => new List<SdkConsoleProgress.ActiveProcess>(),
                label: _label,
                token: _cts.Token
            );
        }

        public void Update(long inc = 1) {
            long add = System.Math.Max(1, inc);
            long newVal = System.Threading.Interlocked.Add(ref _processed, add);
            if (newVal > _total) {
                System.Threading.Interlocked.Exchange(ref _processed, _total);
            }
        }

        public void Complete() {
            try {
                if (!_cts.IsCancellationRequested) {
                    _cts.Cancel();
                }
                try { _panelTask.Wait(1000); } catch {
                                        Core.Diagnostics.Bug("Failed to wait for panel task completion");
                    /* ignore */
                }
            } catch {
                                Core.Diagnostics.Bug("Failed to cancel panel task");
                /* ignore */
            }
        }

        public void Dispose() {
            Complete();
            _cts.Dispose();
        }
    }

    /// <summary>
    /// Simple, reusable console progress panel with an optional list of active jobs.
    /// Emits structured "progress_panel" events that a listener (like the TUI) can use to render the panel.
    /// </summary>
    public static class SdkConsoleProgress {
        /// <summary>
        /// Represents a single active process for the progress panel.
        /// </summary>
        public sealed class ActiveProcess {
            public string Tool { get; set; } = string.Empty;   // e.g., ffmpeg, vgmstream, txd
            public string File { get; set; } = string.Empty;   // file name only
            public System.DateTime StartedUtc { get; set; } = System.DateTime.UtcNow;
        }

        /// <summary>
        /// Starts a background task that periodically emits progress panel events
        /// until the token is cancelled.
        /// </summary>
        public static System.Threading.Tasks.Task StartPanel(
            long total,
            System.Func<(long processed, int ok, int skip, int err)> snapshot,
            System.Func<List<ActiveProcess>> activeSnapshot,
            string label,
            System.Threading.CancellationToken token) {
            return System.Threading.Tasks.Task.Run(() => {
                // Signal the TUI to prepare for the panel
                EmitPanelStart();

                int spinnerIndex = 0;
                char[] spinner = new[] { '|', '/', '-', '\\' };
                while (!token.IsCancellationRequested) {
                    (long processed, int ok, int skip, int err) s = snapshot();
                    List<ActiveProcess> actives = activeSnapshot();

                    // Build the data payload
                    Dictionary<string, object?> data = BuildPanelData(total, s, actives, spinner[spinnerIndex % spinner.Length], label);

                    // Emit the event
                    Core.Utils.EngineSdk.Emit("progress_panel", data);

                    spinnerIndex = (spinnerIndex + 1) & 0x7fffffff;
                    System.Threading.Thread.Sleep(200);
                }

                // Final event emit
                (long processed, int ok, int skip, int err) finalS = snapshot();
                List<ActiveProcess> finalAct = activeSnapshot();
                Dictionary<string, object?> finalData = BuildPanelData(total, finalS, finalAct, ' ', label);
                Core.Utils.EngineSdk.Emit("progress_panel", finalData);

                // Signal the TUI that the panel is done
                EmitPanelEnd();
            });
        }

        private static void EmitPanelStart() {
            int procs = 8;
            try { procs = System.Math.Max(1, System.Math.Min(16, System.Environment.ProcessorCount)); } catch {
                                Core.Diagnostics.Bug("Failed to enumerate PATH directories");
                /* ignore */
            }
            // 1 (progress) + 1 (header/none) + procs (active job lines) + 1 (overflow)
            int reserve = 1 + 1 + procs + 1;
            Core.Utils.EngineSdk.Emit("progress_panel_start", new Dictionary<string, object?> { ["reserve"] = reserve });
        }

        private static void EmitPanelEnd() {
            Core.Utils.EngineSdk.Emit("progress_panel_end");
        }

        private static Dictionary<string, object?> BuildPanelData(long total, (long processed, int ok, int skip, int err) s, List<ActiveProcess> actives, char spinner, string label) {
            if (total < 0) total = 0;

            double percent = System.Math.Clamp(total == 0 ? 1.0 : (double)s.processed / System.Math.Max(1, total), 0.0, 1.0);

            var stats = new Dictionary<string, object?> {
                ["total"] = total,
                ["processed"] = s.processed,
                ["ok"] = s.ok,
                ["skip"] = s.skip,
                ["err"] = s.err,
                ["percent"] = percent
            };

            var jobList = new List<Dictionary<string, object?>>();
            if (actives.Count > 0) {
                int max = 8;
                try {
                    max = System.Math.Max(1, System.Math.Min(16, System.Environment.ProcessorCount));
                } catch {
                                        Core.Diagnostics.Bug("Failed to enumerate PATH directories");
                    /* ignore */
                }
                System.DateTime now = System.DateTime.UtcNow;

                foreach (ActiveProcess job in actives.OrderBy(j => j.StartedUtc).Take(max)) {
                    System.TimeSpan elapsed = now - job.StartedUtc;
                    string elStr = elapsed.TotalHours >= 1
                        ? $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
                        : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";

                    jobList.Add(new Dictionary<string, object?> {
                        ["tool"] = job.Tool,
                        ["file"] = job.File,
                        ["elapsed"] = elStr
                    });
                }
            }

            return new Dictionary<string, object?> {
                ["label"] = label,
                ["spinner"] = spinner.ToString(),
                ["stats"] = stats,
                ["active_jobs"] = jobList,
                ["active_total"] = actives.Count
            };
        }
    }

}
