using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Threading;

namespace EngineNet.Interface.GUI;

/// <summary>
/// Shared service for capturing and displaying operation output across all GUI pages.
/// This ensures that output persists when navigating between pages.
/// </summary>
public sealed class OperationOutputService : INotifyPropertyChanged {
    public static OperationOutputService Instance { get; } = new OperationOutputService();

    private readonly object _lock = new object();

    public OperationOutputService() {
        Lines.CollectionChanged += OnLinesCollectionChanged;
    }

    /// <summary>
    /// Shared output lines collection. Thread-safe via Dispatcher.
    /// </summary>
    public ObservableCollection<OutputLine> Lines { get; } = new ObservableCollection<OutputLine>();

    private readonly HashSet<OutputLine> _trackedLines = new HashSet<OutputLine>();
    private string _fullLogText = string.Empty;
    private bool _isFullLogDirty = true;

    /// <summary>
    /// Combined output text for log selection and clipboard copy.
    /// </summary>
    public string FullLogText {
        get {
            if (_isFullLogDirty) {
                _fullLogText = BuildFullLogText();
                _isFullLogDirty = false;
            }

            return _fullLogText;
        }
    }

    public ObservableCollection<ActiveJob> ActiveJobs { get; } = new ObservableCollection<ActiveJob>();

    private string? _currentOperation;
    public string? CurrentOperation {
        get {
            lock (_lock) {
                return _currentOperation;
            }
        }
        private set {
            lock (_lock) {
                if (!EqualityComparer<string?>.Default.Equals(_currentOperation, value)) {
                    _currentOperation = value;
                    OnPropertyChanged();
                }
            }
        }
    }

    private bool _isProgressPanelActive;
    public bool IsProgressPanelActive {
        get => _isProgressPanelActive;
        private set => SetField(ref _isProgressPanelActive, value);
    }

    private string _progressLabel = string.Empty;
    public string ProgressLabel {
        get => _progressLabel;
        private set => SetField(ref _progressLabel, value);
    }

    private string _progressSummaryLine = string.Empty;
    public string ProgressSummaryLine {
        get => _progressSummaryLine;
        private set => SetField(ref _progressSummaryLine, value);
    }

    private double _progressPercent;
    public double ProgressPercent {
        get => _progressPercent;
        private set => SetField(ref _progressPercent, value);
    }

    // Script activity tracking (stage-based indicator)
    private string _activeScriptName = string.Empty;
    private int _activeScriptStages = 0;
    private int _activeScriptCurrent = 0;

    private string _activeJobsSummary = "Active: none";
    public string ActiveJobsSummary {
        get => _activeJobsSummary;
        private set => SetField(ref _activeJobsSummary, value);
    }

    private int _activeJobCount;
    public int ActiveJobCount {
        get => _activeJobCount;
        private set => SetField(ref _activeJobCount, value);
    }

    private string _currentSpinner = string.Empty;
    public string CurrentSpinner {
        get => _currentSpinner;
        private set => SetField(ref _currentSpinner, value);
    }

    private readonly List<OutputLine> _progressPanelLines = new List<OutputLine>();
    private int _progressPanelInsertIndex = -1;

