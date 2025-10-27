
namespace EngineNet.Interface.GUI.ViewModels;

public sealed class MainViewModel:System.ComponentModel.INotifyPropertyChanged {
    private readonly Core.OperationsEngine _engine; // OperationsEngine (dynamic to avoid hard ref)

    public ObservableCollection<GameItem> Library {
        get;
    } = new ObservableCollection<GameItem>();
    public ObservableCollection<StoreItem> Store {
        get;
    } = new ObservableCollection<StoreItem>();
    public ObservableCollection<InstallRow> Installing {
        get;
    } = new ObservableCollection<InstallRow>();

    private int _currentTabIndex;
    public int CurrentTabIndex {
        get => _currentTabIndex; set {
            if (_currentTabIndex != value) {
                _currentTabIndex = value;
                OnPropertyChanged(nameof(CurrentTabIndex));
            }
        }
    }

    private string _consoleText = string.Empty;
    public string ConsoleText {
        get => _consoleText; set {
            if (!string.Equals(_consoleText, value)) {
                _consoleText = value;
                OnPropertyChanged(nameof(ConsoleText));
            }
        }
    }

    private bool _promptIsVisible;
    public bool PromptIsVisible {
        get => _promptIsVisible; set {
            if (_promptIsVisible != value) {
                _promptIsVisible = value;
                OnPropertyChanged(nameof(PromptIsVisible));
            }
        }
    }
    public string? PromptQuestion {
        get; set;
    }
    public string? PromptAnswer {
        get; set;
    }

    public ICommand RefreshLibraryCommand {
        get;
    }
    public ICommand RefreshStoreCommand {
        get;
    }
    public ICommand RunGameCommand {
        get;
    }
    public ICommand OpenFolderCommand {
        get;
    }
    public ICommand DownloadModuleCommand {
        get;
    }
    public ICommand InstallModuleCommand {
        get;
    }
    public ICommand SubmitPromptCommand {
        get;
    }

