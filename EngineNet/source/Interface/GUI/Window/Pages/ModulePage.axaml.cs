
namespace EngineNet.GUI.Pages;

using Avalonia.Media;
using Core.Data;
using EngineNet.Shared.IO.UI;

internal sealed partial class ModulePage:UserControl, INotifyPropertyChanged {

    /* :: :: Vars :: START :: */
    private readonly string _moduleName;

    private readonly List<SessionOperation> _initOperations = new List<SessionOperation>();


    public string ModuleName { get; }
    public string Title { get; private set; } = string.Empty;
    public string? GameRoot { get; private set; }
    public string? ExePath { get; private set; }
    public string? RegistryUrl { get; private set; }

    public bool IsBuilt { get; private set; }
    public bool IsInstalled { get; private set; }
    public bool IsRegistered { get; private set; }
    public bool IsUnverified { get; private set; }
    public bool IsUnbuilt { get; private set; }

    public bool IsExecutionEnabled { get; private set; } = true;

    public bool CanPlay => IsBuilt && !string.IsNullOrWhiteSpace(ExePath) && CanStartOperation;
    public bool CanRunAll => !string.IsNullOrWhiteSpace(ModuleName) && !IsRunning && IsExecutionEnabled;
    public bool CanRunOperation => CanStartOperation;
    public bool CanStop => IsRunning;
    public bool CanDownload => !IsDownloaded() && !string.IsNullOrWhiteSpace(RegistryUrl);

    private bool IsRunning { get; set; }
    private bool CanStartOperation => IsExecutionEnabled && !IsRunning;
    private CancellationTokenSource? _cts;

    public Bitmap? Image { get; private set; }

    public ObservableCollection<OpRow> Operations { get; } = new ObservableCollection<OpRow>();

    public System.Windows.Input.ICommand Button_Play_Click { get; }
    public System.Windows.Input.ICommand Button_RunAll_Click { get; }
    public System.Windows.Input.ICommand Button_Stop_Click { get; }
    public System.Windows.Input.ICommand Button_RunOp_Click { get; }
    public System.Windows.Input.ICommand Button_Download_Click { get; }
    public System.Windows.Input.ICommand Button_OpenFolder_Click { get; }
    /* :: :: Vars :: END :: */
    // //
    /* :: :: Constructors :: START :: */

    public ModulePage() {
        ModuleName = this._moduleName = "demo";

        Button_Play_Click = new Cmd(async _ => await PlayAsync());
        Button_RunAll_Click = new Cmd(async _ => await RunAllAsync());
        Button_Stop_Click = new Cmd(async _ => Stop());
        Button_RunOp_Click = new Cmd(async p => await RunOpAsync(p as OpRow));
        Button_Download_Click = new Cmd(async _ => await DownloadAsync());
        Button_OpenFolder_Click = new Cmd(async _ => await OpenFolderAsync());

        DataContext = this;
        InitializeComponent();

        this.Loaded += OnLoaded;

        Load();
    }

    internal ModulePage(string moduleName) {
        ModuleName = this._moduleName = moduleName;

        Button_Play_Click = new Cmd(async _ => await PlayAsync());
        Button_RunAll_Click = new Cmd(async _ => await RunAllAsync());
        Button_Stop_Click = new Cmd(async _ => Stop());
        Button_RunOp_Click = new Cmd(async p => await RunOpAsync(p as OpRow));
        Button_Download_Click = new Cmd(async _ => await DownloadAsync());
        Button_OpenFolder_Click = new Cmd(async _ => await OpenFolderAsync());

        DataContext = this;
        InitializeComponent();

        this.Loaded += OnLoaded;

        Load();
    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Methods :: START :: */
    private async void OnLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) {
        if (_initOperations.Count == 0 || GuiBootstrapper.MiniEngine is null) {
            IsExecutionEnabled = true;
            Raise(nameof(CanPlay));
            Raise(nameof(CanRunAll));
            Raise(nameof(CanRunOperation));
            return;
        }

        IsExecutionEnabled = false;
        Raise(nameof(CanPlay));
        Raise(nameof(CanRunAll));
        Raise(nameof(CanRunOperation));

        // Auto-run init operations only when the engine-owned session does not report a successful run.
        bool shouldRunInit = false;
        foreach (SessionOperation operation in _initOperations) {
            if (operation.Status == OperationExecutionStatus.Succeeded) continue;
            shouldRunInit = true;
            break;
        }

        if (shouldRunInit) {
            await ExecuteInitOperationsAsync();
        }

        IsExecutionEnabled = true;
        Raise(nameof(CanPlay));
        Raise(nameof(CanRunAll));
        Raise(nameof(CanRunOperation));
    }

