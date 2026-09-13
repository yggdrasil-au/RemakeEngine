namespace EngineNet.GUI.Models;

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
                OnPropertyChanged(propertyName: nameof(Spinner));
                OnPropertyChanged(propertyName: nameof(Display));
            }
        }
    }

    public string Tool {
        get => _tool;
        set {
            if (_tool != value) {
                _tool = value;
                OnPropertyChanged(propertyName: nameof(Tool));
                OnPropertyChanged(propertyName: nameof(Display));
            }
        }
    }

    public string File {
        get => _file;
        set {
            if (_file != value) {
                _file = value;
                OnPropertyChanged(propertyName: nameof(File));
                OnPropertyChanged(propertyName: nameof(Display));
            }
        }
    }

    public string Elapsed {
        get => _elapsed;
        set {
            if (_elapsed != value) {
                _elapsed = value;
                OnPropertyChanged(propertyName: nameof(Elapsed));
                OnPropertyChanged(propertyName: nameof(Display));
            }
        }
    }

    public string Display => $"{Spinner} {Tool} · {File} · {Elapsed}";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName) {
        PropertyChanged?.Invoke(sender: this, e: new PropertyChangedEventArgs(propertyName: propertyName));
    }
}
