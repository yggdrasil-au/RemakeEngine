using System.Collections.Specialized;
using System.Runtime.CompilerServices;

using Avalonia.Threading;

using EngineNet.GUI.Models;
using EngineNet.Shared.IO.UI;

namespace EngineNet.GUI.Services;

/// <summary>
/// Shared service for capturing and displaying operation output across all GUI pages.
/// This ensures that output persists when navigating between pages.
/// </summary>
public sealed class OperationOutputService : INotifyPropertyChanged {
    internal static OperationOutputService Instance { get; } = new OperationOutputService();

    private readonly Lock _lock = new Lock();

    private OperationOutputService() {
        Lines.CollectionChanged += OnLinesCollectionChanged;
    }

    /// <summary>
    /// Shared output lines collection. Thread-safe via Dispatcher.
    /// </summary>
    internal ObservableCollection<OutputLine> Lines { get; } = new ObservableCollection<OutputLine>();

    private readonly HashSet<OutputLine> _trackedLines = new HashSet<OutputLine>();
    private bool _isFullLogDirty = true;

    /// <summary>
    /// Combined output text for log selection and clipboard copy.
    /// </summary>
    public string FullLogText {
        get {
            if (_isFullLogDirty) {
                field = BuildFullLogText();
                _isFullLogDirty = false;
            }

            return field;
        }
    } = string.Empty;

    public ObservableCollection<ActiveJob> ActiveJobs { get; } = new ObservableCollection<ActiveJob>();

    // Multiple concurrent task progress panels (keyed by id when provided by the engine)
    public ObservableCollection<ProgressPanelState> TaskPanels { get; } = new ObservableCollection<ProgressPanelState>();

    private readonly Dictionary<string, ProgressPanelState> _panelsById = new Dictionary<string, ProgressPanelState>(comparer: StringComparer.Ordinal);

