using System.ComponentModel;

namespace EngineNet.Interface.GUI.Views.Pages;

public partial class BuildingPage:UserControl, INotifyPropertyChanged {
    private readonly Core.OperationsEngine? _engine;
    private readonly DispatcherTimer? _timer;

    public ObservableCollection<Job> Jobs { get; } = new ObservableCollection<Job>();

    private string _status = "";
    public string Status {
        get => _status; private set {
            _status = value;
            Raise(nameof(Status));
        }
    }

    public ICommand RefreshCommand {
        get;
    }
    public ICommand CancelCommand {
        get;
    }
    public ICommand RetryCommand {
        get;
    }

    public BuildingPage() {
        InitializeComponent();
        DataContext = this;

        // Initialize commands
        RefreshCommand = new Cmd(async _ => await Task.CompletedTask);
        CancelCommand = new Cmd(async _ => await Task.CompletedTask);
        RetryCommand = new Cmd(async _ => await Task.CompletedTask);

        // Add sample data for the previewer
        Jobs.Add(new Job { Name = "Example Download", State = "Running", ProgressPercent = 50 });
        Jobs.Add(new Job { Name = "Another Install", State = "Failed", ProgressPercent = 0 });
        Status = "Showing design-time data.";

    }

    public BuildingPage(Core.OperationsEngine engine) {
        _engine = engine;
        DataContext = this;
        InitializeComponent();

        RefreshCommand = new Cmd(async _ => await LoadAsync());
        CancelCommand = new Cmd(async j => await CancelAsync(j as Job));
        RetryCommand = new Cmd(async j => await RetryAsync(j as Job));

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (object? _, EventArgs __) => await LoadAsync();
        _timer.Start();

        _ = LoadAsync();
    }

    private async Task LoadAsync() {
        try {
            IEnumerable<Job>? jobs = TryListJobs() ?? Array.Empty<Job>();
            await Dispatcher.UIThread.InvokeAsync(() => {
                Sync(Jobs, jobs, j => j.Id);
                Status = Jobs.Count == 0 ? "No active installs." : $"{Jobs.Count} job(s)";
            });
        } catch (Exception ex) {
            Status = $"Failed to load jobs: {ex.Message}";
        }
    }

    private System.Collections.Generic.IEnumerable<Job>? TryListJobs() {
        if (_engine is not null) {
            Dictionary<string, object?>? raw = _engine.GetBuiltGames();
            return Project(raw);
        }
        return null;
    }

    private static System.Collections.Generic.IEnumerable<Job> Project(dynamic raw) {
        List<Job>? list = new System.Collections.Generic.List<Job>();
        foreach (dynamic r in raw) {
            try {
                string id = r.Id ?? r.id ?? Guid.NewGuid().ToString(format: "N");
                string name = r.Name ?? r.name ?? "Item";
                string state = r.State ?? r.state ?? "Running";
                int pct = 0;
                try {
                    pct = (int)(r.ProgressPercent ?? r.progress ?? 0);
                } catch { /* ignore */ }
                list.Add(item: new Job { Id = id, Name = name, State = state, ProgressPercent = pct });
            } catch {
                list.Add(item: new Job { Id = Guid.NewGuid().ToString(format: "N"), Name = "Unknown", State = "Unknown", ProgressPercent = 0 });
            }
        }
        return list;
    }

    private async Task CancelAsync(Job? job) {
        if (job is null)
            return;
        try {
            try {
                // cancel the Build process
            } catch {
                //_engine.Cancel(job.Id);
            }
            Status = $"Cancelled {job.Name}.";
        } catch (Exception ex) {
            Status = $"Cancel failed: {ex.Message}";
        }
        await LoadAsync();
    }

    private async Task RetryAsync(Job? job) {
        if (job is null)
            return;
        try {
            try {
                //_engine.Installer.Retry(job.Id);
            } catch {
                //_engine.Retry(job.Id);
            }
            Status = $"Retrying {job.Name}�";
        } catch (Exception ex) {
            Status = $"Retry failed: {ex.Message}";
        }
        await LoadAsync();
    }

    private static void Sync<T, TKey>(ObservableCollection<T> target,
        System.Collections.Generic.IEnumerable<T> source,
        Func<T, TKey> key) {
        List<T>? srcList = source.ToList();
        // remove missing
        for (int i = target.Count - 1; i >= 0; i--)
            if (!srcList.Any(s => Equals(key(s), key(target[i]))))
                target.RemoveAt(i);
        // upsert in order
        foreach (T s in srcList) {
            TKey? k = key(s);
            T? existing = target.FirstOrDefault(t => Equals(key(t), k));
            if (existing is null)
                target.Add(s);
            else
                target[target.IndexOf(existing)] = s;
        }
    }

    public sealed class Job {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string State { get; set; } = "";
        public int ProgressPercent {
            get; set;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class Cmd:ICommand {
        private readonly Func<object?, Task> _run;
        public Cmd(Func<object?, Task> run) => _run = run;
        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter) => await _run(parameter);
        public event EventHandler? CanExecuteChanged;
    }
}
