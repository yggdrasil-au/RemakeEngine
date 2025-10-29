
using System.Collections.Generic;
using System.Linq;

namespace EngineNet.Core.Utils;

/// <summary>
/// Lightweight SDK to communicate with the Remake Engine runner via stdout JSON events.
/// Mirrors the behavior of Engine/Utils/Engine_sdk.py for C# tools and scripts.
/// </summary>
internal static class EngineSdk {
    public const string Prefix = "@@REMAKE@@ ";
    /// <summary>
    /// Optional in-process event sink. When set, Emit will invoke this
    /// delegate with the event payload. If <see cref="MuteStdoutWhenLocalSink"/>
    /// is true, stdout emission is suppressed to avoid double-printing.
    /// </summary>
    public static System.Action<Dictionary<string, object?>>? LocalEventSink {
        get; set;
    }
    public static bool MuteStdoutWhenLocalSink { get; set; } = true;

    /// <summary>
    /// Auto-responses for prompts by ID. When a prompt with matching ID is requested,
    /// the corresponding response is returned automatically without user interaction.
    /// </summary>
    public static Dictionary<string, string> AutoPromptResponses { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new() {
        WriteIndented = false,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Emit a structured event line to stdout and flush immediately.
    /// Event payloads are single-line JSON preceded by the <see cref="Prefix"/>.
    /// </summary>
    public static void Emit(string @event, IDictionary<string, object?>? data = null) {
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
            System.Console.Out.Write(Prefix);
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
    /// Helper class to report determinate progress to the engine UI.
    /// </summary>
    internal sealed class Progress {
        public int Total {
            get;
        }
        public int Current {
            get; private set;
        }
        public string Id {
            get;
        }
        public string? Label {
            get;
        }

        public Progress(int total, string id = "p1", string? label = null) {
            Total = System.Math.Max(1, total);
            Current = 0;
            Id = id;
            Label = label;
            Emit("progress", new Dictionary<string, object?> {
                ["id"] = Id,
                ["current"] = 0,
                ["total"] = Total,
                ["label"] = Label
            });
        }

        public void Update(int inc = 1) {
            Current = System.Math.Min(Total, Current + System.Math.Max(1, inc));
            Emit("progress", new Dictionary<string, object?> {
                ["id"] = Id,
                ["current"] = Current,
                ["total"] = Total,
                ["label"] = Label
            });
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
    // This is the logic from ConsoleProgress.cs, moved inside the SDK
    // and modified to emit events instead of drawing to the console.

    /// <summary>
    /// Simple, reusable console progress panel with an optional list of active jobs.
    /// This class does NOT draw to the console; it emits structured "progress_panel"
    /// events that a listener (like the TUI) can use to render the panel.
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
            int total,
            System.Func<(int processed, int ok, int skip, int err)> snapshot,
            System.Func<List<ActiveProcess>> activeSnapshot,
            string label,
            System.Threading.CancellationToken token) {
            return System.Threading.Tasks.Task.Run(() => {
                // Signal the TUI to prepare for the panel
                EmitPanelStart();

                int spinnerIndex = 0;
                char[] spinner = new[] { '|', '/', '-', '\\' };
                while (!token.IsCancellationRequested) {
                    (int processed, int ok, int skip, int err) s = snapshot();
                    List<ActiveProcess> actives = activeSnapshot();

                    // Build the data payload
                    Dictionary<string, object?> data = BuildPanelData(total, s, actives, spinner[spinnerIndex % spinner.Length], label);

                    // Emit the event
                    Core.Utils.EngineSdk.Emit("progress_panel", data);

                    spinnerIndex = (spinnerIndex + 1) & 0x7fffffff;
                    System.Threading.Thread.Sleep(200);
                }

                // Final event emit
                (int processed, int ok, int skip, int err) finalS = snapshot();
                List<ActiveProcess> finalAct = activeSnapshot();
                Dictionary<string, object?> finalData = BuildPanelData(total, finalS, finalAct, ' ', label);
                Core.Utils.EngineSdk.Emit("progress_panel", finalData);

                // Signal the TUI that the panel is done
                EmitPanelEnd();
            });
        }

        private static void EmitPanelStart() {
            int procs = 8;
            try { procs = System.Math.Max(1, System.Math.Min(16, System.Environment.ProcessorCount)); } catch { /* ignore */ }
            // 1 (progress) + 1 (header/none) + procs (active job lines) + 1 (overflow)
            int reserve = 1 + 1 + procs + 1;
            Core.Utils.EngineSdk.Emit("progress_panel_start", new Dictionary<string, object?> { ["reserve"] = reserve });
        }

        private static void EmitPanelEnd() {
            Core.Utils.EngineSdk.Emit("progress_panel_end");
        }

        private static Dictionary<string, object?> BuildPanelData(int total, (int processed, int ok, int skip, int err) s, List<ActiveProcess> actives, char spinner, string label) {
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
                try { max = System.Math.Max(1, System.Math.Min(16, System.Environment.ProcessorCount)); } catch { /* ignore */ }
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
