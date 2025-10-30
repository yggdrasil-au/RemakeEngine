using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace EngineNet.Interface.GUI;

/// <summary>
/// Shared service for capturing and displaying operation output across all GUI pages.
/// This ensures that output persists when navigating between pages.
/// </summary>
internal sealed class OperationOutputService : INotifyPropertyChanged {
    internal static OperationOutputService Instance { get; } = new OperationOutputService();

    private readonly object _lock = new object();

    internal OperationOutputService() { }

    /// <summary>
    /// Shared output lines collection. Thread-safe via Dispatcher.
    /// </summary>
    internal ObservableCollection<OutputLine> Lines { get; } = new ObservableCollection<OutputLine>();

    internal ObservableCollection<ActiveJob> ActiveJobs { get; } = new ObservableCollection<ActiveJob>();

    private string? _currentOperation;
    internal string? CurrentOperation {
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
    internal bool IsProgressPanelActive {
        get => _isProgressPanelActive;
        private set => SetField(ref _isProgressPanelActive, value);
    }

    private string _progressLabel = string.Empty;
    internal string ProgressLabel {
        get => _progressLabel;
        private set => SetField(ref _progressLabel, value);
    }

    private string _progressSummaryLine = string.Empty;
    internal string ProgressSummaryLine {
        get => _progressSummaryLine;
        private set => SetField(ref _progressSummaryLine, value);
    }

    private double _progressPercent;
    internal double ProgressPercent {
        get => _progressPercent;
        private set => SetField(ref _progressPercent, value);
    }

    private string _activeJobsSummary = "Active: none";
    internal string ActiveJobsSummary {
        get => _activeJobsSummary;
        private set => SetField(ref _activeJobsSummary, value);
    }

    private int _activeJobCount;
    internal int ActiveJobCount {
        get => _activeJobCount;
        private set => SetField(ref _activeJobCount, value);
    }

    private string _currentSpinner = string.Empty;
    internal string CurrentSpinner {
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

    /// <summary>
    /// Add a raw output line.
    /// </summary>
    internal void AddOutput(string text, string stream = "stdout") {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            Lines.Add(new OutputLine {
                Timestamp = System.DateTime.Now,
                Text = text,
                Type = stream == "stderr" ? "error" : "output",
                Color = stream == "stderr" ? "Red" : "Gray"
            });
        });
    }

    /// <summary>
    /// Handle a structured event from the engine.
    /// </summary>
    internal void HandleEvent(Dictionary<string, object?> evt) {
        if (!evt.TryGetValue("event", out object? evtTypeObj)) {
            return;
        }

        string? evtType = evtTypeObj?.ToString();

        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            switch (evtType) {
                case "print":
                    string msg = evt.TryGetValue("message", out object? m) ? m?.ToString() ?? string.Empty : string.Empty;
                    string color = evt.TryGetValue("color", out object? c) ? c?.ToString() ?? "Gray" : "Gray";
                    Lines.Add(new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = msg,
                        Type = "print",
                        Color = MapColor(color)
                    });
                    break;

                case "warning":
                    string warnMsg = evt.TryGetValue("message", out object? wm) ? wm?.ToString() ?? string.Empty : string.Empty;
                    Lines.Add(new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"⚠ {warnMsg}",
                        Type = "warning",
                        Color = "Yellow"
                    });
                    break;

                case "error":
                    string errMsg = evt.TryGetValue("message", out object? em) ? em?.ToString() ?? string.Empty : string.Empty;
                    Lines.Add(new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"✖ {errMsg}",
                        Type = "error",
                        Color = "Red"
                    });
                    break;

                case "prompt":
                    string promptMsg = evt.TryGetValue("message", out object? pm) ? pm?.ToString() ?? string.Empty : string.Empty;
                    Lines.Add(new OutputLine {
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
                    Lines.Add(new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"[{current}/{total}] {label}",
                        Type = "progress",
                        Color = "Cyan"
                    });
                    break;

                case "start":
                    string startContext = FormatEventData(evt);
                    Lines.Add(new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"▶ Started: {startContext}",
                        Type = "start",
                        Color = "Green"
                    });
                    break;

                case "end":
                    bool success = evt.TryGetValue("success", out object? suc) && suc is bool b && b;
                    Lines.Add(new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = success ? "✓ Completed successfully" : "✗ Completed with errors",
                        Type = "end",
                        Color = success ? "Green" : "Red"
                    });
                    break;

                case "progress_panel_start":
                    HandleProgressPanelStart();
                    break;

                case "progress_panel":
                    HandleProgressPanelUpdate(evt);
                    break;

                case "progress_panel_end":
                    HandleProgressPanelEnd();
                    break;

                case "run-all-start":
                case "run-all-op-start":
                case "run-all-op-end":
                case "run-all-complete":
                    string seqInfo = FormatEventData(evt);
                    Lines.Add(new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"• {evtType}: {seqInfo}",
                        Type = "info",
                        Color = "DarkGray"
                    });
                    break;

                default:
                    string unknownData = FormatEventData(evt);
                    Lines.Add(new OutputLine {
                        Timestamp = System.DateTime.Now,
                        Text = $"[{evtType}] {unknownData}",
                        Type = "unknown",
                        Color = "Gray"
                    });
                    break;
            }
        });
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
internal class OutputLine : INotifyPropertyChanged {
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

internal class ActiveJob : INotifyPropertyChanged {
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