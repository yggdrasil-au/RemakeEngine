using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Media.Imaging;
using System.Diagnostics;
using System.Threading;

namespace EngineNet.Interface.GUI.Pages;

public sealed partial class ModulePage:UserControl, INotifyPropertyChanged {

    /* :: :: Vars :: START :: */
    private readonly string _moduleName = string.Empty;

    public string ModuleName { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? GameRoot { get; private set; }
    public string? ExePath { get; private set; }
    public string? RegistryUrl { get; private set; }

    public bool IsBuilt { get; private set; }
    public bool IsInstalled { get; private set; }
    public bool IsRegistered { get; private set; }
    public bool IsUnverified { get; private set; }
    public bool IsUnbuilt { get; private set; }

    public bool CanPlay => IsBuilt && !string.IsNullOrWhiteSpace(ExePath);
    public bool CanRunAll => !string.IsNullOrWhiteSpace(ModuleName);
    public bool CanDownload => !IsDownloaded() && !string.IsNullOrWhiteSpace(RegistryUrl);

    public Bitmap? Image { get; private set; }

    public ObservableCollection<OpRow> Operations { get; } = new ObservableCollection<OpRow>();

    public System.Windows.Input.ICommand Button_Play_Click { get; }
    public System.Windows.Input.ICommand Button_RunAll_Click { get; }
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
        Button_RunOp_Click = new Cmd(async p => await RunOpAsync(p as OpRow));
        Button_Download_Click = new Cmd(async _ => await DownloadAsync());
        Button_OpenFolder_Click = new Cmd(async _ => await OpenFolderAsync());

        DataContext = this;
        InitializeComponent();

        Load();
    }

