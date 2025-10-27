using System.ComponentModel;

namespace EngineNet.Interface.GUI;

/// <summary>
/// Shared service for capturing and displaying operation output across all GUI pages.
/// This ensures that output persists when navigating between pages.
/// </summary>
public static class OperationOutputService {
    private static readonly object _lock = new object();
    
    /// <summary>
    /// Shared output lines collection. Thread-safe via Dispatcher.
    /// </summary>
    public static ObservableCollection<OutputLine> Lines { get; } = new ObservableCollection<OutputLine>();
    
    /// <summary>
    /// Current operation name, if any.
    /// </summary>
    private static string? _currentOperation;
    public static string? CurrentOperation {
        get {
            lock (_lock) {
                return _currentOperation;
            }
        }
        private set {
            lock (_lock) {
                _currentOperation = value;
            }
        }
    }
    
    /// <summary>
    /// Clear output and start a new operation.
    /// </summary>
    public static void StartOperation(string operationName, string gameName) {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            Lines.Clear();
            CurrentOperation = $"{gameName} - {operationName}";
            AddLine($"=== Starting: {operationName} for {gameName} ===", "header");
        });
    }
    
    /// <summary>
    /// Add a raw output line.
    /// </summary>
    public static void AddOutput(string text, string stream = "stdout") {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            Lines.Add(new OutputLine {
                Timestamp = DateTime.Now,
                Text = text,
                Type = stream == "stderr" ? "error" : "output",
                Color = stream == "stderr" ? "Red" : "Gray"
            });
        });
    }
    
    /// <summary>
    /// Handle a structured event from the engine.
    /// </summary>
    public static void HandleEvent(Dictionary<string, object?> evt) {
        if (!evt.TryGetValue("event", out object? evtTypeObj)) {
            return;
        }
        
        string? evtType = evtTypeObj?.ToString();
        
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            switch (evtType) {
                case "print":
                    string msg = evt.TryGetValue("message", out object? m) ? m?.ToString() ?? "" : "";
                    string color = evt.TryGetValue("color", out object? c) ? c?.ToString() ?? "Gray" : "Gray";
                    Lines.Add(new OutputLine {
                        Timestamp = DateTime.Now,
                        Text = msg,
                        Type = "print",
                        Color = MapColor(color)
                    });
                    break;
                    
                case "warning":
                    string warnMsg = evt.TryGetValue("message", out object? wm) ? wm?.ToString() ?? "" : "";
                    Lines.Add(new OutputLine {
                        Timestamp = DateTime.Now,
                        Text = $"⚠ {warnMsg}",
                        Type = "warning",
                        Color = "Yellow"
                    });
                    break;
                    
                case "error":
                    string errMsg = evt.TryGetValue("message", out object? em) ? em?.ToString() ?? "" : "";
                    Lines.Add(new OutputLine {
                        Timestamp = DateTime.Now,
                        Text = $"✖ {errMsg}",
                        Type = "error",
                        Color = "Red"
                    });
                    break;
                    
                case "prompt":
                    string promptMsg = evt.TryGetValue("message", out object? pm) ? pm?.ToString() ?? "" : "";
                    Lines.Add(new OutputLine {
                        Timestamp = DateTime.Now,
                        Text = $"? {promptMsg}",
                        Type = "prompt",
                        Color = "Cyan"
                    });
                    break;
                    
                case "progress":
                    int current = evt.TryGetValue("current", out object? cur) ? Convert.ToInt32(cur) : 0;
                    int total = evt.TryGetValue("total", out object? tot) ? Convert.ToInt32(tot) : 0;
                    string label = evt.TryGetValue("label", out object? lbl) ? lbl?.ToString() ?? "" : "";
                    Lines.Add(new OutputLine {
                        Timestamp = DateTime.Now,
                        Text = $"[{current}/{total}] {label}",
                        Type = "progress",
                        Color = "Cyan"
                    });
                    break;
                    
                case "start":
                    string startContext = FormatEventData(evt);
                    Lines.Add(new OutputLine {
                        Timestamp = DateTime.Now,
                        Text = $"▶ Started: {startContext}",
                        Type = "start",
                        Color = "Green"
                    });
                    break;
                    
                case "end":
                    bool success = evt.TryGetValue("success", out object? suc) && suc is bool b && b;
                    Lines.Add(new OutputLine {
                        Timestamp = DateTime.Now,
                        Text = success ? "✓ Completed successfully" : "✗ Completed with errors",
                        Type = "end",
                        Color = success ? "Green" : "Red"
                    });
                    break;
                    
                case "run-all-start":
                case "run-all-op-start":
                case "run-all-op-end":
                case "run-all-complete":
                    // Log these but don't display redundantly
                    string seqInfo = FormatEventData(evt);
                    Lines.Add(new OutputLine {
                        Timestamp = DateTime.Now,
                        Text = $"• {evtType}: {seqInfo}",
                        Type = "info",
                        Color = "DarkGray"
                    });
                    break;
                    
                default:
                    // Unknown event - log it for debugging
                    string unknownData = FormatEventData(evt);
                    Lines.Add(new OutputLine {
                        Timestamp = DateTime.Now,
                        Text = $"[{evtType}] {unknownData}",
                        Type = "unknown",
                        Color = "Gray"
                    });
                    break;
            }
        });
    }
    
    /// <summary>
    /// Add a status line (used internally).
    /// </summary>
    private static void AddLine(string text, string type, string color = "Gray") {
        Lines.Add(new OutputLine {
            Timestamp = DateTime.Now,
            Text = text,
            Type = type,
            Color = color
        });
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
    
    /// <summary>
    /// Format event data for display (excluding the event type itself).
    /// </summary>
    private static string FormatEventData(Dictionary<string, object?> evt) {
        var parts = new List<string>();
        foreach (var kv in evt) {
            if (kv.Key.Equals("event", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            parts.Add($"{kv.Key}={kv.Value}");
        }
        return string.Join(", ", parts);
    }
    
    /// <summary>
    /// Clear all output.
    /// </summary>
    public static void Clear() {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            Lines.Clear();
            CurrentOperation = null;
        });
    }

    /// <summary>
    /// Clear all output.
    /// </summary>
    public static async Task ClearAsync() {
        await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
            Lines.Clear();
            CurrentOperation = null;
        });
    }
}

/// <summary>
/// Represents a single line of output in the operation log.
/// </summary>
public class OutputLine : INotifyPropertyChanged {
    private string _text = "";
    private string _color = "Gray";
    
    public DateTime Timestamp { get; set; }
    
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