    /// <summary>
    /// Clear output and start a new operation.
    /// </summary>
    internal void StartOperation(string operationName, string gameName) {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
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
            AddLine($"=== Starting: {operationName} for {gameName} ===", "header");
        });
    }

    // --- High-volume output buffering/throttling state ---
    private readonly System.Collections.Concurrent.ConcurrentQueue<OutputLine> _pendingLines = new System.Collections.Concurrent.ConcurrentQueue<OutputLine>();
    private DispatcherTimer? _flushTimer;
    private readonly object _flushLock = new object();
    private const int FlushBatchMax = 250;
    private const int MaxLines = 5000;
    private const int MaxChars = 200_000;
    private int _currentChars = 0;

    /// <summary>
    /// Add a raw output line (buffered + throttled for UI responsiveness).
    /// </summary>
    internal void AddOutput(string text, string stream = "stdout") {
        OutputLine line = new OutputLine {
            Timestamp = System.DateTime.Now,
            Text = text,
            Type = stream == "stderr" ? "error" : "output",
            Color = stream == "stderr" ? "Red" : "Gray"
        };
        EnqueueLine(line);
    }

    private void EnqueueLine(OutputLine line) {
        _pendingLines.Enqueue(line);
        EnsureFlushTimer();
    }

    private void EnsureFlushTimer() {
        lock (_flushLock) {
            if (_flushTimer is null) {
                _flushTimer = new DispatcherTimer() { Interval = System.TimeSpan.FromMilliseconds(33) };
                _flushTimer.Tick += FlushPending;
                _flushTimer.Start();
            } else if (!_flushTimer.IsEnabled) {
                _flushTimer.Start();
            }
        }
    }

    private void FlushPending(object? sender, System.EventArgs e) {
        Dispatcher.UIThread.VerifyAccess();
        int processed = 0;
        while (processed < FlushBatchMax && _pendingLines.TryDequeue(out OutputLine? line)) {
            Lines.Add(line);
            _currentChars += line.Text?.Length ?? 0;
            processed++;
        }
        TrimIfNeeded();
        if (_pendingLines.IsEmpty && processed == 0) {
            lock (_flushLock) { _flushTimer?.Stop(); }
        }
    }

    private void TrimIfNeeded() {
        int removed = 0;
        while (Lines.Count > MaxLines) {
            OutputLine first = Lines[0];
            _currentChars -= first.Text?.Length ?? 0;
            Lines.RemoveAt(0);
            removed++;
        }
        if (_currentChars > MaxChars) {
            int idx = 0;
            while (_currentChars > MaxChars && idx < Lines.Count) {
                OutputLine first = Lines[idx];
                _currentChars -= first.Text?.Length ?? 0;
                Lines.RemoveAt(idx);
                removed++;
            }
        }
        if (removed > 0 && _progressPanelInsertIndex >= 0) {
            _progressPanelInsertIndex = System.Math.Max(0, _progressPanelInsertIndex - removed);
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
                if (item is OutputLine line && _trackedLines.Remove(line)) {
                    line.PropertyChanged -= OnLinePropertyChanged;
                }
            }
        }

        if (e.NewItems != null) {
            foreach (object? item in e.NewItems) {
                if (item is OutputLine line && _trackedLines.Add(line)) {
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
        OnPropertyChanged(nameof(FullLogText));
    }

    private string BuildFullLogText() {
        if (Lines.Count == 0) {
            return string.Empty;
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder(_currentChars + Lines.Count);
        for (int i = 0; i < Lines.Count; i++) {
            if (i > 0) {
                builder.Append('\n');
            }
            builder.Append(Lines[i].Text);
        }
        return builder.ToString();
    }

    /// <summary>
    /// Handle a structured event from the engine.
    /// </summary>
    internal void HandleEvent(Dictionary<string, object?> evt) {
        if (!evt.TryGetValue("event", out object? evtTypeObj)) {
            return;
        }

        string? evtType = evtTypeObj?.ToString();

        switch (evtType) {
            case "print":
                string msg = evt.TryGetValue("message", out object? m) ? m?.ToString() ?? string.Empty : string.Empty;
                string color = evt.TryGetValue("color", out object? c) ? c?.ToString() ?? "Gray" : "Gray";
                EnqueueLine(new OutputLine {
                    Timestamp = System.DateTime.Now,
                    Text = msg,
                    Type = "print",
                    Color = MapColor(color)
                });
                break;

                case "warning":
                    string warnMsg = evt.TryGetValue("message", out object? wm) ? wm?.ToString() ?? string.Empty : string.Empty;
                    EnqueueLine(new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"⚠ {warnMsg}",
                        Type = "warning",
                        Color = "Yellow"
                    });
                    break;

                case "error":
                    string errMsg = evt.TryGetValue("message", out object? em) ? em?.ToString() ?? string.Empty : string.Empty;
                    EnqueueLine(new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"✖ {errMsg}",
                        Type = "error",
                        Color = "Red"
                    });
                    break;

                case "prompt":
                    string promptMsg = evt.TryGetValue("message", out object? pm) ? pm?.ToString() ?? string.Empty : string.Empty;
                    EnqueueLine(new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"? {promptMsg}",
                        Type = "prompt",
                        Color = "Cyan"
                    });
                    break;

                case "progress":
                    int current = evt.TryGetValue("current", out object? cur) ? SafeToInt(cur) : 0;
                    int total = evt.TryGetValue("total", out object? tot) ? SafeToInt(tot) : 0;
                    string label = evt.TryGetValue("label", out object? lbl) ? lbl?.ToString() ?? string.Empty : string.Empty;
                    EnqueueLine(new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"[{current}/{total}] {label}",
                        Type = "progress",
                        Color = "Cyan"
                    });
                    break;

                case "start":
                    string startContext = FormatEventData(evt);
                    EnqueueLine(new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"▶ Started: {startContext}",
                        Type = "start",
                        Color = "Green"
                    });
                    break;

                case "end":
                    bool success = evt.TryGetValue("success", out object? suc) && suc is bool b && b;
                    EnqueueLine(new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = success ? "✓ Completed successfully" : "✗ Completed with errors",
                        Type = "end",
                        Color = success ? "Green" : "Red"
                    });
                    break;

            case "progress_panel_start":
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => HandleProgressPanelStart());
                break;

            case "progress_panel":
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => HandleProgressPanelUpdate(evt));
                break;

            case "progress_panel_end":
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => HandleProgressPanelEnd());
                break;

            case "script_active_start":
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => HandleScriptActiveStart(evt));
                break;

            case "script_progress":
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => HandleScriptProgress(evt));
                break;

            case "script_active_end":
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => HandleScriptActiveEnd(evt));
                break;

                case "run-all-start":
                case "run-all-op-start":
                case "run-all-op-end":
                case "run-all-complete":
                    string seqInfo = FormatEventData(evt);
                    EnqueueLine(new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"• {evtType}: {seqInfo}",
                        Type = "info",
                        Color = "DarkGray"
                    });
                    break;

                default:
                    string unknownData = FormatEventData(evt);
                    EnqueueLine(new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"[{evtType}] {unknownData}",
                        Type = "unknown",
                        Color = "Gray"
                    });
                    break;
            }
    }

    private void HandleProgressPanelStart() {
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
        IsProgressPanelActive = true;

        ProgressPanelModel model = BuildProgressPanelModel(payload);

        ProgressLabel = model.Label;
        ProgressSummaryLine = model.ProgressLine;
        ProgressPercent = model.Percent;
        CurrentSpinner = model.Spinner;
        ActiveJobCount = model.ActiveTotal;
        ActiveJobsSummary = model.ActiveSummary;

        UpdateActiveJobs(model.Jobs, model.Spinner);
        UpdateProgressPanelLines(model.Lines);
    }

    private void HandleProgressPanelEnd() {
        IsProgressPanelActive = false;
        CurrentSpinner = string.Empty;
        ActiveJobCount = 0;
        ActiveJobsSummary = "Active: none";
        ActiveJobs.Clear();
        ResetProgressPanelTracking();
    }

    private void HandleScriptActiveStart(Dictionary<string, object?> payload) {
        _activeScriptName = payload.TryGetValue("name", out object? n) ? n?.ToString() ?? "Script" : "Script";
        _activeScriptStages = 0;
        _activeScriptCurrent = 0;
        // Use bottom panel to show script activity even if no progress panel is active
        IsProgressPanelActive = true;
        ProgressLabel = _activeScriptName;
        ProgressSummaryLine = $"Running script: {_activeScriptName}";
        ProgressPercent = 0.0;
        CurrentSpinner = "|";
    }

    private void HandleScriptProgress(Dictionary<string, object?> payload) {
        int total = payload.TryGetValue("total", out object? t) ? SafeToInt(t) : 0;
        int current = payload.TryGetValue("current", out object? c) ? SafeToInt(c) : 0;
        string label = payload.TryGetValue("label", out object? l) ? l?.ToString() ?? string.Empty : string.Empty;

        if (total < 1) total = 1;
        if (current < 0) current = 0;
        if (current > total) current = total;

        _activeScriptStages = total;
        _activeScriptCurrent = current;

        IsProgressPanelActive = true;
        ProgressLabel = string.IsNullOrEmpty(label) ? (_activeScriptName.Length > 0 ? _activeScriptName : "Script") : label;
        ProgressSummaryLine = string.IsNullOrEmpty(label)
            ? $"Stage {current}/{total}"
            : $"Stage {current}/{total}: {label}";
        ProgressPercent = System.Math.Clamp(total == 0 ? 0.0 : (double)current / System.Math.Max(1, total), 0.0, 1.0);
        CurrentSpinner = "/";
    }

    private void HandleScriptActiveEnd(Dictionary<string, object?> payload) {
        bool success = payload.TryGetValue("success", out object? suc) && suc is bool b && b;
        // Jump to 100% then hide the panel (mirrors requested behavior)
        IsProgressPanelActive = true;
        ProgressPercent = 1.0;
        ProgressSummaryLine = success ? "Script completed successfully" : "Script completed with errors";
        CurrentSpinner = string.Empty;
        // Leave panel visible at 100% until next operation resets/overrides it
        _activeScriptName = string.Empty;
        _activeScriptStages = 0;
        _activeScriptCurrent = 0;
    }

    private void UpdateProgressPanelLines(IReadOnlyList<string> lines) {
        if (_progressPanelInsertIndex < 0) {
            _progressPanelInsertIndex = Lines.Count;
            _progressPanelLines.Clear();
        }

        for (int i = 0; i < lines.Count; i++) {
            OutputLine line;
            if (i < _progressPanelLines.Count) {
                line = _progressPanelLines[i];
            } else {
                line = new OutputLine {
                    Timestamp = System.DateTime.Now,
                    Type = "progress-panel",
                    Color = i == 0 ? "Cyan" : "Gray"
                };
                _progressPanelLines.Add(line);
                int insertIndex = System.Math.Min(_progressPanelInsertIndex + i, Lines.Count);
                Lines.Insert(insertIndex, line);
            }

            line.Text = lines[i];
            line.Color = i == 0 ? "Cyan" : "Gray";
        }

        for (int i = _progressPanelLines.Count - 1; i >= lines.Count; i--) {
            OutputLine toRemove = _progressPanelLines[i];
            Lines.Remove(toRemove);
            _progressPanelLines.RemoveAt(i);
        }
    }

    private void UpdateActiveJobs(IReadOnlyList<ProgressJobSnapshot> jobs, string spinner) {
        int count = jobs.Count;
        for (int i = 0; i < count; i++) {
            ProgressJobSnapshot snapshot = jobs[i];
            if (i < ActiveJobs.Count) {
                ActiveJob job = ActiveJobs[i];
                job.Spinner = spinner;
                job.Tool = snapshot.Tool;
                job.File = snapshot.File;
                job.Elapsed = snapshot.Elapsed;
            } else {
                ActiveJobs.Add(new ActiveJob {
                    Spinner = spinner,
                    Tool = snapshot.Tool,
                    File = snapshot.File,
                    Elapsed = snapshot.Elapsed
                });
            }
        }

        for (int i = ActiveJobs.Count - 1; i >= count; i--) {
            ActiveJobs.RemoveAt(i);
        }
    }

    private ProgressPanelModel BuildProgressPanelModel(IReadOnlyDictionary<string, object?> payload) {
        string label = payload.TryGetValue("label", out object? l) ? l?.ToString() ?? "Processing" : "Processing";
        string spinner = payload.TryGetValue("spinner", out object? s) ? s?.ToString() ?? " " : " ";
        int activeTotal = payload.TryGetValue("active_total", out object? at) ? SafeToInt(at) : 0;

        Dictionary<string, object?>? stats = ExtractDictionary(payload, "stats");
        int total = stats != null && stats.TryGetValue("total", out object? t) ? SafeToInt(t) : 0;
        int processed = stats != null && stats.TryGetValue("processed", out object? p) ? SafeToInt(p) : 0;
        int ok = stats != null && stats.TryGetValue("ok", out object? o) ? SafeToInt(o) : 0;
        int skip = stats != null && stats.TryGetValue("skip", out object? sk) ? SafeToInt(sk) : 0;
        int err = stats != null && stats.TryGetValue("err", out object? e) ? SafeToInt(e) : 0;
        double percent = stats != null && stats.TryGetValue("percent", out object? pct) ? SafeToDouble(pct) : 0.0;
        percent = System.Math.Clamp(percent, 0.0, 1.0);

        List<string> lines = new List<string>();
        string progressLine;
        if (stats != null) {
            int width = 30;
            int filled = (int)System.Math.Round(percent * width);
            System.Text.StringBuilder bar = new System.Text.StringBuilder(width + 64);
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
            progressLine = bar.ToString();
        } else {
            progressLine = label;
        }
        lines.Add(progressLine);

        string activeSummary = activeTotal == 0 ? "Active: none" : $"Active: {activeTotal}";
        lines.Add(activeSummary);

        List<ProgressJobSnapshot> jobs = new List<ProgressJobSnapshot>();
        if (payload.TryGetValue("active_jobs", out object? aj) && aj is System.Collections.IEnumerable enumerable) {
            foreach (object? jobObj in enumerable) {
                if (jobObj is Dictionary<string, object?> dictJob) {
                    jobs.Add(ToSnapshot(dictJob));
                } else if (jobObj is IReadOnlyDictionary<string, object?> readOnlyJob) {
                    jobs.Add(ToSnapshot(readOnlyJob.ToDictionary(kv => kv.Key, kv => kv.Value)));
                } else if (jobObj is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.Object) {
                    Dictionary<string, object?> parsed = new Dictionary<string, object?>(System.StringComparer.Ordinal);
                    foreach (System.Text.Json.JsonProperty prop in element.EnumerateObject()) {
                        parsed[prop.Name] = prop.Value.ValueKind == System.Text.Json.JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
                    }
                    jobs.Add(ToSnapshot(parsed));
                }
            }
        }

        foreach (ProgressJobSnapshot job in jobs) {
            lines.Add($"  {spinner} {job.Tool} · {job.File} · {job.Elapsed}");
        }

        if (activeTotal > jobs.Count) {
            lines.Add($"  … and {activeTotal - jobs.Count} more");
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
        string tool = job.TryGetValue("tool", out object? t) ? t?.ToString() ?? "..." : "...";
        string file = job.TryGetValue("file", out object? f) ? f?.ToString() ?? "..." : "...";
        string elapsed = job.TryGetValue("elapsed", out object? e) ? e?.ToString() ?? "..." : "...";

        file = Truncate(file, 80);

        return new ProgressJobSnapshot(tool, file, elapsed);
    }

    private static Dictionary<string, object?>? ExtractDictionary(IReadOnlyDictionary<string, object?> payload, string key) {
        if (!payload.TryGetValue(key, out object? value) || value is null) {
            return null;
        }

        if (value is Dictionary<string, object?> dict) {
            return dict;
        }

        if (value is IReadOnlyDictionary<string, object?> readOnly) {
            return readOnly.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        if (value is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.Object) {
            Dictionary<string, object?> parsed = new Dictionary<string, object?>(System.StringComparer.Ordinal);
            foreach (System.Text.Json.JsonProperty prop in element.EnumerateObject()) {
                parsed[prop.Name] = prop.Value.ValueKind == System.Text.Json.JsonValueKind.Number
                    ? prop.Value.GetDouble()
                    : prop.Value.ValueKind == System.Text.Json.JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.ToString();
            }
            return parsed;
        }

        return null;
    }

    /// <summary>
    /// Add a status line (used internally).
    /// </summary>
    private void AddLine(string text, string type, string color = "Gray") {
        Lines.Add(new OutputLine {
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
        var parts = new List<string>();
        foreach (KeyValuePair<string, object?> kv in evt) {
            if (kv.Key.Equals("event", System.StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            parts.Add($"{kv.Key}={kv.Value}");
        }
        return string.Join(", ", parts);
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
            return (int)System.Math.Round(d);
        }

        if (value is System.IConvertible convertible) {
            try {
                return convertible.ToInt32(null);
            } catch { }
        }

        if (value is string s && int.TryParse(s, out int parsed)) {
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
            try {
                return convertible.ToDouble(null);
            } catch { }
        }

        if (value is string s && double.TryParse(s, out double parsed)) {
            return parsed;
        }

        return 0.0;
    }

    private static string Truncate(string value, int max) {
        if (string.IsNullOrEmpty(value) || value.Length <= max) {
            return value;
        }

        return value[..System.Math.Max(0, max - 1)] + "…";
    }

    /// <summary>
    /// Clear all output.
    /// </summary>
    internal void Clear() {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
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

    /// <summary>
    /// Clear all output.
    /// </summary>
    internal async System.Threading.Tasks.Task ClearAsync() {
        await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
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
    private bool _isPromptActive;
    public bool IsPromptActive {
        get => _isPromptActive;
        private set => SetField(ref _isPromptActive, value);
    }

    private string _promptTitle = string.Empty;
    public string PromptTitle {
        get => _promptTitle;
        private set => SetField(ref _promptTitle, value);
    }

    private string _promptMessage = string.Empty;
    public string PromptMessage {
        get => _promptMessage;
        private set => SetField(ref _promptMessage, value);
    }

    private string _promptValue = string.Empty;
    public string PromptValue {
        get => _promptValue;
        set => SetField(ref _promptValue, value);
    }

    private bool _isConfirmPrompt;
    public bool IsConfirmPrompt {
        get => _isConfirmPrompt;
        private set {
            if (SetField(ref _isConfirmPrompt, value)) {
                OnPropertyChanged(nameof(IsTextPrompt));
            }
        }
    }

    public bool IsTextPrompt => !IsConfirmPrompt;

    private bool _isSecret;
    public bool IsSecret {
        get => _isSecret;
        private set => SetField(ref _isSecret, value);
    }

    private System.Threading.Tasks.TaskCompletionSource<string?>? _promptTcs;

    public async System.Threading.Tasks.Task<string?> RequestTextPromptAsync(string title, string message, string? defaultValue, bool secret) {
        return await Dispatcher.UIThread.InvokeAsync(async () => {
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

    public async System.Threading.Tasks.Task<bool> RequestConfirmPromptAsync(string title, string message, bool defaultValue) {
        return await Dispatcher.UIThread.InvokeAsync(async () => {
            PromptTitle = title;
            PromptMessage = message;
            IsConfirmPrompt = true;
            IsPromptActive = true;
            PromptValue = "";

            _promptTcs = new System.Threading.Tasks.TaskCompletionSource<string?>();
            string? res = await _promptTcs.Task;
            return res == "y";
        });
    }

    public void SubmitPrompt() {
        IsPromptActive = false;
        if (IsConfirmPrompt) {
             _promptTcs?.TrySetResult("y");
        } else {
             _promptTcs?.TrySetResult(PromptValue);
        }
    }

    public void CancelPrompt() {
        IsPromptActive = false;
        if (IsConfirmPrompt) {
             _promptTcs?.TrySetResult("n");
        } else {
             _promptTcs?.TrySetResult(null);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) {
        if (EqualityComparer<T>.Default.Equals(field, value)) {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed record ProgressPanelModel {
        internal required string Label { get; init; }
        internal required string Spinner { get; init; }
        internal required double Percent { get; init; }
        internal required string ProgressLine { get; init; }
        internal required string ActiveSummary { get; init; }
        internal required int ActiveTotal { get; init; }
        internal required List<ProgressJobSnapshot> Jobs { get; init; }
        internal required List<string> Lines { get; init; }
    }

    private readonly record struct ProgressJobSnapshot(string Tool, string File, string Elapsed);
}

/// <summary>
/// Represents a single line of output in the operation log.
/// </summary>
public class OutputLine : INotifyPropertyChanged {
    private string _text = string.Empty;
    private string _color = "Gray";

    public System.DateTime Timestamp { get; set; }

    public string Text {
        get => _text;
        set {
            if (_text != value) {
                _text = value;
                OnPropertyChanged(nameof(Text));
            }
        }
    }

    public string Type { get; set; } = "output";

    public string Color {
        get => _color;
        set {
            if (_color != value) {
                _color = value;
                OnPropertyChanged(nameof(Color));
            }
        }
    }

    public string FormattedTime => Timestamp.ToString("HH:mm:ss");

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class ActiveJob : INotifyPropertyChanged {
    private string _spinner = string.Empty;
    private string _tool = string.Empty;
    private string _file = string.Empty;
    private string _elapsed = string.Empty;

    public string Spinner {
        get => _spinner;
        set {
            if (_spinner != value) {
                _spinner = value;
                OnPropertyChanged(nameof(Spinner));
                OnPropertyChanged(nameof(Display));
            }
        }
    }

    public string Tool {
        get => _tool;
        set {
            if (_tool != value) {
                _tool = value;
                OnPropertyChanged(nameof(Tool));
                OnPropertyChanged(nameof(Display));
            }
        }
    }

    public string File {
        get => _file;
        set {
            if (_file != value) {
                _file = value;
                OnPropertyChanged(nameof(File));
                OnPropertyChanged(nameof(Display));
            }
        }
    }

    public string Elapsed {
        get => _elapsed;
        set {
            if (_elapsed != value) {
                _elapsed = value;
                OnPropertyChanged(nameof(Elapsed));
                OnPropertyChanged(nameof(Display));
            }
        }
    }

    public string Display => $"{Spinner} {Tool} · {File} · {Elapsed}";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