    private void Load() {
        try {
            if (GuiBootstrapper.MiniEngine is null) {
                throw new InvalidOperationException(message: "Engine is not initialized.");
            }

            // Clear ops
            Operations.Clear();
            _initOperations.Clear();

            // Gather module info from multiple sources
            Core.Data.GameModules modules = GuiBootstrapper.MiniEngine.GameRegistry_GetModules(Core.Data.ModuleFilter.All);
            Core.Data.GameModuleInfo? m = modules.GetValueOrDefault(_moduleName);
            if (m is not null) {
                Title = string.IsNullOrWhiteSpace(m.Title) ? m.Name : m.Title;
                ExePath = m.ExePath;
                GameRoot = m.GameRoot;
                IsBuilt = m.IsBuilt;
                IsInstalled = m.IsInstalled;
                IsRegistered = m.IsRegistered;
                IsUnverified = m.IsUnverified;
                IsUnbuilt = m is { IsInstalled: true, IsBuilt: false };
                Image = ResolveCoverBitmap(gameRoot: GameRoot);
            } else {
                Title = _moduleName;
                Image = ResolveCoverBitmap(gameRoot: EngineNet.Shared.State.RootPath);
            }

            // Registry info (URL)
            IReadOnlyDictionary<string, object?> regs = GuiBootstrapper.MiniEngine.GameRegistry_GetRegisteredModules();
            if (regs.TryGetValue(_moduleName, out object? regObj) && regObj is IDictionary<string, object?> reg) {
                RegistryUrl = reg.TryGetValue(key: "url", value: out object? u) ? u?.ToString() : null;
                if (string.IsNullOrWhiteSpace(Title)) {
                    string? title = reg.TryGetValue(key: "title", value: out object? t) ? t?.ToString() : null;
                    Title = string.IsNullOrWhiteSpace(title) ? _moduleName : title;
                }
            }

            Core.Data.GameModules games = GuiBootstrapper.MiniEngine.GameRegistry_GetModules(Core.Data.ModuleFilter.All);
            ModuleOperationSession session = GuiBootstrapper.MiniEngine.OperationsService_LoadModuleSession(_moduleName, games);
            if (session.Module is not null) {
                if (string.IsNullOrWhiteSpace(ExePath)) {
                    ExePath = session.Module.ExePath;
                }

                if (string.IsNullOrWhiteSpace(GameRoot)) {
                    GameRoot = session.Module.GameRoot;
                }

                Core.Data.PreparedOperations preparedOps = session.PreparedOperations;
                if (!preparedOps.IsLoaded) {
                    Shared.IO.Diagnostics.Log($"Load: Failed to load operations list for module {_moduleName}. {preparedOps.ErrorMessage}");
                    return;
                }

                if (preparedOps.Warnings.Count > 0) {
                    foreach (string warning in preparedOps.Warnings) {
                        Shared.IO.Diagnostics.Log($"[ModulePage::Load()] Warning: {warning}");
                        OperationOutputService.Instance.AddOutput($"Validation: {warning}", "stderr");
                    }
                }

                _initOperations.AddRange(session.InitOperations);

                foreach (SessionOperation sessionOperation in session.RegularOperations) {
                    Core.Data.PreparedOperation op = sessionOperation.Operation;
                    string scriptType = string.IsNullOrWhiteSpace(op.ScriptType) ? "python" : op.ScriptType;
                    string scriptPath = op.ScriptPath ?? string.Empty;
                    string displayName = op.DisplayName;
                    if (op.HasDuplicateId) {
                        displayName = $"[dup-id] {displayName}";
                    } else if (op.HasInvalidId) {
                        displayName = $"[invalid-id] {displayName}";
                    }

                    bool? lastRunSuccess = sessionOperation.Status == OperationExecutionStatus.NotRun
                        ? null
                        : sessionOperation.Status == OperationExecutionStatus.Succeeded;

                    Operations.Add(item: new OpRow {
                        Name = displayName,
                        ScriptType = scriptType,
                        ScriptPath = scriptPath,
                        Op = op.Operation,
                        LastRunSuccess = lastRunSuccess
                    });
                }
            } else {
                Shared.IO.Diagnostics.Log($"Load: No game info found for module {_moduleName}.");
            }

            Raise(nameof(Title));
            Raise(nameof(ExePath));
            Raise(nameof(GameRoot));
            Raise(nameof(IsBuilt));
            Raise(nameof(IsInstalled));
            Raise(nameof(IsRegistered));
            Raise(nameof(IsUnverified));
            Raise(nameof(IsUnbuilt));
            Raise(nameof(CanPlay));
            Raise(nameof(CanRunAll));
            Raise(nameof(CanRunOperation));
            Raise(nameof(CanDownload));
            Raise(nameof(Image));
            Raise(nameof(RegistryUrl));
        } catch (System.Exception ex) {
            OperationOutputService.Instance.AddOutput(text: $"Module load failed: {ex.Message}", stream: "stderr");
            Shared.IO.Diagnostics.Bug($"Load: {ex}");
        }
    }

