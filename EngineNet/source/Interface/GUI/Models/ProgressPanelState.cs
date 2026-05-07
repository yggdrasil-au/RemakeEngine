using System.Collections.ObjectModel;
using System.ComponentModel;

namespace EngineNet.Interface.GUI.Models;

public class ProgressPanelState : INotifyPropertyChanged {
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
        set => SetField(ref _label, value, nameof(Label));
    }

    public string Spinner {
        get => _spinner;
        set => SetField(ref _spinner, value, nameof(Spinner));
    }

    public double Percent {
        get => _percent;
        set => SetField(ref _percent, value, nameof(Percent));
    }

    public string ProgressLine {
        get => _progressLine;
        set => SetField(ref _progressLine, value, nameof(ProgressLine));
    }

    public string ActiveSummary {
        get => _activeSummary;
        set => SetField(ref _activeSummary, value, nameof(ActiveSummary));
    }

    public int ActiveTotal {
        get => _activeTotal;
        set => SetField(ref _activeTotal, value, nameof(ActiveTotal));
    }

    public ObservableCollection<ActiveJob> Jobs { get; } = new ObservableCollection<ActiveJob>();
    public ObservableCollection<string> Lines { get; } = new ObservableCollection<string>();

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
                Lines[i] = model.Lines[i];
            } else {
                Lines.Add(model.Lines[i]);
            }
        }
        for (int i = Lines.Count - 1; i >= model.Lines.Count; i--) {
            Lines.RemoveAt(i);
        }

        // sync jobs
        for (int i = 0; i < model.Jobs.Count; i++) {
            var snapshot = model.Jobs[i];
            if (i < Jobs.Count) {
                var job = Jobs[i];
                job.Spinner = model.Spinner;
                job.Tool = snapshot.Tool;
                job.File = snapshot.File;
                job.Elapsed = snapshot.Elapsed;
            } else {
                Jobs.Add(new ActiveJob {
                    Spinner = model.Spinner,
                    Tool = snapshot.Tool,
                    File = snapshot.File,
                    Elapsed = snapshot.Elapsed
                });
            }
        }
        for (int i = Jobs.Count - 1; i >= model.Jobs.Count; i--) {
            Jobs.RemoveAt(i);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, string propertyName) {
        if (!EqualityComparer<T>.Default.Equals(field, value)) {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