    public string? CurrentOperation {
        get {
            lock (_lock) {
                return field;
            }
        }
        private set {
            lock (_lock) {
                if (EqualityComparer<string?>.Default.Equals(x: field, y: value)) return;
                field = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsProgressPanelActive {
        get;
        private set => SetField(field: ref field, value);
    }


    public string? ProgressLabel {
        get;
        private set => SetField(field: ref field, value);
    }

    public string? ProgressSummaryLine {
        get;
        private set => SetField(field: ref field, value);
    }

    public double ProgressPercent {
        get;
        private set => SetField(field: ref field, value);
    }

    // Script activity tracking (stage-based indicator)
    private string _activeScriptName = string.Empty;


    public string ActiveJobsSummary {
        get;
        private set => SetField(field: ref field, value);
    } = "Active: none";

    public int ActiveJobCount {
        get;
        private set => SetField(field: ref field, value);
    }

    public string CurrentSpinner {
        get;
        private set => SetField(field: ref field, value);
    } = string.Empty;

    private readonly List<OutputLine> _progressPanelLines = new List<OutputLine>();
    private int _progressPanelInsertIndex = -1;

    /// <summary>
    /// Clear output and start a new operation.
    /// </summary>
    public void StartOperation(string operationName, string gameName) {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(action: () => {
            Lines.Clear();
            ResetProgressPanelTracking();
            ActiveJobs.Clear();
            ActiveJobsSummary = "Active: none";
            ActiveJobCount = 0;
            CurrentSpinner = string.Empty;
            ProgressLabel = string.Empty;
            ProgressSummaryLine = string.Empty;
            ProgressPercent = 0;
            IsProgressPanelActive = false;

            CurrentOperation = $"{gameName} - {operationName}";
            AddLine(text: $"=== Starting: {operationName} for {gameName} ===", type: "header");
        });
    }

    // --- High-volume output buffering/throttling state ---
    private readonly System.Collections.Concurrent.ConcurrentQueue<OutputLine> _pendingLines = new System.Collections.Concurrent.ConcurrentQueue<OutputLine>();
    private DispatcherTimer? _flushTimer;
    private readonly Lock _flushLock = new Lock();
    private const int FlushBatchMax = 250;
    private const int MaxLines = 5000;
    private const int MaxChars = 200_000;
    private int _currentChars;
    private bool _flushTimerRequested;

    /// <summary>
    /// Add a raw output line (buffered + throttled for UI responsiveness).
    /// </summary>
    public void AddOutput(string text, string stream = "stdout") {
        OutputLine line = new OutputLine {
            Timestamp = System.DateTime.Now,
            Text = text,
            Type = stream == "stderr" ? "error" : "output",
            Color = stream == "stderr" ? "Red" : "Gray"
        };
        EnqueueLine(line: line);
    }

    private void EnqueueLine(OutputLine line) {
        _pendingLines.Enqueue(item: line);
        EnsureFlushTimer();
    }

    private void EnsureFlushTimer() {
        lock (_flushLock) {
            if (_flushTimerRequested) return;

            _flushTimerRequested = true;
            global::Avalonia.Threading.Dispatcher.UIThread.Post(action: () => {
                lock (_flushLock) {
                    if (_flushTimer is null) {
                        _flushTimer = new DispatcherTimer() { Interval = System.TimeSpan.FromMilliseconds(milliseconds: 33) };
                        _flushTimer.Tick += FlushPending;
                    }
                    if (!_flushTimer.IsEnabled) {
                        _flushTimer.Start();
                    }
                }
            });
        }
    }

    private void FlushPending(object? sender, System.EventArgs e) {
        Dispatcher.UIThread.VerifyAccess();
        int processed = 0;
        while (processed < FlushBatchMax && _pendingLines.TryDequeue(result: out OutputLine? line)) {
            Lines.Add(item: line);
            _currentChars += line.Text?.Length ?? 0;
            processed++;
        }
        TrimIfNeeded();
        if (!_pendingLines.IsEmpty || processed != 0) return;
        lock (_flushLock) {
            _flushTimer?.Stop();
            _flushTimerRequested = false;
        }
    }

    private void TrimIfNeeded() {
        int removed = 0;
        while (Lines.Count > MaxLines) {
            OutputLine first = Lines[index: 0];
            _currentChars -= first.Text?.Length ?? 0;
            Lines.RemoveAt(index: 0);
            removed++;
        }
        if (_currentChars > MaxChars) {
            int idx = 0;
            while (_currentChars > MaxChars && idx < Lines.Count) {
                OutputLine first = Lines[index: idx];
                _currentChars -= first.Text?.Length ?? 0;
                Lines.RemoveAt(index: idx);
                removed++;
            }
        }
        if (removed > 0 && _progressPanelInsertIndex >= 0) {
            _progressPanelInsertIndex = System.Math.Max(val1: 0, val2: _progressPanelInsertIndex - removed);
        }
    }

    private void OnLinesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        if (e.Action == NotifyCollectionChangedAction.Reset) {
            foreach (OutputLine line in _trackedLines) {
                line.PropertyChanged -= OnLinePropertyChanged;
            }
            _trackedLines.Clear();
            MarkFullLogDirty();
            return;
        }

        if (e.OldItems != null) {
            foreach (object? item in e.OldItems) {
                if (item is OutputLine line && _trackedLines.Remove(item: line)) {
                    line.PropertyChanged -= OnLinePropertyChanged;
                }
            }
        }

        if (e.NewItems != null) {
            foreach (object? item in e.NewItems) {
                if (item is OutputLine line && _trackedLines.Add(item: line)) {
                    line.PropertyChanged += OnLinePropertyChanged;
                }
            }
        }

        MarkFullLogDirty();
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(OutputLine.Text)) {
            MarkFullLogDirty();
        }
    }

    private void MarkFullLogDirty() {
        _isFullLogDirty = true;
        OnPropertyChanged(propertyName: nameof(FullLogText));
    }

    private string BuildFullLogText() {
        if (Lines.Count == 0) {
            return string.Empty;
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder(capacity: _currentChars + Lines.Count);
        for (int i = 0; i < Lines.Count; i++) {
            if (i > 0) {
                builder.Append('\n');
            }
            builder.Append(Lines[index: i].Text);
        }
        return builder.ToString();
    }

    /// <summary>
    /// Handle a structured event from the engine.
    /// </summary>
    public void HandleEvent(Dictionary<string, object?> evt) {
        if (!evt.TryGetValue(key: "event", out object? evtTypeObj)) {
            return;
        }

        string? evtType = evtTypeObj?.ToString();

            switch (evtType) {
            case EngineSdk.Events.Print:
                string msg = evt.TryGetValue(key: "message", out object? m) ? m?.ToString() ?? string.Empty : string.Empty;
                string color = evt.TryGetValue(key: "color", out object? c) ? c?.ToString() ?? "Gray" : "Gray";
                EnqueueLine(line: new OutputLine {
                    Timestamp = System.DateTime.Now,
                    Text = msg,
                    Type = "print",
                    Color = MapColor(colorName: color)
                });
                break;

                case EngineSdk.Events.Warning:
                    string warnMsg = evt.TryGetValue(key: "message", out object? wm) ? wm?.ToString() ?? string.Empty : string.Empty;
                    EnqueueLine(line: new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"⚠ {warnMsg}",
                        Type = "warning",
                        Color = "Yellow"
                    });
                    break;

                case EngineSdk.Events.Error:
                    string errMsg = evt.TryGetValue(key: "message", out object? em) ? em?.ToString() ?? string.Empty : string.Empty;
                    EnqueueLine(line: new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"✖ {errMsg}",
                        Type = "error",
                        Color = "Red"
                    });
                    break;

                case EngineSdk.Events.Prompt:
                    string promptMsg = evt.TryGetValue(key: "message", out object? pm) ? pm?.ToString() ?? string.Empty : string.Empty;
                    EnqueueLine(line: new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"? {promptMsg}",
                        Type = "prompt",
                        Color = "Cyan"
                    });
                    break;

                // this event is removed, replaced by progress panel events
                /*case EngineSdk.Events.Progress:
                    int current = evt.TryGetValue("current", out object? cur) ? SafeToInt(cur) : 0;
                    int total = evt.TryGetValue("total", out object? tot) ? SafeToInt(tot) : 0;
                    string label = evt.TryGetValue("label", out object? lbl) ? lbl?.ToString() ?? string.Empty : string.Empty;
                    EnqueueLine(new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"[{current}/{total}] {label}",
                        Type = "progress",
                        Color = "Cyan"
                    });
                    break;*/

                case EngineSdk.Events.Start:
                    string startContext = FormatEventData(evt: evt);
                    EnqueueLine(line: new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"▶ Started: {startContext}",
                        Type = "start",
                        Color = "Green"
                    });
                    break;

