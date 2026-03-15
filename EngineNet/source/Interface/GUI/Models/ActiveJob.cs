using System.ComponentModel;

namespace EngineNet.Interface.GUI.Models;

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
