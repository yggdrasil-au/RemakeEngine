
namespace EngineNet.Shared.IO.UI;

/// <summary>
/// Engine SDK for emitting structured events to the UI from anywhere in the engine or Lua scripts.
/// </summary>
public static class EngineSdk {
    /* :: :: Vars :: START :: */

    // Optional in-process event sink. When set, Emit will invoke this delegate with the event payload. If <see cref="MuteStdoutWhenLocalSink"/>
    // is true, stdout emission is suppressed to avoid double-printing.
    public static System.Action<Dictionary<string, object?>>? LocalEventSink { get; set; }
    public static bool MuteStdoutWhenLocalSink { get; set; } = true;

    /// <summary>
    /// Optional external prompt handler. When set, Prompt and Confirm will invoke this delegate instead of blocking on stdin.
    /// </summary>
    public static System.Func<string, bool, string?>? ExternalPromptHandler { get; set; }

    // Auto-responses for prompts by ID. When a prompt with matching ID is requested, the corresponding response is returned automatically without user interaction.
    public static Dictionary<string, string> AutoPromptResponses { get; set; } = new Dictionary<string, string>(comparer: System.StringComparer.OrdinalIgnoreCase);

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new() {
        WriteIndented = false,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Shared registry of structured event names emitted and consumed by the engine.
    /// Keep this synchronized with the GUI, terminal, and runner code that switches on event payloads.
    /// </summary>
    public static class Events {
        public const string Print = "print";
        public const string Warning = "warning";
        public const string Error = "error";
        public const string Prompt = "prompt";
        public const string ColorPrompt = "color_prompt";
        public const string Confirm = "confirm";
        //public const string Progress = "progress";
        public const string Start = "start";
        public const string End = "end";
        public const string ProgressPanelStart = "progress_panel_start";
        public const string ProgressPanel = "progress_panel";
        public const string ProgressPanelEnd = "progress_panel_end";
        public const string ScriptActiveStart = "script_active_start";
        public const string ScriptProgress = "script_progress";
        public const string ScriptActiveEnd = "script_active_end";
        public const string RunAllStart = "run-all-start";
        public const string RunAllOpStart = "run-all-op-start";
        public const string RunAllOpEnd = "run-all-op-end";
        public const string RunAllOpError = "run-all-op-error";
        public const string RunAllComplete = "run-all-complete";
    }

    /* :: :: Vars :: END :: */
    //
    /* :: :: Methods :: START :: */

    /// <summary>
    /// dont use directly.
    /// Emit a structured event line to stdout and flush immediately.

    /// </summary>
    private static void Emit(string @event, IDictionary<string, object?>? data = null) {
        Dictionary<string, object?> payload = new Dictionary<string, object?>(comparer: System.StringComparer.Ordinal) {
            [key: "event"] = @event
        };
        if (data != null) {
            foreach (KeyValuePair<string, object?> kv in data) {
                payload[key: kv.Key] = kv.Value;
            }
        }

        // Notify in-process sink first (if any)
        if (LocalEventSink != null) {
            try {
                // Pass a shallow copy to avoid accidental modifications by receivers
                LocalEventSink(obj: new Dictionary<string, object?>(dictionary: payload, comparer: System.StringComparer.Ordinal));
            } catch (System.Exception ex) {
                Shared.IO.Diagnostics.Bug($"[EngineSdk::Emit()] Local event sink failed: {ex}");
                /* ignore sink errors */
            }
            if (MuteStdoutWhenLocalSink) {
                return;
            }
        }

        string json;
        try {
            json = System.Text.Json.JsonSerializer.Serialize(payload, options: JsonOpts);
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[EngineSdk::Emit()] Failed to serialize event payload for '{@event}': {ex}");
            // As a last resort, stringify values to avoid serialization failures
            Dictionary<string, object?> safe = new Dictionary<string, object?>(comparer: System.StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> kv in payload) {
                safe[key: kv.Key] = kv.Value?.ToString();
            }

            json = System.Text.Json.JsonSerializer.Serialize(safe, options: JsonOpts);
        }

        try {
            //System.Console.Out.Write(Prefix);
            System.Console.Out.WriteLine(json.Replace(oldChar: '\n', newChar: ' '));
            System.Console.Out.Flush();
        } catch (System.IO.IOException ex) {
            Shared.IO.Diagnostics.Bug($"[EngineSdk::Emit()] IO error writing event '{@event}' to stdout: {ex}");
            // Swallow IO errors; there is no recovery if stdout is closed
        } catch (System.ObjectDisposedException ex) {
            Shared.IO.Diagnostics.Bug($"[EngineSdk::Emit()] Stdout disposed while writing event '{@event}': {ex}");
            // Swallow IO errors; there is no recovery if stdout is closed
        }
    }

    /// <summary>
    /// Report a non-fatal warning to the engine UI/log.
    /// </summary>
    public static void Warn(string message) {
        Emit(@event: Events.Warning, data: new Dictionary<string, object?> { [key: "message"] = message });
    }

    /// <summary>
    /// Report an error to the engine UI/log (does not exit the process).
    /// </summary>
    public static void Error(string message) {
        Emit(@event: Events.Error, data: new Dictionary<string, object?> { [key: "message"] = message });
    }

    public static string color_prompt(string message, string color, string id = "q1", bool secret = false) {
        // Check for auto-response first
        if (AutoPromptResponses.TryGetValue(key: id, out string? autoResponse)) {
            Emit(@event: Events.Print, data: new Dictionary<string, object?> {
                [key: "message"] = $"? {message}",
                [key: "color"] = color
            });
            Emit(@event: Events.Print, data: new Dictionary<string, object?> {
                [key: "message"] = $"> {autoResponse} (auto-response)",
                [key: "color"] = "yellow"
            });
            return autoResponse;
        }

        Emit(@event: Events.ColorPrompt, data: new Dictionary<string, object?> { [key: "id"] = id, [key: "message"] = message, [key: "color"] = color, [key: "secret"] = secret });
        try {
            string? line = System.Console.In.ReadLine();
            return (line ?? string.Empty).TrimEnd(trimChar: '\n');
        } catch (System.IO.IOException ex) {
            Shared.IO.Diagnostics.Bug($"[EngineSdk::color_prompt()] IO error while reading console input: {ex}");
            return string.Empty;
        } catch (System.ObjectDisposedException ex) {
            Shared.IO.Diagnostics.Bug($"[EngineSdk::color_prompt()] Console input disposed: {ex}");
            return string.Empty;
        } catch (System.InvalidOperationException ex) {
            Shared.IO.Diagnostics.Bug($"[EngineSdk::color_prompt()] Console input unavailable: {ex}");
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
        if (AutoPromptResponses.TryGetValue(key: id, out string? autoResponse)) {
            Emit(@event: Events.Print, data: new Dictionary<string, object?> {
                [key: "message"] = $"? {message}",
                [key: "color"] = "cyan"
            });
            Emit(@event: Events.Print, data: new Dictionary<string, object?> {
                [key: "message"] = $"> {autoResponse} (auto-response)",
                [key: "color"] = "yellow"
            });
            return autoResponse;
        }

        // Intercept standard blocking read and route to custom UI loop if attached
        if (ExternalPromptHandler != null) {
            return ExternalPromptHandler(arg1: message, arg2: secret) ?? string.Empty;
        }

        Emit(@event: Events.Prompt, data: new Dictionary<string, object?> { [key: "id"] = id, [key: "message"] = message, [key: "secret"] = secret });
        try {
            string? line = System.Console.In.ReadLine();
            return (line ?? string.Empty).TrimEnd(trimChar: '\n');
        } catch (System.IO.IOException ex) {
            Shared.IO.Diagnostics.Bug($"[EngineSdk::Prompt()] IO error while reading console input: {ex}");
            return string.Empty;
        } catch (System.ObjectDisposedException ex) {
            Shared.IO.Diagnostics.Bug($"[EngineSdk::Prompt()] Console input disposed: {ex}");
            return string.Empty;
        } catch (System.InvalidOperationException ex) {
            Shared.IO.Diagnostics.Bug($"[EngineSdk::Prompt()] Console input unavailable: {ex}");
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
        if (AutoPromptResponses.TryGetValue(key: id, out string? autoResponse)) {
            Emit(@event: Events.Print, data: new Dictionary<string, object?> {
                [key: "message"] = $"? {message} [y/n]",
                [key: "color"] = "cyan"
            });
            Emit(@event: Events.Print, data: new Dictionary<string, object?> {
                [key: "message"] = $"> {autoResponse} (auto-response)",
                [key: "color"] = "yellow"
            });
            return autoResponse.Trim().StartsWith("y", comparisonType: System.StringComparison.OrdinalIgnoreCase) ||
                    autoResponse.Trim().Equals("true", comparisonType: System.StringComparison.OrdinalIgnoreCase);
        }

        // Intercept standard blocking read and route to custom UI loop if attached
        if (ExternalPromptHandler != null) {
            return ExternalPromptHandler(arg1: message + " [y/n]", arg2: false)?.Trim().StartsWith("y", comparisonType: System.StringComparison.OrdinalIgnoreCase) == true ||
                   ExternalPromptHandler(arg1: message + " [y/n]", arg2: false)?.Trim().Equals("true", comparisonType: System.StringComparison.OrdinalIgnoreCase) == true;
        }

        Emit(@event: Events.Confirm, data: new Dictionary<string, object?> { [key: "id"] = id, [key: "message"] = message, [key: "default"] = defaultValue });
        try {
            string? line = System.Console.In.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) return defaultValue;
            return line.Trim().StartsWith("y", comparisonType: System.StringComparison.OrdinalIgnoreCase) ||
                    line.Trim().Equals("true", comparisonType: System.StringComparison.OrdinalIgnoreCase) ||
                    line.Trim().Equals("yes", comparisonType: System.StringComparison.OrdinalIgnoreCase);
        } catch (System.IO.IOException ex) {
            Shared.IO.Diagnostics.Bug($"[EngineSdk::Confirm()] IO error while reading console input: {ex}");
            return defaultValue;
        } catch (System.ObjectDisposedException ex) {
            Shared.IO.Diagnostics.Bug($"[EngineSdk::Confirm()] Console input disposed: {ex}");
            return defaultValue;
        } catch (System.InvalidOperationException ex) {
            Shared.IO.Diagnostics.Bug($"[EngineSdk::Confirm()] Console input unavailable: {ex}");
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
            [key: "message"] = message,
            [key: "color"] = string.IsNullOrWhiteSpace(color) ? null : color,
            [key: "newline"] = newline
        };
        Emit(@event: Events.Print, data: data);
    }

    /// <summary>
    /// Emit a colored print event with a trailing newline.
    /// </summary>
    public static void PrintLine(string message) {
        Print(message, color: null, newline: true);
    }

    /// <summary>
    /// Emit a colored print event using a ConsoleColor with a trailing newline.
    /// </summary>
    public static void PrintLine(string message, System.ConsoleColor color) {
        Print(message, color: color.ToString(), newline: true);
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
        private string _label;

        public int Total => _total;
        public int Current => System.Threading.Volatile.Read(location: ref _processed);
        public string Id { get; }
        public string Label => _label;

        public ScriptProgress(int total, string id = "s1", string? label = null) {
            _total = System.Math.Max(val1: 1, val2: total);
            Id = id;
            _label = label ?? string.Empty;
            _processed = 0;
            EmitProgress();
        }

        public void Update(int inc = 1, string? newLabel = null) {
            if (newLabel != null) _label = newLabel;
            int add = System.Math.Max(val1: 1, val2: inc);
            int newVal = System.Threading.Interlocked.Add(location1: ref _processed, add);
            if (newVal > _total) {
                System.Threading.Interlocked.Exchange(location1: ref _processed, _total);
            }
            EmitProgress();
        }

        public void SetTotal(int total) {
            _total = System.Math.Max(val1: 1, val2: total);
            EmitProgress();
        }

        public void Complete() {
            try {
                System.Threading.Interlocked.Exchange(location1: ref _processed, _total);
                EmitProgress();
            } catch (System.Exception ex) {
                Shared.IO.Diagnostics.Bug($"[EngineSdk::ScriptProgress::Complete()] Failed to emit final script progress: {ex}");
            }
        }

        private void EmitProgress() {
            Dictionary<string, object?> data = new Dictionary<string, object?> {
                [key: "id"] = Id,
                [key: "current"] = System.Threading.Volatile.Read(location: ref _processed),
                [key: "total"] = _total,
                [key: "label"] = _label
            };
            Emit(@event: Events.ScriptProgress, data: data);
        }
    }

    /// <summary>
    /// Signal the start of a script run to consumers (e.g., GUI bottom panel).
    /// should only be called in the main entry point of a script action (e.g., Lua.Main.ExecuteAsync) to indicate that a script is active.
    /// </summary>
    public static void ScriptActiveStart(string scriptPath) {
        string name = string.Empty;
        try {
            name = System.IO.Path.GetFileName(path: scriptPath);
        } catch (System.ArgumentException ex) {
            Shared.IO.Diagnostics.Bug($"[EngineSdk::ScriptActiveStart()] Invalid script path '{scriptPath}': {ex}");
        } catch (System.IO.PathTooLongException ex) {
            Shared.IO.Diagnostics.Bug($"[EngineSdk::ScriptActiveStart()] Script path too long '{scriptPath}': {ex}");
        } catch (System.NotSupportedException ex) {
            Shared.IO.Diagnostics.Bug($"[EngineSdk::ScriptActiveStart()] Unsupported script path '{scriptPath}': {ex}");
        }
        Emit(@event: Events.ScriptActiveStart, data: new Dictionary<string, object?> {
            [key: "name"] = string.IsNullOrEmpty(name) ? scriptPath : name,
            [key: "path"] = scriptPath
        });
    }

    /// <summary>
    /// Signal the end of a script run to consumers, with success status and optional exit code.
    /// should only be called in the main entry point of a script action (e.g., Lua.Main.ExecuteAsync) to indicate that a script has finished.
    /// </summary>
    public static void ScriptActiveEnd(bool success = true, int exitCode = 0) {
        Emit(@event: Events.ScriptActiveEnd, data: new Dictionary<string, object?> {
            [key: "success"] = success,
            [key: "exit_code"] = exitCode
        });
    }
    /* :: :: Script Progress :: END :: */

    //


    // progress indicates the 'progress' of a single lua script, managed by the script itself via PanelProgress handles.
    //progress.start(19, 'label')
    //progress.step('label')
    //progress.finish()

    /// <summary>
    /// Progress handle now backed by SdkConsoleProgress panel events.
    /// Provides Update(int) while allowing dynamic total/label updates for the TUI panel.
    /// </summary>
    public sealed class PanelProgress : System.IDisposable {
        private readonly System.Threading.CancellationTokenSource _cts;
        private readonly System.Threading.Tasks.Task _panelTask;
        private long _processed;
        private long _total;
        private string _label;

        /// <summary>
        /// Gets the current processed count.
        /// </summary>
        public long Current => System.Threading.Volatile.Read(location: ref _processed);

        /// <summary>
        /// Gets or sets the total item count for the panel.
        /// </summary>
        public long Total {
            get => System.Threading.Interlocked.Read(location: ref _total);
            set => System.Threading.Interlocked.Exchange(location1: ref _total, System.Math.Max(val1: 1, val2: value));
        }

        /// <summary>
        /// Gets or sets the panel label.
        /// </summary>
        public string? Label {
            get => System.Threading.Volatile.Read(location: ref _label);
            set => System.Threading.Volatile.Write(location: ref _label, value ?? string.Empty);
        }
        public string Id { get; }

        public PanelProgress(long total, string id = "p1", string? label = null) {
            _total = System.Math.Max(val1: 1, val2: total);
            Id = id;
            _label = label ?? string.Empty;
            _processed = 0;
            _cts = new System.Threading.CancellationTokenSource();
            _panelTask = SdkConsoleProgress.StartPanel(
                total: () => System.Threading.Interlocked.Read(location: ref _total),
                snapshot: () => {
                    long p = System.Threading.Volatile.Read(location: ref _processed);
                    // Use 'ok' equal to processed (clamped to int) for a simple linear flow.
                    int ok = p > int.MaxValue ? int.MaxValue : (int)p;
                    return (processed: p, ok, skip: 0, err: 0);
                },
                activeSnapshot: () => new List<SdkConsoleProgress.ActiveProcess>(),
                label: () => System.Threading.Volatile.Read(location: ref _label),
                token: _cts.Token,
                id: Id
            );
        }

        public void Update(long inc = 1) {
            long add = System.Math.Max(val1: 1, val2: inc);
            long newVal = System.Threading.Interlocked.Add(location1: ref _processed, add);
            long total = System.Threading.Interlocked.Read(location: ref _total);
            if (newVal > total) {
                System.Threading.Interlocked.Exchange(location1: ref _processed, total);
            }
        }

        /// <summary>
        /// Sets the total item count for the panel.
        /// </summary>
        public void SetTotal(long total) {
            System.Threading.Interlocked.Exchange(location1: ref _total, System.Math.Max(val1: 1, val2: total));
        }

        /// <summary>
        /// Sets the label text for the panel.
        /// </summary>
        public void SetLabel(string? label) {
            System.Threading.Volatile.Write(location: ref _label, label ?? string.Empty);
        }

        public void Complete() {
            try {
                if (!_cts.IsCancellationRequested) {
                    _cts.Cancel();
                }
                try { _panelTask.Wait(); } catch (System.AggregateException ex) {
                    Shared.IO.Diagnostics.Bug($"[EngineSdk::PanelProgress::Complete()] Failed while waiting for panel task completion: {ex}");
                } catch (System.ObjectDisposedException ex) {
                    Shared.IO.Diagnostics.Bug($"[EngineSdk::PanelProgress::Complete()] Panel task disposed while waiting: {ex}");
                }
            } catch (System.ObjectDisposedException ex) {
                Shared.IO.Diagnostics.Bug($"[EngineSdk::PanelProgress::Complete()] Cancellation source disposed: {ex}");
            } catch (System.InvalidOperationException ex) {
                Shared.IO.Diagnostics.Bug($"[EngineSdk::PanelProgress::Complete()] Failed to cancel panel task: {ex}");
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
        /// until the token is cancelled. Total and label are provided via delegates
        /// to allow dynamic updates.
        /// </summary>
        public static System.Threading.Tasks.Task StartPanel(
            System.Func<long> total,
            System.Func<(long processed, int ok, int skip, int err)> snapshot,
            System.Func<List<ActiveProcess>> activeSnapshot,
            System.Func<string> label,
            System.Threading.CancellationToken token,
            string id = "p1"
        ) {
            return System.Threading.Tasks.Task.Run(action: () => {
                // Signal the TUI to prepare for the panel
                EmitPanelStart(id: id);

                int spinnerIndex = 0;
                char[] spinner = new[] { '|', '/', '-', '\\' };
                while (!token.IsCancellationRequested) {
                    (long processed, int ok, int skip, int err) s = snapshot();
                    List<ActiveProcess> actives = activeSnapshot();
                    long totalValue = total();
                    string labelValue = label();

                    // Build the data payload
                    Dictionary<string, object?> data = BuildPanelData(total: totalValue, s: s, actives: actives, spinner: spinner[spinnerIndex % spinner.Length], label: labelValue);
                    data[key: "id"] = id;

                    // Emit the event
                    Emit(@event: Events.ProgressPanel, data: data);

                    spinnerIndex = (spinnerIndex + 1) & 0x7fffffff;
                    System.Threading.Thread.Sleep(millisecondsTimeout: 200);
                }

                // Final event emit
                (long processed, int ok, int skip, int err) finalS = snapshot();
                List<ActiveProcess> finalAct = activeSnapshot();
                long finalTotal = total();
                string finalLabel = label();
                Dictionary<string, object?> finalData = BuildPanelData(total: finalTotal, s: finalS, actives: finalAct, spinner: ' ', label: finalLabel);
                finalData[key: "id"] = id;
                Emit(@event: Events.ProgressPanel, data: finalData);

                // Signal the TUI that the panel is done
                EmitPanelEnd(id: id);
            }, cancellationToken: token);
        }

        private static void EmitPanelStart(string id) {
            int procs = 8;
            try { procs = System.Math.Max(val1: 1, val2: System.Math.Min(val1: 16, val2: System.Environment.ProcessorCount)); } catch (System.Exception ex) {
                Shared.IO.Diagnostics.Bug($"[EngineSdk::SdkConsoleProgress::EmitPanelStart()] Failed to read processor count: {ex}");
                /* ignore */
            }
            // 1 (progress) + 1 (header/none) + procs (active job lines) + 1 (overflow)
            int reserve = 1 + 1 + procs + 1;
            Emit(@event: Events.ProgressPanelStart, data: new Dictionary<string, object?> { [key: "reserve"] = reserve, [key: "id"] = id });
        }

        private static void EmitPanelEnd(string id) {
            Emit(@event: Events.ProgressPanelEnd, data: new Dictionary<string, object?> { [key: "id"] = id });
        }

        private static Dictionary<string, object?> BuildPanelData(long total, (long processed, int ok, int skip, int err) s, List<ActiveProcess> actives, char spinner, string label) {
            if (total < 0) total = 0;

            double percent = System.Math.Clamp(total == 0 ? 1.0 : (double)s.processed / System.Math.Max(val1: 1, val2: total), min: 0.0, max: 1.0);

            Dictionary<string, object?> stats = new Dictionary<string, object?> {
                [key: "total"] = total,
                [key: "processed"] = s.processed,
                [key: "ok"] = s.ok,
                [key: "skip"] = s.skip,
                [key: "err"] = s.err,
                [key: "percent"] = percent
            };

            List<Dictionary<string, object?>> jobList = new List<Dictionary<string, object?>>();
            if (actives.Count > 0) {
                int max = 8;
                try {
                    max = System.Math.Max(val1: 1, val2: System.Math.Min(val1: 16, val2: System.Environment.ProcessorCount));
                } catch (System.Exception ex) {
                    Shared.IO.Diagnostics.Bug($"[EngineSdk::SdkConsoleProgress::BuildPanelData()] Failed to read processor count: {ex}");
                    /* ignore */
                }
                System.DateTime now = System.DateTime.UtcNow;

                foreach (ActiveProcess job in actives.OrderBy(keySelector: j => j.StartedUtc).Take(count: max)) {
                    System.TimeSpan elapsed = now - job.StartedUtc;
                    string elStr = elapsed.TotalHours >= 1
                        ? $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
                        : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";

                    jobList.Add(item: new Dictionary<string, object?> {
                        [key: "tool"] = job.Tool,
                        [key: "file"] = job.File,
                        [key: "elapsed"] = elStr
                    });
                }
            }

            return new Dictionary<string, object?> {
                [key: "label"] = label,
                [key: "spinner"] = spinner.ToString(),
                [key: "stats"] = stats,
                [key: "active_jobs"] = jobList,
                [key: "active_total"] = actives.Count
            };
        }
    }

}