    public ModulePage(string moduleName) {
        ModuleName = this._moduleName = moduleName;

        Button_Play_Click = new Cmd(async _ => await PlayAsync());
        Button_RunAll_Click = new Cmd(async _ => await RunAllAsync());
        Button_RunOp_Click = new Cmd(async p => await RunOpAsync(p as OpRow));
        Button_Download_Click = new Cmd(async _ => await DownloadAsync());
        Button_OpenFolder_Click = new Cmd(async _ => await OpenFolderAsync());

        DataContext = this;
        InitializeComponent();

        Load();
    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Methods :: START :: */

    private void Load() {
        try {
            if (AvaloniaGui.Engine is null) {
                throw new InvalidOperationException(message: "Engine is not initialized.");
            }

            // Clear ops
            Operations.Clear();

            // Gather module info from multiple sources
            Dictionary<string, Core.Utils.GameModuleInfo> modules = AvaloniaGui.Engine.Modules(Core.Utils.ModuleFilter.All);
            Core.Utils.GameModuleInfo? m = modules.TryGetValue(_moduleName, out Core.Utils.GameModuleInfo? mm) ? mm : null;
            if (m is not null) {
                Title = string.IsNullOrWhiteSpace(m.Title) ? m.Name : m.Title!;
                ExePath = m.ExePath;
                GameRoot = m.GameRoot;
                IsBuilt = m.IsBuilt;
                IsInstalled = m.IsInstalled;
                IsRegistered = m.IsRegistered;
                IsUnverified = m.IsUnverified;
                IsUnbuilt = m.IsInstalled && !m.IsBuilt;
                Image = ResolveCoverBitmap(gameRoot: GameRoot);
            } else {
                Title = _moduleName;
                Image = ResolveCoverBitmap(gameRoot: null);
            }

            // Registry info (URL)
            IReadOnlyDictionary<string, object?> regs = AvaloniaGui.Engine.GameRegistry.GetRegisteredModules();
            if (regs.TryGetValue(_moduleName, out object? regObj) && regObj is IDictionary<string, object?> reg) {
                RegistryUrl = reg.TryGetValue(key: "url", value: out object? u) ? u?.ToString() : null;
                if (string.IsNullOrWhiteSpace(Title)) {
                    string? title = reg.TryGetValue(key: "title", value: out object? t) ? t?.ToString() : null;
                    Title = string.IsNullOrWhiteSpace(title) ? _moduleName : title!;
                }
            }

            // Load operations if ops_file exists
            string? opsFile = null;
            Dictionary<string, Core.Utils.GameModuleInfo> games = AvaloniaGui.Engine.Modules(Core.Utils.ModuleFilter.All);
            if (games.TryGetValue(_moduleName, out Core.Utils.GameModuleInfo? gameInfo)) {
                opsFile = gameInfo.OpsFile;
                if (string.IsNullOrWhiteSpace(ExePath)) {
                    ExePath = gameInfo.ExePath;
                }

                if (string.IsNullOrWhiteSpace(GameRoot)) {
                    GameRoot = gameInfo.GameRoot;
                }
            } else {
                Core.Diagnostics.Log($"Load: No game info found for module {_moduleName}.");
            }

            if (!string.IsNullOrWhiteSpace(opsFile) && System.IO.File.Exists(path: opsFile)) {
                Core.Services.OperationsService.PreparedOperations preparedOps = AvaloniaGui.Engine.OperationsService.LoadAndPrepare(opsFile);
                if (!preparedOps.IsLoaded) {
                    Core.Diagnostics.Log($"Load: Failed to load operations list for module {_moduleName} from ops file '{opsFile}'. {preparedOps.ErrorMessage}");
                    return;
                }

                if (preparedOps.Warnings.Count > 0) {
                    foreach (string warning in preparedOps.Warnings) {
                        Core.Diagnostics.Log($"[ModulePage::Load()] Warning: {warning}");
                        OperationOutputService.Instance.AddOutput($"Validation: {warning}", "stderr");
                    }
                }

                foreach (Core.Services.OperationsService.PreparedOperation op in preparedOps.RegularOperations) {
                    string scriptType = string.IsNullOrWhiteSpace(op.ScriptType) ? "python" : op.ScriptType;
                    string scriptPath = op.ScriptPath ?? string.Empty;
                    string displayName = op.DisplayName;
                    if (op.HasDuplicateId) {
                        displayName = $"[dup-id] {displayName}";
                    } else if (op.HasInvalidId) {
                        displayName = $"[invalid-id] {displayName}";
                    }
                    Operations.Add(item: new OpRow { Name = displayName, ScriptType = scriptType, ScriptPath = scriptPath, Op = op.Operation });
                }
            } else {
                Core.Diagnostics.Log($"Load: Ops file not found for module {_moduleName} at path '{opsFile}'.");
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
            Raise(nameof(CanDownload));
            Raise(nameof(Image));
            Raise(nameof(RegistryUrl));
        } catch (System.Exception ex) {
            OperationOutputService.Instance.AddOutput(text: $"Module load failed: {ex.Message}", stream: "stderr");
            Core.Diagnostics.Bug($"Load: {ex}");
        }
    }

    private Bitmap? ResolveCoverBitmap(string? gameRoot) {
        if (AvaloniaGui.Engine == null) {
            return null;
        }
        string? icon = string.IsNullOrWhiteSpace(gameRoot) ? null : System.IO.Path.Combine(path1: gameRoot!, path2: "icon.png");
        string placeholder = System.IO.Path.Combine(path1: Program.rootPath, path2: "placeholder.png");
        string pick = (!string.IsNullOrWhiteSpace(icon) && System.IO.File.Exists(path: icon!)) ? icon! : placeholder;
        try {
            return System.IO.File.Exists(path: pick) ? new Bitmap(pick) : null;
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"ResolveCoverBitmap: Failed to load bitmap from {pick}. {ex}");
            return null;
        }
    }

    private async System.Threading.Tasks.Task PlayAsync() {
        if (AvaloniaGui.Engine is null || string.IsNullOrWhiteSpace(ModuleName)) return;
        try {
            await GUI.Utils.ExecuteEngineOperationAsync(
                engine: AvaloniaGui.Engine,
                moduleName: ModuleName,
                operationName: "Play",
                executor: async (onOutput, onEvent, stdin) => {
                    // Temporarily redirect engine events/output for this launch
                    var previousSink = Core.UI.EngineSdk.LocalEventSink;
                    var previousMute = Core.UI.EngineSdk.MuteStdoutWhenLocalSink;
                    var previousIn = System.Console.In;

                    Core.UI.EngineSdk.LocalEventSink = e => onEvent(e);
                    Core.UI.EngineSdk.MuteStdoutWhenLocalSink = true;

                    try {
                        System.Console.SetIn(new GuiStdinRedirectReader(provider: stdin));
                        return await AvaloniaGui.Engine.GameLauncher.LaunchGameAsync(name: ModuleName);
                    } finally {
                        Core.UI.EngineSdk.LocalEventSink = previousSink;
                        Core.UI.EngineSdk.MuteStdoutWhenLocalSink = previousMute;
                        System.Console.SetIn(previousIn);
                    }
                }
            );
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"PlayAsync: {ex}");
            OperationOutputService.Instance.AddOutput(text: $"Launch failed: {ex.Message}", stream: "stderr");
        }
    }

