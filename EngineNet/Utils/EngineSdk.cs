using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RemakeEngine.Utils;

/// <summary>
/// Lightweight SDK to communicate with the Remake Engine runner via stdout JSON events.
/// Mirrors the behavior of Engine/Utils/Engine_sdk.py for C# tools and scripts.
/// </summary>
public static class EngineSdk
{
    public const string Prefix = "@@REMAKE@@ ";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Emit a structured event line to stdout and flush immediately.
    /// Event payloads are single-line JSON preceded by the <see cref="Prefix"/>.
    /// </summary>
    public static void Emit(string @event, IDictionary<string, object?>? data = null)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["event"] = @event
        };
        if (data != null)
        {
            foreach (var kv in data)
                payload[kv.Key] = kv.Value;
        }

        string json;
        try
        {
            json = JsonSerializer.Serialize(payload, JsonOpts);
        }
        catch
        {
            // As a last resort, stringify values to avoid serialization failures
            var safe = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in payload)
                safe[kv.Key] = kv.Value?.ToString();
            json = JsonSerializer.Serialize(safe, JsonOpts);
        }

        try
        {
            Console.Out.Write(Prefix);
            Console.Out.WriteLine(json.Replace('\n', ' '));
            Console.Out.Flush();
        }
        catch
        {
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
    /// </summary>
    public static string Prompt(string message, string id = "q1", bool secret = false)
    {
        Emit("prompt", new Dictionary<string, object?> { ["id"] = id, ["message"] = message, ["secret"] = secret });
        try
        {
            var line = Console.In.ReadLine();
            return (line ?? string.Empty).TrimEnd('\n');
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Helper class to report determinate progress to the engine UI.
    /// </summary>
    public sealed class Progress
    {
        public int Total { get; }
        public int Current { get; private set; }
        public string Id { get; }
        public string? Label { get; }

        public Progress(int total, string id = "p1", string? label = null)
        {
            Total = Math.Max(1, total);
            Current = 0;
            Id = id;
            Label = label;
            EngineSdk.Emit("progress", new Dictionary<string, object?>
            {
                ["id"] = Id,
                ["current"] = 0,
                ["total"] = Total,
                ["label"] = Label
            });
        }

        public void Update(int inc = 1)
        {
            Current = Math.Min(Total, Current + Math.Max(1, inc));
            EngineSdk.Emit("progress", new Dictionary<string, object?>
            {
                ["id"] = Id,
                ["current"] = Current,
                ["total"] = Total,
                ["label"] = Label
            });
        }
    }
}