    public MainViewModel(Core.OperationsEngine? engine) {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));

        RefreshLibraryCommand = new RelayCommand(_ => RefreshLibrary());
        RefreshStoreCommand = new RelayCommand(_ => RefreshStore());
        RunGameCommand = new AsyncRelayCommand(async p => {
            if (p is GameItem gi) {
                // Prefer launching executable; fallback to opening folder
                try {
                    if (_engine.LaunchGame(gi.Name))
                        return;
                } catch { /* ignore */ }
                await EngineNet.Interface.GUI.Avalonia.PromptHelpers.InfoAsync(message: "No executable found. Opening folder instead.", title: "Play");
                await OpenFolder(gi);
            }
        });
        OpenFolderCommand = new RelayCommand(async p => { if (p is GameItem gi) await OpenFolder(gi); });
        DownloadModuleCommand = new AsyncRelayCommand(async p => {
            if (p is StoreItem si && !string.IsNullOrWhiteSpace(si.Url)) {
                bool ok = await Task.Run(() => _engine.DownloadModule(si.Url));
                await EngineNet.Interface.GUI.Avalonia.PromptHelpers.InfoAsync(ok ? $"Downloaded '{si.Name}'." : $"Failed to download '{si.Name}'.", title: "Download");
                RefreshStore();
            }
        });
        InstallModuleCommand = new AsyncRelayCommand(async p => {
            if (p is StoreItem si)
                await StartInstallAsync(si.Name);
        });
        SubmitPromptCommand = new RelayCommand(_ => {
            string? ans = PromptAnswer ?? string.Empty;
            _activePromptTcs?.TrySetResult(ans);
            PromptAnswer = string.Empty;
            PromptIsVisible = false;
            OnPropertyChanged(nameof(PromptAnswer));
            OnPropertyChanged(nameof(PromptQuestion));
        });

        // initial load
        RefreshLibrary();
        RefreshStore();
    }

    private void RefreshLibrary() {
        Library.Clear();
        try {
            IDictionary<string, object?> games;
            try {
                games = _engine.GetBuiltGames();
            } catch { games = _engine.ListGames(); }
            foreach (KeyValuePair<string, object?> kv in games) {
                IDictionary<string, object?>? info = kv.Value as IDictionary<string, object?>;
                string? exe = info != null && info.TryGetValue(key: "exe", out object? e) ? e?.ToString() : null;
                Library.Add(item: new GameItem { Name = kv.Key, Info = info, ExePath = exe });
            }
            if (Library.Count == 0) {
                Library.Add(item: new GameItem { Name = "No games found.", Info = null, IsPlaceholder = true });
            }
        } catch {
            Library.Add(item: new GameItem { Name = "Error loading games.", Info = null, IsPlaceholder = true });
        }
    }

    private void RefreshStore() {
        Store.Clear();
        try {
            // Registry JSON entries
            System.Reflection.Assembly? asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => string.Equals(a.GetName().Name, b: "EngineNet", StringComparison.OrdinalIgnoreCase));
            Type? t = asm?.GetType(name: "RemakeEngine.Core.Registries");
            Dictionary<string, StoreItem>? byName = new Dictionary<string, StoreItem>(StringComparer.OrdinalIgnoreCase);
            if (t is not null) {
                dynamic regs = Activator.CreateInstance(t, System.IO.Directory.GetCurrentDirectory())!;
                IDictionary<string, object?> reg = regs.GetRegisteredModules();
                foreach (KeyValuePair<string, object?> kv in reg) {
                    IDictionary<string, object?>? meta = kv.Value as IDictionary<string, object?>;
                    string? url = meta != null && meta.TryGetValue(key: "url", out object? u) ? u?.ToString() : null;
                    StoreItem? it = new StoreItem { Name = kv.Key, Meta = kv.Value, Url = url };
                    byName[kv.Key] = it;
                }
            }

            // Local modules in RemakeRegistry/Games
            string? gamesRoot = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "RemakeRegistry", "Games");
            if (System.IO.Directory.Exists(gamesRoot)) {
                foreach (string dir in System.IO.Directory.EnumerateDirectories(gamesRoot)) {
                    string? name = new System.IO.DirectoryInfo(dir).Name;
                    if (!byName.TryGetValue(name, out StoreItem? it)) {
                        it = new StoreItem { Name = name, Meta = null, Url = null };
                        byName[name] = it;
                    }
                }
            }

            foreach (StoreItem it in byName.Values.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)) {
                string state = _engine.GetModuleState(it.Name);
                it.State = state;
                it.CanDownload = state == "not_downloaded" && !string.IsNullOrWhiteSpace(it.Url);
                it.CanInstall = state == "downloaded";
                it.IsInstalled = state == "installed";
                Store.Add(it);
            }
            if (Store.Count == 0)
                Store.Add(new StoreItem { Name = "No registry entries found.", Meta = null, IsPlaceholder = true });
        } catch {
            Store.Add(new StoreItem { Name = "Error loading registry.", Meta = null, IsPlaceholder = true });
        }
    }

    private async Task OpenFolder(GameItem item) {
        try {
            string? path = (string?)_engine.GetGamePath(item.Name);
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path)) {
                await EngineNet.Interface.GUI.Avalonia.PromptHelpers.InfoAsync($"Couldn't locate a folder for '{item.Name}'.", "Open Folder");
                return;
            }
            System.Diagnostics.ProcessStartInfo? psi = new System.Diagnostics.ProcessStartInfo { UseShellExecute = true };
            if (OperatingSystem.IsWindows()) {
                psi.FileName = "explorer";
                psi.Arguments = $"\"{path}\"";
            } else if (OperatingSystem.IsMacOS()) {
                psi.FileName = "open";
                psi.Arguments = $"\"{path}\"";
            } else {
                psi.FileName = "xdg-open";
                psi.Arguments = $"\"{path}\"";
            }
            System.Diagnostics.Process.Start(psi);
        } catch {
            /* ignore */
        }
    }

    private System.Threading.Tasks.Task<bool> StartInstallAsync(string moduleName) {
        CurrentTabIndex = 2; // switch to Installing tab
        EnsureInstallRow(moduleName);

        // Clear any lingering prompt state
        PromptIsVisible = false;
        PromptQuestion = null;
        PromptAnswer = string.Empty;
        OnPropertyChanged(nameof(PromptQuestion));
        OnPropertyChanged(nameof(PromptAnswer));

        // Handlers
        System.Action<string, string> outH = (line, stream) => {
            Dispatcher.UIThread.Post(() => { ConsoleText += (string.IsNullOrEmpty(ConsoleText) ? "" : "\r\n") + line; });
        };
        System.Action<System.Collections.Generic.Dictionary<string, object?>> evtH = (evt) => {
            var kind = evt.TryGetValue("event", out var e) ? e?.ToString() ?? string.Empty : string.Empty;
            if (string.Equals(kind, "prompt", StringComparison.OrdinalIgnoreCase)) {
                var msg = evt.TryGetValue("message", out var m) ? m?.ToString() ?? "Input required" : "Input required";
                Dispatcher.UIThread.Post(() => { PromptQuestion = msg; PromptIsVisible = true; OnPropertyChanged(nameof(PromptQuestion)); });
                _activePromptTcs = new System.Threading.Tasks.TaskCompletionSource<string?>();
            } else if (string.Equals(kind, "print", StringComparison.OrdinalIgnoreCase)) {
                var msg = evt.TryGetValue("message", out var m) ? m?.ToString() ?? string.Empty : string.Empty;
                // For now, append plain text; GUI TextBox does not render colors
                Dispatcher.UIThread.Post(() => { ConsoleText += (string.IsNullOrEmpty(ConsoleText) ? "" : "\r\n") + msg; });
            } else if (string.Equals(kind, "warning", StringComparison.OrdinalIgnoreCase)) {
                var msg = evt.TryGetValue("message", out var m) ? m?.ToString() ?? string.Empty : string.Empty;
                Dispatcher.UIThread.Post(() => { ConsoleText += (string.IsNullOrEmpty(ConsoleText) ? "" : "\r\n") + "⚠ " + msg; });
            } else if (string.Equals(kind, "error", StringComparison.OrdinalIgnoreCase)) {
                var msg = evt.TryGetValue("message", out var m) ? m?.ToString() ?? string.Empty : string.Empty;
                Dispatcher.UIThread.Post(() => { ConsoleText += (string.IsNullOrEmpty(ConsoleText) ? "" : "\r\n") + "✖ " + msg; });
            } else if (string.Equals(kind, "progress", StringComparison.OrdinalIgnoreCase)) {
                var label = evt.TryGetValue("label", out var l) ? l?.ToString() ?? "Working" : "Working";
                int current = 0, total = 0;
                try {
                    if (evt.TryGetValue("current", out var c))
                        current = Convert.ToInt32(c);
                } catch { }
                try {
                    if (evt.TryGetValue("total", out var t))
                        total = Convert.ToInt32(t);
                } catch { }
                UpdateInstallProgress(moduleName, label, current, total);
            } else if (string.Equals(kind, "end", StringComparison.OrdinalIgnoreCase)) {
                Dispatcher.UIThread.Post(() => { RemoveInstallRow(moduleName); RefreshStore(); RefreshLibrary(); PromptIsVisible = false; OnPropertyChanged(nameof(PromptQuestion)); });
            }
        };
        System.Func<string?> stdin = () => _activePromptTcs?.Task.GetAwaiter().GetResult();
        // Convert to engine-specific delegate types via reflection to avoid compile-time reference
        Type? engineType = _engine.GetType();
        System.Reflection.Assembly? asm = engineType.Assembly;
        Type? prType = asm.GetType("RemakeEngine.Core.ProcessRunner");


        Core.Sys.ProcessRunner.OutputHandler outDel = (line, stream) => {
            Dispatcher.UIThread.Post(() => {
                ConsoleText += (string.IsNullOrEmpty(ConsoleText) ? "" : "\r\n") + line;
            });
        };

        Core.Sys.ProcessRunner.EventHandler evtDel = (evt) => {
            var kind = evt.TryGetValue("event", out var e) ? e?.ToString() ?? string.Empty : string.Empty;
            if (string.Equals(kind, "prompt", StringComparison.OrdinalIgnoreCase)) {
                var msg = evt.TryGetValue("message", out var m) ? m?.ToString() ?? "Input required" : "Input required";
                Dispatcher.UIThread.Post(() => {
                    PromptQuestion = msg;
                    PromptIsVisible = true;
                    OnPropertyChanged(nameof(PromptQuestion));
                });
                _activePromptTcs = new System.Threading.Tasks.TaskCompletionSource<string?>();
            } else if (string.Equals(kind, "print", StringComparison.OrdinalIgnoreCase)) {
                var msg = evt.TryGetValue("message", out var m) ? m?.ToString() ?? string.Empty : string.Empty;
                Dispatcher.UIThread.Post(() => { ConsoleText += (string.IsNullOrEmpty(ConsoleText) ? "" : "\r\n") + msg; });
            } else if (string.Equals(kind, "warning", StringComparison.OrdinalIgnoreCase)) {
                var msg = evt.TryGetValue("message", out var m) ? m?.ToString() ?? string.Empty : string.Empty;
                Dispatcher.UIThread.Post(() => { ConsoleText += (string.IsNullOrEmpty(ConsoleText) ? "" : "\r\n") + "⚠ " + msg; });
            } else if (string.Equals(kind, "error", StringComparison.OrdinalIgnoreCase)) {
                var msg = evt.TryGetValue("message", out var m) ? m?.ToString() ?? string.Empty : string.Empty;
                Dispatcher.UIThread.Post(() => { ConsoleText += (string.IsNullOrEmpty(ConsoleText) ? "" : "\r\n") + "✖ " + msg; });
            } else if (string.Equals(kind, "progress", StringComparison.OrdinalIgnoreCase)) {
                var label = evt.TryGetValue("label", out var l) ? l?.ToString() ?? "Working" : "Working";
                int current = 0, total = 0;
                try {
                    if (evt.TryGetValue("current", out var c))
                        current = Convert.ToInt32(c);
                } catch { }
                try {
                    if (evt.TryGetValue("total", out var t))
                        total = Convert.ToInt32(t);
                } catch { }
                UpdateInstallProgress(moduleName, label, current, total);
            } else if (string.Equals(kind, "end", StringComparison.OrdinalIgnoreCase)) {
                Dispatcher.UIThread.Post(() => {
                    RemoveInstallRow(moduleName);
                    RefreshStore();
                    RefreshLibrary();
                    PromptIsVisible = false;
                    OnPropertyChanged(nameof(PromptQuestion));
                });
            }
        };

        EngineNet.Core.Sys.ProcessRunner.StdinProvider stdinDel = () =>
            _activePromptTcs?.Task.GetAwaiter().GetResult();

        // call:
        Task<bool>? task = _engine.InstallModuleAsync(moduleName, outDel, evtDel, stdinDel);
        return task;
    }

    private System.Threading.Tasks.TaskCompletionSource<string?>? _activePromptTcs;

    private void EnsureInstallRow(string moduleName) {
        if (Installing.Any(x => string.Equals(x.Name, moduleName, StringComparison.OrdinalIgnoreCase)))
            return;
        Dispatcher.UIThread.Post(() => Installing.Add(new InstallRow { Name = moduleName, Label = "Generating...", Progress = 0.0 }));
    }
    private void UpdateInstallProgress(string moduleName, string label, int current, int total) {
        var row = Installing.FirstOrDefault(x => string.Equals(x.Name, moduleName, StringComparison.OrdinalIgnoreCase));
        if (row is null) {
            EnsureInstallRow(moduleName);
            row = Installing.First(x => string.Equals(x.Name, moduleName, StringComparison.OrdinalIgnoreCase));
        }
        var pct = (total > 0) ? Math.Clamp((double)current / (double)total, 0.0, 1.0) : 0.0;
        Dispatcher.UIThread.Post(() => { row.Label = $"{label} ({current}/{total})"; row.Progress = pct; });
    }
    private void RemoveInstallRow(string moduleName) {
        var row = Installing.FirstOrDefault(x => string.Equals(x.Name, moduleName, StringComparison.OrdinalIgnoreCase));
        if (row is null)
            return;
        Dispatcher.UIThread.Post(() => Installing.Remove(row));
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public sealed class GameItem {
    public string Name { get; set; } = string.Empty;
    public IDictionary<string, object?>? Info {
        get; set;
    }
    public string? ExePath {
        get; set;
    }
    public bool IsPlaceholder {
        get; set;
    }
}

public sealed class StoreItem {
    public string Name { get; set; } = string.Empty;
    public object? Meta {
        get; set;
    }
    public string? Url {
        get; set;
    }
    public string State { get; set; } = string.Empty; // not_downloaded | downloaded | installed
    public bool CanDownload {
        get; set;
    }
    public bool CanInstall {
        get; set;
    }
    public bool IsInstalled {
        get; set;
    }
    public bool IsPlaceholder {
        get; set;
    }
}

public sealed class InstallRow:System.ComponentModel.INotifyPropertyChanged {
    private string _label = string.Empty;
    private double _progress;
    public string Name { get; set; } = string.Empty;
    public string Label {
        get => _label; set {
            if (_label != value) {
                _label = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Label)));
            }
        }
    }
    public double Progress {
        get => _progress; set {
            if (Math.Abs(_progress - value) > 0.0001) {
                _progress = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Progress)));
            }
        }
    }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class RelayCommand:ICommand {
    private readonly Action<object?> _action;
    private readonly Func<object?, bool>? _canExecute;
    public RelayCommand(Action<object?> action, Func<object?, bool>? canExecute = null) {
        _action = action;
        _canExecute = canExecute;
    }
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _action(parameter);
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class AsyncRelayCommand:ICommand {
    private readonly Func<object?, Task> _action;
    private readonly Func<object?, bool>? _canExecute;
    private bool _busy;
    public AsyncRelayCommand(Func<object?, Task> action, Func<object?, bool>? canExecute = null) {
        _action = action;
        _canExecute = canExecute;
    }
    public bool CanExecute(object? parameter) => !_busy && (_canExecute?.Invoke(parameter) ?? true);
    public async void Execute(object? parameter) {
        _busy = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try {
            await _action(parameter);
        } finally { _busy = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler? CanExecuteChanged;
}

// Local helper to notify item change in collection templates
