using System.ComponentModel;

namespace EngineNet.GUI.Models;

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
                OnPropertyChanged(propertyName: nameof(Text));
            }
        }
    }

    public string Type { get; set; } = "output";

    public string Color {
        get => _color;
        set {
            if (_color != value) {
                _color = value;
                OnPropertyChanged(propertyName: nameof(Color));
                OnPropertyChanged(propertyName: nameof(Color));
            }
        }
    }

    public string FormattedTime => Timestamp.ToString(format: "HH:mm:ss");

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName) {
        PropertyChanged?.Invoke(sender: this, e: new PropertyChangedEventArgs(propertyName: propertyName));
    }
}