                case EngineSdk.Events.End:
                    bool success = evt.TryGetValue(key: "success", out object? suc) && suc is bool b && b;
                    EnqueueLine(line: new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = success ? "✓ Completed successfully" : "✗ Completed with errors",
                        Type = "end",
                        Color = success ? "Green" : "Red"
                    });
                    break;

            case EngineSdk.Events.ProgressPanelStart:
                global::Avalonia.Threading.Dispatcher.UIThread.Post(action: () => HandleProgressPanelStart(payload: evt));
                break;

            case EngineSdk.Events.ProgressPanel:
                global::Avalonia.Threading.Dispatcher.UIThread.Post(action: () => HandleProgressPanelUpdate(payload: evt));
                break;

            case EngineSdk.Events.ProgressPanelEnd:
                global::Avalonia.Threading.Dispatcher.UIThread.Post(action: () => HandleProgressPanelEnd(payload: evt));
                break;

            case EngineSdk.Events.ScriptActiveStart:
                global::Avalonia.Threading.Dispatcher.UIThread.Post(action: () => HandleScriptActiveStart(payload: evt));
                break;

            case EngineSdk.Events.ScriptProgress:
                global::Avalonia.Threading.Dispatcher.UIThread.Post(action: () => HandleScriptProgress(payload: evt));
                break;

            case EngineSdk.Events.ScriptActiveEnd:
                global::Avalonia.Threading.Dispatcher.UIThread.Post(action: () => HandleScriptActiveEnd(payload: evt));
                break;

                case EngineSdk.Events.RunAllStart:
                case EngineSdk.Events.RunAllOpStart:
                case EngineSdk.Events.RunAllOpEnd:
                case EngineSdk.Events.RunAllComplete:
                    string seqInfo = FormatEventData(evt: evt);
                    EnqueueLine(line: new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"• {evtType}: {seqInfo}",
                        Type = "info",
                        Color = "DarkGray"
                    });
                    break;

                default:
                    string unknownData = FormatEventData(evt: evt);
                    EnqueueLine(line: new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"[{evtType}] {unknownData}",
                        Type = "unknown",
                        Color = "Gray"
                    });
                    break;
            }
    }

    private void HandleProgressPanelStart(Dictionary<string, object?>? payload) {
        // If the engine provides an id, create a dedicated panel rather than using the shared bottom-panel state.
        string? id = payload?.TryGetValue(key: "id", out object? idObj) == true ? idObj?.ToString() : null;
        if (!string.IsNullOrEmpty(id)) {
            if (_panelsById.ContainsKey(key: id)) return;
            ProgressPanelState panel = new ProgressPanelState { Id = id };
            _panelsById[key: id] = panel;
            TaskPanels.Add(item: panel);
            return;
        }

        // legacy / fallback single shared panel behavior
        ResetProgressPanelTracking();
        ActiveJobs.Clear();
        ActiveJobsSummary = "Active: none";
        ActiveJobCount = 0;
        CurrentSpinner = string.Empty;
        ProgressLabel = string.Empty;
        ProgressSummaryLine = string.Empty;
        ProgressPercent = 0;
        IsProgressPanelActive = true;
    }

    private void HandleProgressPanelUpdate(Dictionary<string, object?> payload) {
        // if payload contains an id, update that specific panel
        string? id = payload.TryGetValue(key: "id", out object? idObj) ? idObj?.ToString() : null;
        ProgressPanelModel model = BuildProgressPanelModel(payload: payload);

        if (!string.IsNullOrEmpty(id)) {
            if (!_panelsById.TryGetValue(key: id, out ProgressPanelState? panel)) {
                panel = new ProgressPanelState { Id = id };
                _panelsById[key: id] = panel;
                TaskPanels.Add(item: panel);
            }
            panel.UpdateFrom(model: model);
            return;
        }

        // legacy single shared panel behavior
        IsProgressPanelActive = true;

        ProgressLabel = model.Label;
        ProgressSummaryLine = model.ProgressLine;
        ProgressPercent = model.Percent;
        CurrentSpinner = model.Spinner;
        ActiveJobCount = model.ActiveTotal;
        ActiveJobsSummary = model.ActiveSummary;

        UpdateActiveJobs(jobs: model.Jobs, spinner: model.Spinner);
        UpdateProgressPanelLines(lines: model.Lines);
    }

    private void HandleProgressPanelEnd(Dictionary<string, object?>? payload) {
        string? id = payload?.TryGetValue(key: "id", out object? idObj) == true ? idObj?.ToString() : null;
        if (!string.IsNullOrEmpty(id)) {
            if (_panelsById.TryGetValue(key: id, out ProgressPanelState? panel)) {
                _panelsById.Remove(key: id);
                TaskPanels.Remove(item: panel);
            }
            return;
        }

        // legacy single shared panel cleanup
        IsProgressPanelActive = false;
        CurrentSpinner = string.Empty;
        ActiveJobCount = 0;
        ActiveJobsSummary = "Active: none";
        ActiveJobs.Clear();
        ResetProgressPanelTracking();
    }

    private void HandleScriptActiveStart(Dictionary<string, object?> payload) {
        _activeScriptName = payload.TryGetValue(key: "name", out object? n) ? n?.ToString() ?? "Script" : "Script";
        //_activeScriptStages = 0;
        //_activeScriptCurrent = 0;
        // Use bottom panel to show script activity even if no progress panel is active
        IsProgressPanelActive = true;
        ProgressLabel = _activeScriptName;
        ProgressSummaryLine = $"Running script: {_activeScriptName}";
        ProgressPercent = 0.0;
        CurrentSpinner = "|";
    }

    private void HandleScriptProgress(Dictionary<string, object?> payload) {
        int total = payload.TryGetValue(key: "total", out object? t) ? SafeToInt(t) : 0;
        int current = payload.TryGetValue(key: "current", out object? c) ? SafeToInt(c) : 0;
        string label = payload.TryGetValue(key: "label", out object? l) ? l?.ToString() ?? string.Empty : string.Empty;

        if (total < 1) total = 1;
        if (current < 0) current = 0;
        if (current > total) current = total;

        //_activeScriptStages = total;
        //_activeScriptCurrent = current;

        IsProgressPanelActive = true;
        ProgressLabel = string.IsNullOrEmpty(label) ? (_activeScriptName.Length > 0 ? _activeScriptName : "Script") : label;
        ProgressSummaryLine = string.IsNullOrEmpty(label)
            ? $"Stage {current}/{total}"
            : $"Stage {current}/{total}: {label}";
        ProgressPercent = System.Math.Clamp(total == 0 ? 0.0 : (double)current / System.Math.Max(val1: 1, val2: total), min: 0.0, max: 1.0);
        CurrentSpinner = "/";
    }

    private void HandleScriptActiveEnd(Dictionary<string, object?> payload) {
        bool success = payload.TryGetValue(key: "success", out object? suc) && suc is bool b && b;
        // Jump to 100% then hide the panel (mirrors requested behavior)
        IsProgressPanelActive = true;
        ProgressPercent = 1.0;
        ProgressSummaryLine = success ? "Script completed successfully" : "Script completed with errors";
        CurrentSpinner = string.Empty;
        // Leave panel visible at 100% until next operation resets/overrides it
        _activeScriptName = string.Empty;
        //_activeScriptStages = 0;
        //_activeScriptCurrent = 0;
    }

    private void UpdateProgressPanelLines(IReadOnlyList<string> lines) {
        if (_progressPanelInsertIndex < 0) {
            _progressPanelInsertIndex = Lines.Count;
            _progressPanelLines.Clear();
        }

        for (int i = 0; i < lines.Count; i++) {
            OutputLine line;
            if (i < _progressPanelLines.Count) {
                line = _progressPanelLines[index: i];
            } else {
                line = new OutputLine {
                    Timestamp = System.DateTime.Now,
                    Type = "progress-panel",
                    Color = i == 0 ? "Cyan" : "Gray"
                };
                _progressPanelLines.Add(item: line);
                int insertIndex = System.Math.Min(val1: _progressPanelInsertIndex + i, val2: Lines.Count);
                Lines.Insert(index: insertIndex, item: line);
            }

            line.Text = lines[index: i];
            line.Color = i == 0 ? "Cyan" : "Gray";
        }

        for (int i = _progressPanelLines.Count - 1; i >= lines.Count; i--) {
            OutputLine toRemove = _progressPanelLines[index: i];
            Lines.Remove(item: toRemove);
            _progressPanelLines.RemoveAt(index: i);
        }
    }

    private void UpdateActiveJobs(IReadOnlyList<ProgressJobSnapshot> jobs, string spinner) {
        int count = jobs.Count;
        for (int i = 0; i < count; i++) {
            ProgressJobSnapshot snapshot = jobs[index: i];
            if (i < ActiveJobs.Count) {
                ActiveJob job = ActiveJobs[index: i];
                job.Spinner = spinner;
                job.Tool = snapshot.Tool;
                job.File = snapshot.File;
                job.Elapsed = snapshot.Elapsed;
            } else {
                ActiveJobs.Add(item: new ActiveJob {
                    Spinner = spinner,
                    Tool = snapshot.Tool,
                    File = snapshot.File,
                    Elapsed = snapshot.Elapsed
                });
            }
        }

        for (int i = ActiveJobs.Count - 1; i >= count; i--) {
            ActiveJobs.RemoveAt(index: i);
        }
    }

    private ProgressPanelModel BuildProgressPanelModel(IReadOnlyDictionary<string, object?> payload) {
        string label = payload.TryGetValue(key: "label", out object? l) ? l?.ToString() ?? "Processing" : "Processing";
        string spinner = payload.TryGetValue(key: "spinner", out object? s) ? s?.ToString() ?? " " : " ";
        int activeTotal = payload.TryGetValue(key: "active_total", out object? at) ? SafeToInt(at) : 0;

        Dictionary<string, object?>? stats = ExtractDictionary(payload: payload, key: "stats");
        int total = stats != null && stats.TryGetValue(key: "total", out object? t) ? SafeToInt(t) : 0;
        int processed = stats != null && stats.TryGetValue(key: "processed", out object? p) ? SafeToInt(p) : 0;
        int ok = stats != null && stats.TryGetValue(key: "ok", out object? o) ? SafeToInt(o) : 0;
        int skip = stats != null && stats.TryGetValue(key: "skip", out object? sk) ? SafeToInt(sk) : 0;
        int err = stats != null && stats.TryGetValue(key: "err", out object? e) ? SafeToInt(e) : 0;
        double percent = stats != null && stats.TryGetValue(key: "percent", out object? pct) ? SafeToDouble(pct) : 0.0;
        percent = System.Math.Clamp(percent, min: 0.0, max: 1.0);

        List<string> lines = new List<string>();
        string progressLine;
        if (stats != null) {
            int width = 30;
            int filled = (int)System.Math.Round(a: percent * width);
            System.Text.StringBuilder bar = new System.Text.StringBuilder(capacity: width + 64);
            bar.Append(label);
            bar.Append(' ');
            bar.Append('[');
            for (int i = 0; i < width; i++) {
                bar.Append(i < filled ? '#' : '-');
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
            progressLine = bar.ToString();
        } else {
            progressLine = label;
        }
        lines.Add(item: progressLine);

        string activeSummary = activeTotal == 0 ? "Active: none" : $"Active: {activeTotal}";
        lines.Add(item: activeSummary);

        List<ProgressJobSnapshot> jobs = new List<ProgressJobSnapshot>();
        if (payload.TryGetValue(key: "active_jobs", out object? aj) && aj is System.Collections.IEnumerable enumerable) {
            foreach (object? jobObj in enumerable) {
                switch (jobObj) {
                    case Dictionary<string, object?> dictJob:
                        jobs.Add(item: ToSnapshot(job: dictJob));
                        break;
                    case IReadOnlyDictionary<string, object?> readOnlyJob:
                        jobs.Add(item: ToSnapshot(job: readOnlyJob.ToDictionary(keySelector: kv => kv.Key, elementSelector: kv => kv.Value)));
                        break;
                    case System.Text.Json.JsonElement element when element.ValueKind == System.Text.Json.JsonValueKind.Object: {
                        Dictionary<string, object?> parsed = new Dictionary<string, object?>(comparer: System.StringComparer.Ordinal);
                        foreach (System.Text.Json.JsonProperty prop in element.EnumerateObject()) {
                            parsed[key: prop.Name] = prop.Value.ValueKind == System.Text.Json.JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
                        }
                        jobs.Add(item: ToSnapshot(job: parsed));
                        break;
                    }
                }
            }
        }

        foreach (ProgressJobSnapshot job in jobs) {
            lines.Add(item: $"  {spinner} {job.Tool} · {job.File} · {job.Elapsed}");
        }

        if (activeTotal > jobs.Count) {
            lines.Add(item: $"  … and {activeTotal - jobs.Count} more");
        }

        return new ProgressPanelModel {
            Label = label,
            Spinner = spinner,
            Percent = percent,
            ProgressLine = progressLine,
            ActiveSummary = activeSummary,
            ActiveTotal = activeTotal,
            Jobs = jobs,
            Lines = lines
        };
    }

    private static ProgressJobSnapshot ToSnapshot(Dictionary<string, object?> job) {
        string tool = job.TryGetValue(key: "tool", out object? t) ? t?.ToString() ?? "..." : "...";
        string file = job.TryGetValue(key: "file", out object? f) ? f?.ToString() ?? "..." : "...";
        string elapsed = job.TryGetValue(key: "elapsed", out object? e) ? e?.ToString() ?? "..." : "...";

        file = Truncate(file, max: 80);

        return new ProgressJobSnapshot(Tool: tool, File: file, Elapsed: elapsed);
    }

    private static Dictionary<string, object?>? ExtractDictionary(IReadOnlyDictionary<string, object?> payload, string key) {
        if ((!payload.TryGetValue(key: key, out object? value) || value is null) || (value is not System.Text.Json.JsonElement element || element.ValueKind != System.Text.Json.JsonValueKind.Object)) {
            return null;
        }

        switch (value) {
            case Dictionary<string, object?> dict:
                return dict;
            case IReadOnlyDictionary<string, object?> readOnly:
                return readOnly.ToDictionary(keySelector: kv => kv.Key, elementSelector: kv => kv.Value);
        }

        Dictionary<string, object?> parsed = new Dictionary<string, object?>(comparer: System.StringComparer.Ordinal);
        foreach (System.Text.Json.JsonProperty prop in element.EnumerateObject()) {
            parsed[key: prop.Name] = prop.Value.ValueKind switch {
                System.Text.Json.JsonValueKind.Number => prop.Value.GetDouble(),
                System.Text.Json.JsonValueKind.String => prop.Value.GetString(),
                _ => prop.Value.ToString()
            };
        }
        return parsed;

    }

    /// <summary>
    /// Add a status line (used internally).
    /// </summary>
    private void AddLine(string text, string type, string color = "Gray") {
        Lines.Add(item: new OutputLine {
            Timestamp = System.DateTime.Now,
            Text = text,
            Type = type,
            Color = color
        });
    }

    private void ResetProgressPanelTracking() {
        _progressPanelLines.Clear();
        _progressPanelInsertIndex = -1;
    }

    /// <summary>
    /// Map color names to Avalonia color names.
    /// </summary>
    private static string MapColor(string? colorName) {
        if (string.IsNullOrWhiteSpace(colorName)) {
            return "Gray";
        }

        return colorName.ToLowerInvariant() switch {
            "cyan" => "Cyan",
            "red" => "Red",
            "green" => "Green",
            "yellow" => "Yellow",
            "blue" => "Blue",
            "magenta" => "Magenta",
            "white" => "White",
            "black" => "Black",
            "darkgray" or "darkgrey" => "DarkGray",
            "darkred" => "DarkRed",
            "darkgreen" => "DarkGreen",
            "darkcyan" => "DarkCyan",
            "darkblue" => "DarkBlue",
            "darkmagenta" => "DarkMagenta",
            "darkyellow" => "DarkYellow",
            _ => "Gray"
        };
    }

    private static string FormatEventData(Dictionary<string, object?> evt) {
        List<string> parts = new List<string>();
        foreach (KeyValuePair<string, object?> kv in evt) {
            if (kv.Key.Equals("event", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            parts.Add(item: $"{kv.Key}={kv.Value}");
        }
        return string.Join(separator: ", ", values: parts);
    }

    private static int SafeToInt(object? value) {
        if (value is null) {
            return 0;
        }

        if (value is int i) {
            return i;
        }

        if (value is long l) {
            return (int)l;
        }

        if (value is double d) {
            return (int)System.Math.Round(a: d);
        }

        if (value is System.IConvertible convertible) {
            return convertible.ToInt32(provider: null);
        }

        if (value is string s && int.TryParse(s: s, result: out int parsed)) {
            return parsed;
        }

        return 0;
    }

    private static double SafeToDouble(object? value) {
        if (value is null) {
            return 0.0;
        }

        if (value is double d) {
            return d;
        }

        if (value is float f) {
            return f;
        }

        if (value is int i) {
            return i;
        }

        if (value is long l) {
            return l;
        }

        if (value is System.IConvertible convertible) {
            return convertible.ToDouble(provider: null);
        }

        if (value is string s && double.TryParse(s: s, result: out double parsed)) {
            return parsed;
        }

        return 0.0;
    }

    private static string Truncate(string value, int max) {
        if (string.IsNullOrEmpty(value) || value.Length <= max) {
            return value;
        }

        return value[..System.Math.Max(val1: 0, val2: max - 1)] + "…";
    }

    /// <summary>
    /// Clear all output.
    /// </summary>
    public async System.Threading.Tasks.Task ClearAsync() {
        await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(callback: () => {
            Lines.Clear();
            ActiveJobs.Clear();
            ResetProgressPanelTracking();
            ActiveJobCount = 0;
            ActiveJobsSummary = "Active: none";
            CurrentSpinner = string.Empty;
            ProgressLabel = string.Empty;
            ProgressSummaryLine = string.Empty;
            ProgressPercent = 0;
            IsProgressPanelActive = false;
            CurrentOperation = null;
        });
    }

    // --- Prompt / Popup State ---
    public bool IsPromptActive {
        get;
        private set => SetField(field: ref field, value);
    }

    public string? PromptTitle {
        get;
        private set => SetField(field: ref field, value);
    }

    public string? PromptMessage {
        get;
        private set => SetField(field: ref field, value);
    }

    public string PromptValue {
        get;
        set => SetField(field: ref field, value);
    } = string.Empty;

    public bool IsConfirmPrompt {
        get;
        private set {
            if (SetField(field: ref field, value)) {
                OnPropertyChanged(propertyName: nameof(IsTextPrompt));
            }
        }
    }

    public bool IsTextPrompt => !IsConfirmPrompt;

    public bool IsSecret {
        get;
        private set => SetField(field: ref field, value);
    }

    private System.Threading.Tasks.TaskCompletionSource<string?>? _promptTcs;

    public async System.Threading.Tasks.Task<string?> RequestTextPromptAsync(string title, string message, string? defaultValue, bool secret) {
        return await Dispatcher.UIThread.InvokeAsync(action: async () => {
            PromptTitle = title;
            PromptMessage = message;
            PromptValue = defaultValue ?? "";
            IsSecret = secret;
            IsConfirmPrompt = false;
            IsPromptActive = true;

            _promptTcs = new System.Threading.Tasks.TaskCompletionSource<string?>();
            return await _promptTcs.Task;
        });
    }

    public async System.Threading.Tasks.Task<bool?> RequestConfirmPromptAsync(string title, string message, bool defaultValue) {
        return await Dispatcher.UIThread.InvokeAsync<bool?>(action: async () => {
            PromptTitle = title;
            PromptMessage = message;
            IsConfirmPrompt = true;
            IsPromptActive = true;
            PromptValue = "";

            _promptTcs = new System.Threading.Tasks.TaskCompletionSource<string?>();
            string? res = await _promptTcs.Task;
            if (res == "y") return true;
            if (res == "n") return false;
            return null;
        });
    }

    public void SubmitPrompt() {
        IsPromptActive = false;
        if (IsConfirmPrompt) {
             _promptTcs?.TrySetResult(result: "y");
        } else {
             _promptTcs?.TrySetResult(result: PromptValue);
        }
    }

    public void SubmitNoPrompt() {
        IsPromptActive = false;
        _promptTcs?.TrySetResult(result: "n");
    }

    public void CancelPrompt() {
        IsPromptActive = false;
        _promptTcs?.TrySetResult(result: null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) {
        if (EqualityComparer<T>.Default.Equals(x: field, y: value)) {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName: propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(sender: this, e: new PropertyChangedEventArgs(propertyName: propertyName));
    }
}
