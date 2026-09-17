namespace EngineNet.Interface.GUI.Models;

public sealed class ProgressPanelState : INotifyPropertyChanged {
    private string _id = string.Empty;
    private string _label = string.Empty;
    private string _spinner = string.Empty;
    private double _percent;
    private string _progressLine = string.Empty;
    private string _activeSummary = string.Empty;
    private int _activeTotal;

    public string Id {
        get => _id;
        init => _id = value;
    }

    public string Label {
        get => _label;
        set => SetField(field: ref _label, value, propertyName: nameof(Label));
    }

    public string Spinner {
        get => _spinner;
        set => SetField(field: ref _spinner, value, propertyName: nameof(Spinner));
    }

    public double Percent {
        get => _percent;
        set => SetField(field: ref _percent, value, propertyName: nameof(Percent));
    }

    public string ProgressLine {
        get => _progressLine;
        set => SetField(field: ref _progressLine, value, propertyName: nameof(ProgressLine));
    }

    public string ActiveSummary {
        get => _activeSummary;
        set => SetField(field: ref _activeSummary, value, propertyName: nameof(ActiveSummary));
    }

    public int ActiveTotal {
        get => _activeTotal;
        set => SetField(field: ref _activeTotal, value, propertyName: nameof(ActiveTotal));
    }

    public ObservableCollection<ActiveJob> Jobs { get; } = new();
    public ObservableCollection<string> Lines { get; } = new();

    public void UpdateFrom(ProgressPanelModel model) {
        Label = model.Label;
        Spinner = model.Spinner;
        Percent = model.Percent;
        ProgressLine = model.ProgressLine;
        ActiveSummary = model.ActiveSummary;
        ActiveTotal = model.ActiveTotal;

        // sync lines
        for (int i = 0; i < model.Lines.Count; i++) {
            if (i < Lines.Count) {
                Lines[index: i] = model.Lines[index: i];
            } else {
                Lines.Add(item: model.Lines[index: i]);
            }
        }
        for (int i = Lines.Count - 1; i >= model.Lines.Count; i--) {
            Lines.RemoveAt(index: i);
        }

        // sync jobs
        for (int i = 0; i < model.Jobs.Count; i++) {
            ProgressJobSnapshot snapshot = model.Jobs[index: i];
            if (i < Jobs.Count) {
                ActiveJob job = Jobs[index: i];
                job.Spinner = model.Spinner;
                job.Tool = snapshot.Tool;
                job.File = snapshot.File;
                job.Elapsed = snapshot.Elapsed;
            } else {
                Jobs.Add(item: new ActiveJob {
                    Spinner = model.Spinner,
                    Tool = snapshot.Tool,
                    File = snapshot.File,
                    Elapsed = snapshot.Elapsed
                });
            }
        }
        for (int i = Jobs.Count - 1; i >= model.Jobs.Count; i--) {
            Jobs.RemoveAt(index: i);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, string propertyName) {
        if (!EqualityComparer<T>.Default.Equals(x: field, y: value)) {
            field = value;
            PropertyChanged?.Invoke(sender: this, e: new PropertyChangedEventArgs(propertyName: propertyName));
        }
    }
}
