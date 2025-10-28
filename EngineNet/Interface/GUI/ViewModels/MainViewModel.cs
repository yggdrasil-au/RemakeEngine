

namespace EngineNet.Interface.GUI.ViewModels;

public sealed class MainViewModel:System.ComponentModel.INotifyPropertyChanged {
    private readonly dynamic _engine;
    private readonly Dictionary<string, InstallRow> _rows = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<GameItem> Library { get; } = new();
    public ObservableCollection<StoreItem> Store { get; } = new();
    public ObservableCollection<InstallRow> Installing { get; } = new();

    private int _currentTabIndex;
    public int CurrentTabIndex {
        get => _currentTabIndex;
        set {
            if (_currentTabIndex != value) {
                _currentTabIndex = value;
                OnPropertyChanged(nameof(CurrentTabIndex));
            }
        }
    }

    private string _consoleText = string.Empty;
    public string ConsoleText {
        get => _consoleText;
        set {
            if (!string.Equals(_consoleText, value, StringComparison.Ordinal)) {
                _consoleText = value;
                OnPropertyChanged(nameof(ConsoleText));
            }
        }
    }

    private bool _promptIsVisible;
    public bool PromptIsVisible {
        get => _promptIsVisible;
        set {
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

    public System.Windows.Input.ICommand RefreshLibraryCommand {
        get;
    }
    public System.Windows.Input.ICommand RefreshStoreCommand {
        get;
    }
    public System.Windows.Input.ICommand RunBuildCommand {
        get;
    }
    public System.Windows.Input.ICommand RunGameCommand {
        get;
    }
    public System.Windows.Input.ICommand OpenFolderCommand {
        get;
    }
    public System.Windows.Input.ICommand DownloadModuleCommand {
        get;
    }
    public System.Windows.Input.ICommand InstallModuleCommand {
        get;
    }
    public System.Windows.Input.ICommand SubmitPromptCommand {
        get;
    }

    private readonly Type? _outputHandlerType;
    private readonly Type? _eventHandlerType;
    private readonly Type? _stdinProviderType;

    private TaskCompletionSource<string?>? _activePromptTcs;

    public MainViewModel(object engine) {
        DebugWriteLine("Initializing MainViewModel.");
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));

        var asm = _engine.GetType().Assembly;
        var processRunnerType = asm.GetType("EngineNet.Core.Sys.ProcessRunner") ?? asm.GetType("RemakeEngine.Core.ProcessRunner");
        _outputHandlerType = processRunnerType?.GetNestedType("OutputHandler");
        _eventHandlerType = processRunnerType?.GetNestedType("EventHandler");
        _stdinProviderType = processRunnerType?.GetNestedType("StdinProvider");

        RefreshLibraryCommand = new RelayCommand(_ => RefreshLibrary());
        RefreshStoreCommand = new RelayCommand(_ => RefreshStore());
        RunBuildCommand = new AsyncRelayCommand(async p => {
            if (p is GameItem gi && !gi.IsPlaceholder) {
                await RunBuildAsync(gi);
            }
        }, p => p is GameItem { IsPlaceholder: false });
        RunGameCommand = new AsyncRelayCommand(async p => {
            if (p is GameItem gi && !gi.IsPlaceholder) {
                if (TryLaunchGame(gi.Name)) {
                    return;
                }

                await PromptHelpers.InfoAsync("No executable found. Opening folder instead.", "Play");
                OpenFolder(gi);
            }
        }, p => p is GameItem { IsPlaceholder: false });
        OpenFolderCommand = new RelayCommand(p => {
            if (p is GameItem gi && !gi.IsPlaceholder) {
                OpenFolder(gi);
            }
        });
        DownloadModuleCommand = new AsyncRelayCommand(async p => {
            if (p is StoreItem si && !si.IsPlaceholder && !string.IsNullOrWhiteSpace(si.Url)) {
                var ok = await Task.Run(() => (bool)_engine.DownloadModule(si.Url));
                await PromptHelpers.InfoAsync(ok ? $"Downloaded '{si.Name}'." : $"Failed to download '{si.Name}'.", "Download");
                RefreshStore();
            }
        }, p => p is StoreItem { CanDownload: true });
        InstallModuleCommand = new AsyncRelayCommand(async p => {
            if (p is StoreItem si && !si.IsPlaceholder && si.CanInstall) {
                await StartInstallAsync(si.Name);
            }
        }, p => p is StoreItem { CanInstall: true });
        SubmitPromptCommand = new RelayCommand(_ => {
            var answer = PromptAnswer ?? string.Empty;
            _activePromptTcs?.TrySetResult(answer);
            PromptAnswer = string.Empty;
            PromptIsVisible = false;
            OnPropertyChanged(nameof(PromptAnswer));
            OnPropertyChanged(nameof(PromptQuestion));
        });

        RefreshLibrary();
        RefreshStore();
    }

