using System;
using System.Collections.Generic;
using System.Text.Json;

namespace EngineNet.Core.ScriptEngines.Helpers;

/// <summary>
/// Lightweight SDK to communicate with the Remake Engine runner via stdout JSON events.
/// Mirrors the behavior of Engine/Utils/Engine_sdk.py for C# tools and scripts.
/// </summary>
public static class EngineSdk {
    public const String Prefix = "@@REMAKE@@ ";
    /// <summary>
    /// Optional in-process event sink. When set, Emit will invoke this
    /// delegate with the event payload. If <see cref="MuteStdoutWhenLocalSink"/>
    /// is true, stdout emission is suppressed to avoid double-printing.
    /// </summary>
    public static Action<Dictionary<String, Object?>>? LocalEventSink {
        get; set;
    }
    public static Boolean MuteStdoutWhenLocalSink { get; set; } = true;

    private static readonly JsonSerializerOptions JsonOpts = new() {
        WriteIndented = false,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Emit a structured event line to stdout and flush immediately.
    /// Event payloads are single-line JSON preceded by the <see cref="Prefix"/>.
    /// </summary>
    public static void Emit(String @event, IDictionary<String, Object?>? data = null) {
        Dictionary<String, Object?> payload = new Dictionary<String, Object?>(StringComparer.Ordinal) {
            ["event"] = @event
        };
        if (data != null) {
            foreach (KeyValuePair<String, Object?> kv in data) {
                payload[kv.Key] = kv.Value;
            }
        }

        // Notify in-process sink first (if any)
        if (LocalEventSink != null) {
            try {
                // Pass a shallow copy to avoid accidental modifications by receivers
                LocalEventSink(new Dictionary<String, Object?>(payload, StringComparer.Ordinal));
            } catch { /* ignore sink errors */ }
            if (MuteStdoutWhenLocalSink) {
                return;
            }
        }

        String json;
        try {
            json = JsonSerializer.Serialize(payload, JsonOpts);
        } catch {
            // As a last resort, stringify values to avoid serialization failures
            Dictionary<String, Object?> safe = new Dictionary<String, Object?>(StringComparer.Ordinal);
            foreach (KeyValuePair<String, Object?> kv in payload) {
                safe[kv.Key] = kv.Value?.ToString();
            }

            json = JsonSerializer.Serialize(safe, JsonOpts);
        }

        try {
            Console.Out.Write(Prefix);
            Console.Out.WriteLine(json.Replace('\n', ' '));
            Console.Out.Flush();
        } catch {
            // Swallow IO errors; there is no recovery if stdout is closed
        }
    }

    /// <summary>
    /// Report a non-fatal warning to the engine UI/log.
    /// </summary>
    public static void Warn(String message) => Emit("warning", new Dictionary<String, Object?> { ["message"] = message });

    /// <summary>
    /// Report an error to the engine UI/log (does not exit the process).
    /// </summary>
    public static void Error(String message) => Emit("error", new Dictionary<String, Object?> { ["message"] = message });

    /// <summary>
    /// Informational message (non-warning).
    /// </summary>
    public static void Info(String message) => Print(message, color: "cyan");

    /// <summary>
    /// Success message (green).
    /// </summary>
    public static void Success(String message) => Print(message, color: "green");

    /// <summary>
    /// Mark the start of an operation or phase.
    /// </summary>
    public static void Start(String? op = null) => Emit("start", op is null ? null : new Dictionary<String, Object?> { ["op"] = op });

    /// <summary>
    /// Mark the end of an operation or phase.
    /// </summary>
    public static void End(Boolean success = true, Int32 exitCode = 0) => Emit(
        "end",
        new Dictionary<String, Object?> { ["success"] = success, ["exit_code"] = exitCode }
    );

    /// <summary>
    /// Prompt the user for input. Emits a prompt event, then blocks to read a single line from stdin.
    /// Returns the answer without the trailing newline. May return an empty string.
    /// </summary>
    public static String Prompt(String message, String id = "q1", Boolean secret = false) {
        Emit("prompt", new Dictionary<String, Object?> { ["id"] = id, ["message"] = message, ["secret"] = secret });
        try {
            String? line = Console.In.ReadLine();
            return (line ?? String.Empty).TrimEnd('\n');
        } catch {
            return String.Empty;
        }
    }

    /// <summary>
    /// Helper class to report determinate progress to the engine UI.
    /// </summary>
    public sealed class Progress {
        public Int32 Total {
            get;
        }
        public Int32 Current {
            get; private set;
        }
        public String Id {
            get;
        }
        public String? Label {
            get;
        }

        public Progress(Int32 total, String id = "p1", String? label = null) {
            Total = Math.Max(1, total);
            Current = 0;
            Id = id;
            Label = label;
            Emit("progress", new Dictionary<String, Object?> {
                ["id"] = Id,
                ["current"] = 0,
                ["total"] = Total,
                ["label"] = Label
            });
        }

        public void Update(Int32 inc = 1) {
            Current = Math.Min(Total, Current + Math.Max(1, inc));
            Emit("progress", new Dictionary<String, Object?> {
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
    public static void Print(String message, String? color = null, Boolean newline = true) {
        Dictionary<String, Object?> data = new Dictionary<String, Object?> {
            ["message"] = message,
            ["color"] = String.IsNullOrWhiteSpace(color) ? null : color,
            ["newline"] = newline
        };
        Emit("print", data);
    }

    /// <summary>
    /// Emit a colored print event using a ConsoleColor.
    /// </summary>
    public static void Print(String message, ConsoleColor color, Boolean newline = true) {
        Print(message, color.ToString(), newline);
    }
}