    private Bitmap? ResolveCoverBitmap(string? gameRoot) {
        if (GuiBootstrapper.MiniEngine == null) {
            return null;
        }
        string? icon = string.IsNullOrWhiteSpace(gameRoot) ? null : System.IO.Path.Combine(path1: gameRoot, path2: "icon.png");
        string placeholder = System.IO.Path.Combine(path1: EngineNet.Shared.State.RootPath, path2: "placeholder.png");
        string pick = (!string.IsNullOrWhiteSpace(icon) && System.IO.File.Exists(path: icon)) ? icon : placeholder;
        try {
            return System.IO.File.Exists(path: pick) ? new Bitmap(pick) : null;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"ResolveCoverBitmap: Failed to load bitmap from {pick}. {ex}");
            return null;
        }
    }

    private async System.Threading.Tasks.Task ExecuteInitOperationsAsync() {
        if (_initOperations.Count == 0 || GuiBootstrapper.MiniEngine is null || IsRunning) return;

        IsRunning = true;
        _cts = new CancellationTokenSource();
        Raise(nameof(CanPlay));
        Raise(nameof(CanRunAll));
        Raise(nameof(CanRunOperation));
        Raise(nameof(CanStop));

        try {
            Core.Data.GameModules games = GuiBootstrapper.MiniEngine.GameRegistry_GetModules(Core.Data.ModuleFilter.All);

            await EngineOperationRunner.RunAsync(
                moduleName: ModuleName,
                operationName: "Initialization Ops",
                executor: async (_, onEvent, stdin) => {
                    Shared.IO.UI.EngineSdk.LocalEventSink = e => onEvent(e);
                    Shared.IO.UI.EngineSdk.MuteStdoutWhenLocalSink = true;

                    System.IO.TextReader previous = System.Console.In;
                    try {
                        System.Console.SetIn(new GuiStdinRedirectReader(provider: stdin));
                        bool okAllInit = true;
                        foreach (SessionOperation sessionOperation in _initOperations) {
                            PreparedOperation op = sessionOperation.Operation;
                            Core.Data.PromptAnswers answers = new Core.Data.PromptAnswers();
                            await CollectAnswersForOperationAsync(op: op.Operation, answers: answers, defaultsOnly: true);

                            bool ok = await GuiBootstrapper.MiniEngine.RunSingleOperationAsync(
                                currentGame: ModuleName,
                                games,
                                op: op.Operation,
                                answers,
                                cancellationToken: _cts.Token
                            );
                            okAllInit &= ok;
                        }
                        return okAllInit;
                    } finally {
                        try {
                            System.Console.SetIn(previous);
                        } catch (Exception ex) {
                            System.Console.SetIn(previous);
                            Shared.IO.Diagnostics.Bug($"ExecuteInitOperationsAsync: Failed to restore Console.In. {ex}");
                        }
                    }
                }
            );
        } catch (System.OperationCanceledException) {
            OperationOutputService.Instance.AddOutput(text: "Init operations cancelled by user.", stream: "stderr");
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"ExecuteInitOperationsAsync: {ex}");
            OperationOutputService.Instance.AddOutput(text: $"Init operations failed: {ex.Message}", stream: "stderr");
        } finally {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            Raise(nameof(CanPlay));
            Raise(nameof(CanRunAll));
            Raise(nameof(CanRunOperation));
            Raise(nameof(CanStop));
        }
    }

    private async System.Threading.Tasks.Task PlayAsync() {
        if (GuiBootstrapper.MiniEngine is null || string.IsNullOrWhiteSpace(ModuleName) || IsRunning) return;

        IsRunning = true;
        _cts = new CancellationTokenSource();
        Raise(nameof(CanPlay));
        Raise(nameof(CanRunAll));
        Raise(nameof(CanRunOperation));
        Raise(nameof(CanStop));
        try {
            await EngineOperationRunner.RunAsync(
                moduleName: ModuleName,
                operationName: "Play",
                executor: async (_, onEvent, stdin) => {
                    // Temporarily redirect engine events/output for this launch
                    var previousSink = Shared.IO.UI.EngineSdk.LocalEventSink;
                    bool previousMute = Shared.IO.UI.EngineSdk.MuteStdoutWhenLocalSink;
                    var previousIn = System.Console.In;

                    Shared.IO.UI.EngineSdk.LocalEventSink = e => onEvent(e);
                    Shared.IO.UI.EngineSdk.MuteStdoutWhenLocalSink = true;

                    try {
                        System.Console.SetIn(new GuiStdinRedirectReader(provider: stdin));
                        return await GuiBootstrapper.MiniEngine.GameLauncher_LaunchGameAsync(name: ModuleName, cancellationToken: _cts.Token);
                    } finally {
                        Shared.IO.UI.EngineSdk.LocalEventSink = previousSink;
                        Shared.IO.UI.EngineSdk.MuteStdoutWhenLocalSink = previousMute;
                        System.Console.SetIn(previousIn);
                    }
                }
            );
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"PlayAsync: {ex}");
            OperationOutputService.Instance.AddOutput(text: $"Launch failed: {ex.Message}", stream: "stderr");
        } finally {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            Raise(nameof(CanPlay));
            Raise(nameof(CanRunAll));
            Raise(nameof(CanRunOperation));
            Raise(nameof(CanStop));
        }
    }

    private async System.Threading.Tasks.Task RunAllAsync() {
        if (GuiBootstrapper.MiniEngine is null || string.IsNullOrWhiteSpace(ModuleName) || IsRunning) return;

        IsRunning = true;
        _cts = new CancellationTokenSource();
        Raise(nameof(CanPlay));
        Raise(nameof(CanRunAll));
        Raise(nameof(CanRunOperation));
        Raise(nameof(CanStop));

        try {
            await EngineOperationRunner.RunAsync(
                moduleName: ModuleName,
                operationName: "Run All",
                executor: async (onOutput, onEvent, stdin) => {
                    var res = await GuiBootstrapper.MiniEngine.RunAllAsync(gameName: ModuleName, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdin, cancellationToken: _cts.Token);
                    return res.Success;
                }
            );
            Load();
        } catch (System.OperationCanceledException) {
            OperationOutputService.Instance.AddOutput(text: "Operation cancelled by user.", stream: "stderr");
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"RunAllAsync: {ex}");
            OperationOutputService.Instance.AddOutput(text: $"Run All failed: {ex.Message}", stream: "stderr");
        } finally {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            Raise(nameof(CanPlay));
            Raise(nameof(CanRunAll));
            Raise(nameof(CanRunOperation));
            Raise(nameof(CanStop));
        }
    }

    private async void Stop() {
        this._cts?.Cancel();
    }

    private async System.Threading.Tasks.Task RunOpAsync(OpRow? row) {
        if (row is null || GuiBootstrapper.MiniEngine is null || IsRunning) return;

        IsRunning = true;
        _cts = new CancellationTokenSource();
        Raise(nameof(CanPlay));
        Raise(nameof(CanRunAll));
        Raise(nameof(CanRunOperation));
        Raise(nameof(CanStop));

        try {
            Core.Data.GameModules games = GuiBootstrapper.MiniEngine.GameRegistry_GetModules(Core.Data.ModuleFilter.All);

            Core.Data.PromptAnswers promptAnswers = new Core.Data.PromptAnswers();
            await CollectAnswersForOperationAsync(op: row.Op, answers: promptAnswers);

            // Use embedded execution path (Engine handles engine/lua/js/bms in-process)
            await EngineOperationRunner.RunAsync(
                moduleName: ModuleName,
                operationName: row.Name,
                executor: async (_, onEvent, stdin) => {
                    Shared.IO.UI.EngineSdk.LocalEventSink = e => onEvent(e);
                    Shared.IO.UI.EngineSdk.MuteStdoutWhenLocalSink = true;

                    System.IO.TextReader previous = System.Console.In;
                    try {
                        System.Console.SetIn(new GuiStdinRedirectReader(provider: stdin));
                        bool ok = await GuiBootstrapper.MiniEngine.RunSingleOperationAsync(
                            currentGame: ModuleName,
                            games,
                            op: row.Op,
                            promptAnswers,
                            cancellationToken: _cts.Token
                        );
                        return ok;
                    } finally {
                        try {
                            System.Console.SetIn(previous);
                        } catch (Exception ex) {
                            System.Console.SetIn(previous);
                            Shared.IO.Diagnostics.Bug($"RunOpAsync: Failed to restore Console.In. {ex}");
                        }
                    }
                }
            );
            Load();
        } catch (System.OperationCanceledException) {
            OperationOutputService.Instance.AddOutput(text: "Operation cancelled by user.", stream: "stderr");
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"RunOpAsync: {ex}");
            OperationOutputService.Instance.AddOutput(text: $"Operation failed: {ex.Message}", stream: "stderr");
        } finally {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            Raise(nameof(CanPlay));
            Raise(nameof(CanRunAll));
            Raise(nameof(CanRunOperation));
            Raise(nameof(CanStop));
        }
    }

    private async System.Threading.Tasks.Task DownloadAsync() {
        if (GuiBootstrapper.MiniEngine is null) return;
        if (string.IsNullOrWhiteSpace(RegistryUrl)) return;
        try {
            await EngineOperationRunner.RunAsync(
                moduleName: ModuleName,
                operationName: $"Download {ModuleName}",
                executor: async (onOutput, onEvent, _) => {
                    onEvent(new Dictionary<string, object?> { ["event"] = EngineSdk.Events.Start, ["name"] = ModuleName, ["url"] = RegistryUrl });
                    bool result = await System.Threading.Tasks.Task.Run(function: () => GuiBootstrapper.MiniEngine.GitService_CloneModule(RegistryUrl!));
                    onOutput(result ? $"Download complete for {ModuleName}." : $"Download failed for {ModuleName}.", result ? "stdout" : "stderr");
                    onEvent(new Dictionary<string, object?> { ["event"] = EngineSdk.Events.End, ["success"] = result, ["name"] = ModuleName });
                    return result;
                }
            );
            Load();
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"DownloadAsync: {ex}");
            OperationOutputService.Instance.AddOutput(text: $"Download failed: {ex.Message}", stream: "stderr");
        }
    }

    private async System.Threading.Tasks.Task OpenFolderAsync() {
        if (GuiBootstrapper.MiniEngine is null) return;
        string? path = GuiBootstrapper.MiniEngine.GameRegistry_GetGamePath(name: ModuleName);
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path: path)) {
            return;
        }
        GuiBootstrapper.MiniEngine.CommandService_OpenFolder(path);
        await System.Threading.Tasks.Task.CompletedTask;
    }

    private bool IsDownloaded() {
        if (GuiBootstrapper.MiniEngine is null) return false;
        string path = System.IO.Path.Combine(path1: EngineNet.Shared.State.RootPath, path2: System.IO.Path.Combine("EngineApps", "Games", _moduleName));
        return System.IO.Directory.Exists(path: path);
    }

    private async System.Threading.Tasks.Task CollectAnswersForOperationAsync(Dictionary<string, object?> op, Core.Data.PromptAnswers answers, bool defaultsOnly = false) {
        if (GuiBootstrapper.MiniEngine is null) {
            return;
        }

        static async Task<PromptResponse> promptHandler(PromptRequest request, CancellationToken cancellationToken) {
            switch (request.Type) {
                case EngineSdk.Events.Confirm: {
                    bool defVal = request.DefaultValue is bool b && b;
                    bool? res = await OperationOutputService.Instance.RequestConfirmPromptAsync(title: request.Title, message: request.Title, defaultValue: defVal);
                    if (res == null) {
                        return Core.Data.PromptResponse.Cancelled();
                    }

                    return Core.Data.PromptResponse.FromValue(res.Value);
                }
                case "select": {
                    string hint = request.Choices.Count > 0
                        ? $"Choices: {string.Join(", ", request.Choices.Select(choice => choice.Label))}"
                        : "No choices provided";
                    string? v = await OperationOutputService.Instance.RequestTextPromptAsync(title: request.Title, message: hint, defaultValue: request.DefaultValue?.ToString(), secret: request.IsSecret);
                    if (string.IsNullOrWhiteSpace(v)) {
                        return request.DefaultValue is not null
                            ? Core.Data.PromptResponse.UseDefaultValue()
                            : Core.Data.PromptResponse.FromValue(string.Empty);
                    }

                    return Core.Data.PromptResponse.FromValue(v);
                }
                case "checkbox": {
                    string hint = request.Choices.Count > 0
                        ? $"Enter comma-separated values (Choices: {string.Join(", ", request.Choices.Select(choice => choice.Label))})"
                        : "Enter comma-separated values";
                    string? v = await OperationOutputService.Instance.RequestTextPromptAsync(title: request.Title, message: hint, defaultValue: null, secret: request.IsSecret);

                    if (string.IsNullOrWhiteSpace(v)) {
                        if (request.DefaultValue is IList<object?>) {
                            return Core.Data.PromptResponse.UseDefaultValue();
                        }

                        return Core.Data.PromptResponse.FromValue(new List<object?>());
                    }

                    List<object?> list = new List<object?>();
                    string[] parts = v.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
                    foreach (string s in parts) {
                        list.Add(s);
                    }

                    return Core.Data.PromptResponse.FromValue(list);
                }
                //case "text":
                default: {
                    string? def = request.DefaultValue?.ToString();
                    string? v = await OperationOutputService.Instance.RequestTextPromptAsync(title: request.Title, message: request.Title, defaultValue: def, secret: request.IsSecret);
                    if (string.IsNullOrWhiteSpace(v)) {
                        return request.DefaultValue is not null
                            ? Core.Data.PromptResponse.UseDefaultValue()
                            : Core.Data.PromptResponse.FromValue(string.Empty);
                    }

                    return Core.Data.PromptResponse.FromValue(v);
                }
            }
        }

        await GuiBootstrapper.MiniEngine.OperationsService_CollectAnswersAsync(op, answers, promptHandler, defaultsOnly);
    }

    /* :: :: Methods :: END :: */
    // //
    /* :: :: Nested Types :: START :: */
    /// <summary>
    /// Represents a single operation row in the module view.
    /// </summary>
    public sealed class OpRow {
        public string Name { get; init; } = string.Empty;
        public string ScriptType { get; set; } = string.Empty;
        public string ScriptPath { get; set; } = string.Empty;
        public Dictionary<string, object?> Op { get; init; } = new Dictionary<string, object?>();

        /// <summary>
        /// Latest run success for this operation, based on operation_execution.log.
        /// </summary>
        public bool? LastRunSuccess { get; init; }

        /// <summary>
        /// Whether a run status is available for display.
        /// </summary>
        public bool HasRunStatus => LastRunSuccess.HasValue;

        /// <summary>
        /// Display label for the latest run status.
        /// </summary>
        public string RunStatusText => LastRunSuccess.HasValue
            ? (LastRunSuccess.Value ? "Completed" : "Failed")
            : string.Empty;

        /// <summary>
        /// Foreground brush for the latest run status label.
        /// </summary>
        public IBrush? RunStatusForeground => LastRunSuccess.HasValue
            ? (LastRunSuccess.Value ? Brushes.Green : Brushes.OrangeRed)
            : null;
    }

    private sealed class GuiStdinRedirectReader:System.IO.TextReader {
        private readonly Core.ProcessRunner.StdinProvider _provider;
        internal GuiStdinRedirectReader(Core.ProcessRunner.StdinProvider provider) { _provider = provider; }
        public override string? ReadLine() { return _provider(); }
    }

    private event PropertyChangedEventHandler? _propertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged {
        add => _propertyChanged += value;
        remove => _propertyChanged -= value;
    }

    private void Raise(string name) => _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName: name));

    private sealed class Cmd:System.Windows.Input.ICommand {
        private readonly System.Func<object?, System.Threading.Tasks.Task> _run;
        internal Cmd(System.Func<object?, System.Threading.Tasks.Task> run) { _run = run; }
        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter) => await _run(parameter);
        public event System.EventHandler? CanExecuteChanged {
            add { }
            remove { }
        }
    }
    /* :: :: Nested Types :: END :: */
}