    private void RefreshLibrary() {
        DebugWriteLine("Refreshing library.");
        Library.Clear();
        try {
            IDictionary<string, object?> games = FetchGames();
            if (games.Count == 0) {
                Library.Add(new GameItem { Name = "No games found.", IsPlaceholder = true });
                return;
            }

            foreach (var kv in games.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)) {
                var info = kv.Value as IDictionary<string, object?>;
                Library.Add(new GameItem {
                    Name = kv.Key,
                    Title = TryGetString(info, "title"),
                    Info = info,
                    ExePath = TryGetString(info, "exe"),
                    GameRoot = TryGetString(info, "game_root"),
                    IsPlaceholder = false
                });
            }
        } catch {
            DebugWriteLine("Error loading games.");
            Library.Clear();
            Library.Add(new GameItem { Name = "Error loading games.", IsPlaceholder = true });
        }
    }

    private void RefreshStore() {
        DebugWriteLine("Refreshing store.");
        Store.Clear();
        try {
            IDictionary<string, object?> registry = GetRegisteredModules();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in registry.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)) {
                seen.Add(kv.Key);
                var meta = kv.Value as IDictionary<string, object?>;
                string? url = TryGetString(meta, "url");
                string state = GetModuleStateSafe(kv.Key);
                Store.Add(new StoreItem {
                    Name = kv.Key,
                    Meta = kv.Value,
                    Url = url,
                    State = state,
                    CanDownload = state == "not_downloaded" && !string.IsNullOrWhiteSpace(url),
                    CanInstall = state == "downloaded",
                    IsInstalled = state == "installed",
                    IsPlaceholder = false
                });
            }

            IDictionary<string, object?> localGames = FetchGames();
            foreach (var kv in localGames) {
                if (!seen.Add(kv.Key)) {
                    continue;
                }

                string state = GetModuleStateSafe(kv.Key);
                Store.Add(new StoreItem {
                    Name = kv.Key,
                    Meta = kv.Value,
                    Url = null,
                    State = state,
                    CanDownload = false,
                    CanInstall = state == "downloaded",
                    IsInstalled = state == "installed",
                    IsPlaceholder = false
                });
            }

            if (Store.Count == 0) {
                Store.Add(new StoreItem { Name = "No registry entries found.", State = string.Empty, IsPlaceholder = true });
            }
        } catch {
            DebugWriteLine("Error loading registry.");
            Store.Clear();
            Store.Add(new StoreItem { Name = "Error loading registry.", State = string.Empty, IsPlaceholder = true });
        }
    }

    private IDictionary<string, object?> FetchGames() {
        DebugWriteLine("Fetching games from engine.");
        try {
            var installed = _engine.GetInstalledGames();
            if (installed is IDictionary<string, object?> dict) {
                return dict;
            }
        } catch {
            DebugWriteLine("Error fetching installed games.");
         }

        try {
            var games = _engine.ListGames();
            if (games is IDictionary<string, object?> dict) {
                return dict;
            }
        } catch {
            DebugWriteLine("Error fetching game list.");
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private IDictionary<string, object?> GetRegisteredModules() {
        DebugWriteLine("Fetching registered modules.");
        try {
            var modules = _engine.GetRegisteredModules();
            if (modules is IDictionary<string, object?> dict) {
                return dict;
            }

            if (modules is IReadOnlyDictionary<string, object?> ro) {
                return new Dictionary<string, object?>(ro, StringComparer.OrdinalIgnoreCase);
            }
        } catch {
            DebugWriteLine("Error fetching registered modules.");
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private string GetModuleStateSafe(string name) {
        try {
            DebugWriteLine($"Getting module state for '{name}'");
            return _engine.GetModuleState(name);
        } catch {
            return "not_downloaded";
        }
    }

    private async Task RunBuildAsync(GameItem game) {
        DebugWriteLine($"Starting build for game '{game.Name}'");
        CurrentTabIndex = 2;
        string contextId = $"build:{game.Name}";
        string displayName = game.DisplayTitle;
        EnsureInstallRow(contextId, displayName, OperationKind.Build);
        ClearPromptState();

        var handlers = CreateEngineDelegates(contextId, displayName, OperationKind.Build, refreshOnComplete: true);
        try {
            var task = (Task)_engine.RunAllAsync(game.Name, handlers.Output, handlers.Event, handlers.Stdin);
            await task.ConfigureAwait(false);
        } catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) {
            await Dispatcher.UIThread.InvokeAsync(async () => {
                await PromptHelpers.InfoAsync("Run-all is not available in this engine build.", "Run Build");
            });
            RemoveInstallRow(contextId);
        } catch {
            RemoveInstallRow(contextId);
            throw;
        }
    }

    private Task<bool> StartInstallAsync(string moduleName) {
        DebugWriteLine($"Starting install for module '{moduleName}'");
        CurrentTabIndex = 2;
        string contextId = $"install:{moduleName}";
        EnsureInstallRow(contextId, moduleName, OperationKind.Install);
        ClearPromptState();

        var handlers = CreateEngineDelegates(contextId, moduleName, OperationKind.Install, refreshOnComplete: true);
        return (Task<bool>)_engine.InstallModuleAsync(moduleName, handlers.Output, handlers.Event, handlers.Stdin);
    }

    private static void DebugWriteLine(string message) {
#if DEBUG
        Console.WriteLine(message);
#endif
    }

    private (Delegate? Output, Delegate? Event, Delegate? Stdin) CreateEngineDelegates(string contextId, string displayName, OperationKind kind, bool refreshOnComplete) {
        DebugWriteLine($"Creating engine delegates for context '{contextId}'");
        Action<string, string> outputHandler = (line, _) => AppendLogLine(line);

        Action<Dictionary<string, object?>> eventHandler = evt => {
            Dictionary<string, object?> payload = new Dictionary<string, object?>(evt, StringComparer.OrdinalIgnoreCase) {
                ["context"] = contextId
            };
            HandleEngineEvent(contextId, displayName, payload, kind, refreshOnComplete);
        };

        Func<string?> stdinProvider = () => {
            var tcs = _activePromptTcs;
            if (tcs is null) {
                return string.Empty;
            }

            try {
                return tcs.Task.GetAwaiter().GetResult() ?? string.Empty;
            } finally {
                _activePromptTcs = null;
            }
        };

        Delegate? outDel = _outputHandlerType is null ? null : Delegate.CreateDelegate(_outputHandlerType, outputHandler.Target!, outputHandler.Method);
        Delegate? evtDel = _eventHandlerType is null ? null : Delegate.CreateDelegate(_eventHandlerType, eventHandler.Target!, eventHandler.Method);
        Delegate? stdinDel = _stdinProviderType is null ? null : Delegate.CreateDelegate(_stdinProviderType, stdinProvider.Target!, stdinProvider.Method);

        return (outDel, evtDel, stdinDel);
    }

    private void HandleEngineEvent(string contextId, string displayName, Dictionary<string, object?> evt, OperationKind kind, bool refreshOnComplete) {
        DebugWriteLine($"Handling engine event for context '{contextId}': {string.Join(", ", evt.Select(kv => $"{kv.Key}={kv.Value}"))}");
        string eventType = evt.TryGetValue("event", out object? value) ? value?.ToString()?.ToLowerInvariant() ?? string.Empty : string.Empty;
        if (string.IsNullOrEmpty(eventType)) {
            return;
        }

        switch (eventType) {
            case "prompt": {
                string message = evt.TryGetValue("message", out object? msg) ? msg?.ToString() ?? "Input required" : "Input required";
                bool secret = evt.TryGetValue("secret", out object? sec) && ConvertToBool(sec);
                string? def = evt.TryGetValue("default", out object? defVal) ? defVal?.ToString() : null;
                RequestPrompt(message, secret, def);
                AppendLogLine($"[Prompt] {message}");
                break;
            }
            case "print":
            case "info": {
                string message = evt.TryGetValue("message", out object? msg) ? msg?.ToString() ?? string.Empty : string.Empty;
                AppendLogLine(message);
                break;
            }
            case "warning": {
                string message = evt.TryGetValue("message", out object? msg) ? msg?.ToString() ?? string.Empty : string.Empty;
                AppendLogLine($"⚠ {message}");
                break;
            }
            case "error": {
                string message = evt.TryGetValue("message", out object? msg) ? msg?.ToString() ?? string.Empty : string.Empty;
                AppendLogLine($"✖ {message}");
                break;
            }
            case "progress": {
                string label = evt.TryGetValue("label", out object? lbl) ? lbl?.ToString() ?? "Working" : "Working";
                int current = ToInt(evt.TryGetValue("current", out object? cur) ? cur : null);
                int total = ToInt(evt.TryGetValue("total", out object? tot) ? tot : null);
                UpdateInstallProgress(contextId, displayName, label, current, total);
                break;
            }
            case "start": {
                AppendLogLine($"Starting {displayName}…");
                break;
            }
            case "end": {
                bool success = evt.TryGetValue("success", out object? suc) ? ConvertToBool(suc) : true;
                AppendLogLine(success ? $"{displayName} completed successfully." : $"{displayName} failed.");
                if (refreshOnComplete) {
                    Dispatcher.UIThread.Post(() => {
                        RefreshStore();
                        RefreshLibrary();
                    });
                }
                RemoveInstallRow(contextId);
                break;
            }
            case "run-all-start": {
                int total = ToInt(evt.TryGetValue("total", out object? tot) ? tot : null);
                AppendLogLine($"Running build for {displayName} ({total} operation(s)).");
                var row = EnsureInstallRow(contextId, displayName, kind);
                Dispatcher.UIThread.Post(() => {
                    row.SetStepProgress(0, total);
                    row.Label = total > 0 ? $"Queued operations: {total}" : "Queued operations";
                });
                break;
            }
            case "run-all-op-start": {
                string opName = evt.TryGetValue("name", out object? nm) ? nm?.ToString() ?? displayName : displayName;
                AppendLogLine($"> {opName}");
                var row = EnsureInstallRow(contextId, displayName, kind);
                Dispatcher.UIThread.Post(() => row.Label = $"Running {opName}");
                break;
            }
            case "run-all-op-end": {
                string opName = evt.TryGetValue("name", out object? nm) ? nm?.ToString() ?? displayName : displayName;
                bool success = evt.TryGetValue("success", out object? suc) && ConvertToBool(suc);
                AppendLogLine((success ? "✔ " : "✖ ") + opName);
                int index = ToInt(evt.TryGetValue("index", out object? idx) ? idx : null);
                int total = ToInt(evt.TryGetValue("total", out object? tot) ? tot : null);
                var row = EnsureInstallRow(contextId, displayName, kind);
                Dispatcher.UIThread.Post(() => {
                    row.SetStepProgress(index + 1, total);
                    row.Label = success ? $"Completed {opName}" : $"Failed {opName}";
                });
                break;
            }
            case "run-all-op-error": {
                string message = evt.TryGetValue("message", out object? msg) ? msg?.ToString() ?? "Operation failed." : "Operation failed.";
                AppendLogLine($"✖ {message}");
                break;
            }
            case "run-all-complete": {
                bool success = evt.TryGetValue("success", out object? suc) && ConvertToBool(suc);
                AppendLogLine(success ? $"Build for {displayName} finished successfully." : $"Build for {displayName} finished with errors.");
                RemoveInstallRow(contextId);
                Dispatcher.UIThread.Post(() => {
                    RefreshLibrary();
                    RefreshStore();
                });
                break;
            }
        }
    }

    private void RequestPrompt(string message, bool secret, string? defaultValue) {
        DebugWriteLine($"Requesting prompt: {message} (secret={secret})");
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _activePromptTcs = tcs;

        Dispatcher.UIThread.Post(async () => {
            PromptQuestion = message;
            PromptAnswer = defaultValue ?? string.Empty;
            PromptIsVisible = true;
            OnPropertyChanged(nameof(PromptQuestion));
            OnPropertyChanged(nameof(PromptAnswer));

            string? result = null;
            bool closeAutomatically = false;
            try {
                result = await PromptHelpers.TextAsync("Input Required", message, defaultValue, secret);
                if (result is not null) {
                    closeAutomatically = true;
                }
            } catch {
                result = null;
            }

            if (closeAutomatically) {
                tcs.TrySetResult(result ?? string.Empty);
                PromptIsVisible = false;
                PromptAnswer = string.Empty;
                OnPropertyChanged(nameof(PromptQuestion));
                OnPropertyChanged(nameof(PromptAnswer));
            }
        });
    }

    private void AppendLogLine(string line) {
        DebugWriteLine($"Appending log line: {line}");
        if (string.IsNullOrWhiteSpace(line)) {
            return;
        }

        Dispatcher.UIThread.Post(() => {
            ConsoleText = string.IsNullOrEmpty(ConsoleText) ? line : $"{ConsoleText}\n{line}";
        });
    }

    private InstallRow EnsureInstallRow(string contextId, string displayName, OperationKind kind = OperationKind.Install) {
        DebugWriteLine($"Ensuring install row for context '{contextId}'");
        if (_rows.TryGetValue(contextId, out var existing)) {
            return existing;
        }

        var row = new InstallRow {
            ContextId = contextId,
            Kind = kind,
            Name = displayName,
            Label = kind == OperationKind.Build ? "Preparing build…" : "Preparing…",
            Progress = 0.0
        };
        _rows[contextId] = row;
        Dispatcher.UIThread.Post(() => Installing.Add(row));
        return row;
    }

    private void UpdateInstallProgress(string contextId, string displayName, string label, int current, int total) {
        DebugWriteLine($"Updating install progress for context '{contextId}': {label} ({current}/{total})");
        var row = EnsureInstallRow(contextId, displayName);
        double pct = total > 0 ? Math.Clamp((double)current / Math.Max(1, total), 0.0, 1.0) : 0.0;
        Dispatcher.UIThread.Post(() => {
            row.Label = total > 0 ? $"{label} ({current}/{total})" : label;
            row.Progress = pct;
        });
    }

    private void RemoveInstallRow(string contextId) {
        DebugWriteLine($"Removing install row for context '{contextId}'");
        if (!_rows.TryGetValue(contextId, out var row)) {
            return;
        }

        _rows.Remove(contextId);
        Dispatcher.UIThread.Post(() => Installing.Remove(row));
        ClearPromptState();
    }

    private bool TryLaunchGame(string name) {
        try {
            DebugWriteLine($"Attempting to launch game '{name}'");
            return _engine.LaunchGame(name);
        } catch {
            return false;
        }
    }

    private void OpenFolder(GameItem item) {
        DebugWriteLine($"Opening folder for game '{item.Name}'");
        try {
            var path = (string?)_engine.GetGamePath(item.Name);
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path)) {
                PromptHelpers.InfoAsync($"Couldn't locate a folder for '{item.Name}'.", "Open Folder");
                return;
            }

            var psi = new System.Diagnostics.ProcessStartInfo { UseShellExecute = true };
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
            // ignore
        }
    }

    private void ClearPromptState() {
        DebugWriteLine("Clearing prompt state");
        _activePromptTcs = null;
        PromptIsVisible = false;
        PromptQuestion = null;
        PromptAnswer = string.Empty;
        OnPropertyChanged(nameof(PromptQuestion));
        OnPropertyChanged(nameof(PromptAnswer));
    }

    private static bool ConvertToBool(object? value) {
        DebugWriteLine($"Converting to bool: {value}");
        return value switch {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            IConvertible convertible => convertible.ToInt32(System.Globalization.CultureInfo.InvariantCulture) != 0,
            _ => false
        };
    }

    private static int ToInt(object? value) {
        DebugWriteLine($"Converting to int: {value}");
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
            return (int)d;
        }

        if (value is string s && int.TryParse(s, out var parsed)) {
            return parsed;
        }

        try {
            return Convert.ToInt32(value);
        } catch {
            return 0;
        }
    }

    private static string? TryGetString(IDictionary<string, object?>? dict, string key) {
        DebugWriteLine($"Trying to get string for key '{key}'");
        if (dict is null) {
            return null;
        }

        return dict.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public sealed class GameItem {
    public string Name { get; set; } = string.Empty;
    public string? Title {
        get; set;
    }
    public IDictionary<string, object?>? Info {
        get; set;
    }
    public string? ExePath {
        get; set;
    }
    public string? GameRoot {
        get; set;
    }
    public bool IsPlaceholder {
        get; set;
    }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) || string.Equals(Title, Name, StringComparison.OrdinalIgnoreCase)
        ? Name
        : $"{Title} ({Name})";

    public bool HasGameRoot => !string.IsNullOrWhiteSpace(GameRoot);
}