    private async System.Threading.Tasks.Task RunAllAsync() {
        if (AvaloniaGui.Engine is null || string.IsNullOrWhiteSpace(ModuleName)) return;
        try {
            await GUI.Utils.ExecuteEngineOperationAsync(
                engine: AvaloniaGui.Engine,
                moduleName: ModuleName,
                operationName: "Run All",
                executor: (onOutput, onEvent, stdin) => AvaloniaGui.Engine.RunAllAsync(gameName: ModuleName, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdin)
            );
            Load();
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"RunAllAsync: {ex}");
            OperationOutputService.Instance.AddOutput(text: $"Run All failed: {ex.Message}", stream: "stderr");
        }
    }

    private async System.Threading.Tasks.Task RunOpAsync(OpRow? row) {
        if (row is null || AvaloniaGui.Engine is null) return;
        try {
            Dictionary<string, Core.Utils.GameModuleInfo> games = AvaloniaGui.Engine.Modules(Core.Utils.ModuleFilter.All);

            Dictionary<string, object?> answers = new Dictionary<string, object?>();
            await CollectAnswersForOperationAsync(op: row.Op, answers: answers);

            // Use embedded execution path (Engine handles engine/lua/js/bms in-process)
            await GUI.Utils.ExecuteEngineOperationAsync(
                engine: AvaloniaGui.Engine,
                moduleName: ModuleName,
                operationName: row.Name,
                executor: async (onOutput, onEvent, stdin) => {
                    Core.UI.EngineSdk.LocalEventSink = e => onEvent(e);
                    Core.UI.EngineSdk.MuteStdoutWhenLocalSink = true;

                    System.IO.TextReader previous = System.Console.In;
                    try {
                        System.Console.SetIn(new GuiStdinRedirectReader(provider: stdin));
                        bool ok = await AvaloniaGui.Engine.Engino.RunSingleOperationAsync(
                            currentGame: ModuleName,
                            games,
                            op: row.Op,
                            answers,
                            AvaloniaGui.Engine.EngineConfig,
                            AvaloniaGui.Engine.ToolResolver,
                            AvaloniaGui.Engine.GitService,
                            AvaloniaGui.Engine.GameRegistry,
                            AvaloniaGui.Engine.CommandService,
                            AvaloniaGui.Engine.OperationExecution, CancellationToken.None
                        );
                        return ok;
                    } finally {
                        try {
                            System.Console.SetIn(new System.IO.StreamReader(System.Console.OpenStandardInput()));
                        } catch (Exception ex) {
                            System.Console.SetIn(previous);                            Core.Diagnostics.Bug($"RunOpAsync: Failed to restore Console.In. {ex}");
                        }
                    }
                }
            );
            Load();
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"RunOpAsync: {ex}");
            OperationOutputService.Instance.AddOutput(text: $"Operation failed: {ex.Message}", stream: "stderr");
        }
    }

    private async System.Threading.Tasks.Task DownloadAsync() {
        if (AvaloniaGui.Engine is null) return;
        if (string.IsNullOrWhiteSpace(RegistryUrl)) return;
        try {
            await GUI.Utils.ExecuteEngineOperationAsync(
                engine: AvaloniaGui.Engine,
                moduleName: ModuleName,
                operationName: $"Download {ModuleName}",
                executor: async (onOutput, onEvent, stdin) => {
                    onEvent(new Dictionary<string, object?> { ["event"] = "start", ["name"] = ModuleName, ["url"] = RegistryUrl });
                    bool result = await System.Threading.Tasks.Task.Run(function: () => AvaloniaGui.Engine.DownloadModule(RegistryUrl!));
                    onOutput(result ? $"Download complete for {ModuleName}." : $"Download failed for {ModuleName}.", result ? "stdout" : "stderr");
                    onEvent(new Dictionary<string, object?> { ["event"] = "end", ["success"] = result, ["name"] = ModuleName });
                    return result;
                }
            );
            Load();
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"DownloadAsync: {ex}");
            OperationOutputService.Instance.AddOutput(text: $"Download failed: {ex.Message}", stream: "stderr");
        }
    }

    private async System.Threading.Tasks.Task OpenFolderAsync() {
        try {
            if (AvaloniaGui.Engine is null) return;
            string? path = AvaloniaGui.Engine.GetGamePath(name: ModuleName);
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path: path)) {
                return;
            }
            System.Diagnostics.ProcessStartInfo? psi = new System.Diagnostics.ProcessStartInfo { UseShellExecute = true };
            if (System.OperatingSystem.IsWindows()) {
                psi.FileName = "explorer";
                psi.Arguments = $"\"{path}\"";
            } else if (System.OperatingSystem.IsMacOS()) {
                psi.FileName = "open";
                psi.Arguments = $"\"{path}\"";
            } else {
                psi.FileName = "xdg-open";
                psi.Arguments = $"\"{path}\"";
            }
            System.Diagnostics.Process.Start(psi);
        } catch {
            Core.Diagnostics.Bug("OpenFolderAsync: Failed to open folder.");
        }
        await System.Threading.Tasks.Task.CompletedTask;
    }

    private bool IsDownloaded() {
        if (AvaloniaGui.Engine is null) return false;
        string path = System.IO.Path.Combine(path1: Program.rootPath, path2: System.IO.Path.Combine("EngineApps", "Games", _moduleName));
        return System.IO.Directory.Exists(path: path);
    }

    private async System.Threading.Tasks.Task CollectAnswersForOperationAsync(Dictionary<string, object?> op, Dictionary<string, object?> answers) {
        if (AvaloniaGui.Engine is null) {
            return;
        }

        Core.Services.OperationsService.PromptHandler handler = async request => {
            switch (request.Type) {
                case "confirm": {
                    bool defVal = request.DefaultValue is bool b && b;
                    bool res = await OperationOutputService.Instance.RequestConfirmPromptAsync(
                        title: request.Title,
                        message: request.Title,
                        defaultValue: defVal
                    );
                    return Core.Services.OperationsService.PromptResponse.FromValue(res);
                }
                case "select": {
                    string hint = request.Choices.Count > 0
                        ? $"Choices: {string.Join(", ", request.Choices.Select(choice => choice.Label))}"
                        : "No choices provided";
                    string? v = await OperationOutputService.Instance.RequestTextPromptAsync(
                        title: request.Title,
                        message: hint,
                        defaultValue: request.DefaultValue?.ToString(),
                        secret: request.IsSecret
                    );
                    if (string.IsNullOrWhiteSpace(v)) {
                        return request.DefaultValue is not null
                            ? Core.Services.OperationsService.PromptResponse.UseDefaultValue()
                            : Core.Services.OperationsService.PromptResponse.FromValue(string.Empty);
                    }
                    return Core.Services.OperationsService.PromptResponse.FromValue(v);
                }
                case "checkbox": {
                    string hint = request.Choices.Count > 0
                        ? $"Enter comma-separated values (Choices: {string.Join(", ", request.Choices.Select(choice => choice.Label))})"
                        : "Enter comma-separated values";
                    string? v = await OperationOutputService.Instance.RequestTextPromptAsync(
                        title: request.Title,
                        message: hint,
                        defaultValue: null,
                        secret: request.IsSecret
                    );

                    if (string.IsNullOrWhiteSpace(v)) {
                        if (request.DefaultValue is IList<object?>) {
                            return Core.Services.OperationsService.PromptResponse.UseDefaultValue();
                        }
                        return Core.Services.OperationsService.PromptResponse.FromValue(new List<object?>());
                    }

                    List<object?> list = new List<object?>();
                    string[] parts = v.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
                    foreach (string s in parts) {
                        list.Add(s);
                    }
                    return Core.Services.OperationsService.PromptResponse.FromValue(list);
                }
                case "text":
                default: {
                    string? def = request.DefaultValue?.ToString();
                    string? v = await OperationOutputService.Instance.RequestTextPromptAsync(
                        title: request.Title,
                        message: request.Title,
                        defaultValue: def,
                        secret: request.IsSecret
                    );
                    if (string.IsNullOrWhiteSpace(v)) {
                        return request.DefaultValue is not null
                            ? Core.Services.OperationsService.PromptResponse.UseDefaultValue()
                            : Core.Services.OperationsService.PromptResponse.FromValue(string.Empty);
                    }
                    return Core.Services.OperationsService.PromptResponse.FromValue(v);
                }
            }
        };

        await AvaloniaGui.Engine.OperationsService.CollectAnswersAsync(op, answers, handler);
    }

    /* :: :: Methods :: END :: */
    // //
    /* :: :: Nested Types :: START :: */
    public sealed class OpRow {
        public string Name { get; set; } = string.Empty;
        public string ScriptType { get; set; } = string.Empty;
        public string ScriptPath { get; set; } = string.Empty;
        public Dictionary<string, object?> Op { get; set; } = new Dictionary<string, object?>();
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
        public event System.EventHandler? CanExecuteChanged;
    }
    /* :: :: Nested Types :: END :: */
}