public sealed class StoreItem {
    public string Name { get; set; } = string.Empty;
    public object? Meta {
        get; set;
    }
    public string? Url {
        get; set;
    }
    public string State { get; set; } = string.Empty;
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

    public string ContextId { get; set; } = string.Empty;
    internal OperationKind Kind { get; set; } = OperationKind.Install;
    public string Name { get; set; } = string.Empty;
    public string Label {
        get => _label;
        set {
            if (_label != value) {
                _label = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Label)));
            }
        }
    }

    public double Progress {
        get => _progress;
        set {
            if (Math.Abs(_progress - value) > 0.0001) {
                _progress = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Progress)));
            }
        }
    }

    public void SetStepProgress(int completed, int total) {
        double pct = total > 0 ? Math.Clamp((double)completed / Math.Max(1, total), 0.0, 1.0) : 0.0;
        Progress = pct;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class RelayCommand:System.Windows.Input.ICommand {
    private readonly Action<object?> _action;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> action, Func<object?, bool>? canExecute = null) {
        _action = action;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) {
        if (!CanExecute(parameter)) {
            return;
        }

        _action(parameter);
    }

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class AsyncRelayCommand:System.Windows.Input.ICommand {
    private readonly Func<object?, Task> _action;
    private readonly Func<object?, bool>? _canExecute;
    private bool _busy;

    public AsyncRelayCommand(Func<object?, Task> action, Func<object?, bool>? canExecute = null) {
        _action = action;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => !_busy && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter) {
        if (!CanExecute(parameter)) {
            return;
        }

        _busy = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try {
            await _action(parameter);
        } finally {
            _busy = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CanExecuteChanged;
}

internal enum OperationKind {
    Install,
    Build
}

