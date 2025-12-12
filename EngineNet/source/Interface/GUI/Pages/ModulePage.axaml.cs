using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;

using Avalonia.Controls;
using Avalonia.Media.Imaging;
using System.Diagnostics;

namespace EngineNet.Interface.GUI.Pages;

internal sealed partial class ModulePage:UserControl {

    /* :: :: Vars :: START :: */
    private readonly Core.Engine? _engine;
    private readonly string _moduleName = string.Empty;

    internal string ModuleName { get; private set; } = string.Empty;
    internal string Title { get; private set; } = string.Empty;
    internal string? GameRoot { get; private set; }
    internal string? ExePath { get; private set; }
    internal string? RegistryUrl { get; private set; }

    internal bool IsBuilt { get; private set; }
    internal bool IsInstalled { get; private set; }
    internal bool IsRegistered { get; private set; }
    internal bool IsUnverified { get; private set; }
    internal bool IsUnbuilt { get; private set; }

    internal bool CanPlay => IsBuilt && !string.IsNullOrWhiteSpace(ExePath);
    internal bool CanRunAll => !string.IsNullOrWhiteSpace(ModuleName);
    internal bool CanDownload => !IsDownloaded() && !string.IsNullOrWhiteSpace(RegistryUrl);

    internal Bitmap? Image { get; private set; }

    internal ObservableCollection<OpRow> Operations { get; } = new ObservableCollection<OpRow>();

    internal System.Windows.Input.ICommand Button_Play_Click { get; }
    internal System.Windows.Input.ICommand Button_RunAll_Click { get; }
    internal System.Windows.Input.ICommand Button_RunOp_Click { get; }
    internal System.Windows.Input.ICommand Button_Download_Click { get; }
    internal System.Windows.Input.ICommand Button_OpenFolder_Click { get; }
    /* :: :: Vars :: END :: */
    // //
    /* :: :: Constructors :: START :: */

    // Designer only
    public ModulePage() {
        DataContext = this;
        InitializeComponent();
        Button_Play_Click = new Cmd(_ => System.Threading.Tasks.Task.CompletedTask);
        Button_RunAll_Click = new Cmd(_ => System.Threading.Tasks.Task.CompletedTask);
        Button_RunOp_Click = new Cmd(_ => System.Threading.Tasks.Task.CompletedTask);
        Button_Download_Click = new Cmd(_ => System.Threading.Tasks.Task.CompletedTask);
        Button_OpenFolder_Click = new Cmd(_ => System.Threading.Tasks.Task.CompletedTask);
    }

    internal ModulePage(Core.Engine engine, string moduleName) {
        _engine = engine;
        _moduleName = moduleName;
        ModuleName = moduleName;

        DataContext = this;
        InitializeComponent();

        Button_Play_Click = new Cmd(async _ => await PlayAsync());
        Button_RunAll_Click = new Cmd(async _ => await RunAllAsync());
        Button_RunOp_Click = new Cmd(async p => await RunOpAsync(p as OpRow));
        Button_Download_Click = new Cmd(async _ => await DownloadAsync());
        Button_OpenFolder_Click = new Cmd(async _ => await OpenFolderAsync());

        Load();
    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Methods :: START :: */

    private void Load() {
        try {
            if (_engine is null) {
                throw new InvalidOperationException(message: "Engine is not initialized.");
            }

            // Clear ops
            Operations.Clear();

            // Gather module info from multiple sources
            Dictionary<string, Core.Utils.GameModuleInfo> modules = _engine.Modules(Core.Utils.ModuleFilter.All);
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
            IReadOnlyDictionary<string, object?> regs = _engine.GetRegistries.GetRegisteredModules();
            if (regs.TryGetValue(_moduleName, out object? regObj) && regObj is IDictionary<string, object?> reg) {
                RegistryUrl = reg.TryGetValue(key: "url", value: out object? u) ? u?.ToString() : null;
                if (string.IsNullOrWhiteSpace(Title)) {
                    string? title = reg.TryGetValue(key: "title", value: out object? t) ? t?.ToString() : null;
                    Title = string.IsNullOrWhiteSpace(title) ? _moduleName : title!;
                }
            }

            // Load operations if ops_file exists
            string? opsFile = null;
            Dictionary<string, Core.Utils.GameModuleInfo> games = _engine.Modules(Core.Utils.ModuleFilter.All);
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
                List<Dictionary<string, object?>>? allOps = Core.Engine.LoadOperationsList(opsFile);
                if (allOps is null) {
                    Core.Diagnostics.Log($"Load: Failed to load operations list for module {_moduleName} from ops file '{opsFile}'.");
                    return;
                }
                foreach (Dictionary<string, object?> op in allOps) {
                    string name = op.TryGetValue(key: "name", value: out object? n) ? n?.ToString() ?? "" : "";
                    string scriptType = (op.TryGetValue(key: "script_type", value: out object? st) ? st?.ToString() : null) ?? "python";
                    string scriptPath = op.TryGetValue(key: "script", value: out object? sp) ? sp?.ToString() ?? "" : "";
                    Operations.Add(item: new OpRow { Name = string.IsNullOrWhiteSpace(name) ? scriptPath : name, ScriptType = scriptType, ScriptPath = scriptPath, Op = op });
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
                        Core.Diagnostics.Log($"Load: {ex}");
        }
    }

    private Bitmap? ResolveCoverBitmap(string? gameRoot) {
        if (_engine == null) {
            return null;
        }
        string? icon = string.IsNullOrWhiteSpace(gameRoot) ? null : System.IO.Path.Combine(path1: gameRoot!, path2: "icon.png");
        string placeholder = System.IO.Path.Combine(path1: _engine.rootPath, path2: "placeholder.png");
        string pick = (!string.IsNullOrWhiteSpace(icon) && System.IO.File.Exists(path: icon!)) ? icon! : placeholder;
        try {
            return System.IO.File.Exists(path: pick) ? new Bitmap(pick) : null;
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"ResolveCoverBitmap: Failed to load bitmap from {pick}. {ex}");
            return null;
        }
    }

    private async System.Threading.Tasks.Task PlayAsync() {
        try {
            if (_engine is null) return;
            if (string.IsNullOrWhiteSpace(ModuleName)) return;
            _engine.LaunchGame(name: ModuleName);
        } catch (System.Exception ex) {
            OperationOutputService.Instance.AddOutput(text: $"Launch failed: {ex.Message}", stream: "stderr");
            Core.Diagnostics.Log($"PlayAsync: {ex}");
        }
        await System.Threading.Tasks.Task.CompletedTask;
    }

    private async System.Threading.Tasks.Task RunAllAsync() {
        if (_engine is null || string.IsNullOrWhiteSpace(ModuleName)) return;
        try {
            await GUI.Utils.ExecuteEngineOperationAsync(
                engine: _engine,
                moduleName: ModuleName,
                operationName: "Run All",
                executor: (onOutput, onEvent, stdin) => _engine.RunAllAsync(gameName: ModuleName, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdin)
            );
            Load();
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"RunAllAsync: {ex}");
            OperationOutputService.Instance.AddOutput(text: $"Run All failed: {ex.Message}", stream: "stderr");
        }
    }

    private async System.Threading.Tasks.Task RunOpAsync(OpRow? row) {
        if (row is null || _engine is null) return;
        try {
            Dictionary<string, Core.Utils.GameModuleInfo> games = _engine.Modules(Core.Utils.ModuleFilter.All);

            Dictionary<string, object?> answers = new Dictionary<string, object?>();
            await CollectAnswersForOperationAsync(op: row.Op, answers: answers);

            // Use embedded execution path (Engine handles engine/lua/js/bms in-process)
            await GUI.Utils.ExecuteEngineOperationAsync(
                engine: _engine,
                moduleName: ModuleName,
                operationName: row.Name,
                executor: async (onOutput, onEvent, stdin) => {
                    Core.Utils.EngineSdk.LocalEventSink = e => onEvent(e);
                    Core.Utils.EngineSdk.MuteStdoutWhenLocalSink = true;

                    System.IO.TextReader previous = System.Console.In;
                    try {
                        System.Console.SetIn(new GuiStdinRedirectReader(provider: stdin));
                        bool ok = await _engine.RunSingleOperationAsync(currentGame: ModuleName, games: games, op: row.Op, promptAnswers: answers);
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
        if (_engine is null) return;
        if (string.IsNullOrWhiteSpace(RegistryUrl)) return;
        try {
            await GUI.Utils.ExecuteEngineOperationAsync(
                engine: _engine,
                moduleName: ModuleName,
                operationName: $"Download {ModuleName}",
                executor: async (onOutput, onEvent, stdin) => {
                    onEvent(new Dictionary<string, object?> { ["event"] = "start", ["name"] = ModuleName, ["url"] = RegistryUrl });
                    bool result = await System.Threading.Tasks.Task.Run(function: () => _engine.DownloadModule(RegistryUrl!));
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
            if (_engine is null) return;
            string? path = _engine.GetGamePath(name: ModuleName);
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
        if (_engine is null) return false;
        string path = System.IO.Path.Combine(path1: _engine.rootPath, path2: System.IO.Path.Combine("EngineApps", "Games", _moduleName));
        return System.IO.Directory.Exists(path: path);
    }

    private async System.Threading.Tasks.Task CollectAnswersForOperationAsync(Dictionary<string, object?> op, Dictionary<string, object?> answers) {
        if (!op.TryGetValue(key: "prompts", value: out object? promptsObj) || promptsObj is not IList<object?> prompts) {
            return;
        }

        foreach (object? p in prompts) {
            if (p is not Dictionary<string, object?> prompt) continue;

            string? name = prompt.TryGetValue(key: "Name", value: out object? n) ? n?.ToString() : null;
            string? type = prompt.TryGetValue(key: "type", value: out object? tt) ? tt?.ToString() : null;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) continue;

            // honor condition if present
            if (prompt.TryGetValue(key: "condition", value: out object? cond) && cond is string condName) {
                if (!answers.TryGetValue(key: condName, value: out object? condVal) || condVal is not bool cb || !cb) {
                    answers[name] = type == "checkbox" ? new List<object?>() : null;
                    continue;
                }
            }

            switch (type) {
                case "confirm": {
                    bool def = prompt.TryGetValue(key: "default", value: out object? dv) && dv is bool db && db;
                    bool res = await OperationOutputService.Instance.RequestConfirmPromptAsync(title: name, message: name, defaultValue: def);
                    answers[name] = res;
                    break;
                }
                case "checkbox": {
                    // Fallback to comma-separated input prompt
                    string? hint = prompt.TryGetValue(key: "hint", value: out object? h) ? h?.ToString() : null;
                    string msg = string.IsNullOrWhiteSpace(hint) ? "Enter comma-separated values" : hint!;
                    string? v = await OperationOutputService.Instance.RequestTextPromptAsync(title: name, message: msg, defaultValue: null, secret: false);
                    List<object?> list = new List<object?>();
                    if (!string.IsNullOrWhiteSpace(v)) {
                        string[] parts = v.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
                        foreach (string s in parts) list.Add(s);
                    }
                    answers[name] = list;
                    break;
                }
                case "text":
                default: {
                    string label = prompt.TryGetValue(key: "message", value: out object? msgObj) ? msgObj?.ToString() ?? name : name;
                    string? def = prompt.TryGetValue(key: "default", value: out object? d) ? d?.ToString() : null;
                    string? v = await OperationOutputService.Instance.RequestTextPromptAsync(title: name, message: label, defaultValue: def, secret: false);
                    answers[name] = string.IsNullOrEmpty(v) ? def : v;
                    break;
                }
            }
        }
    }

    /* :: :: Methods :: END :: */
    // //
    /* :: :: Nested Types :: START :: */
    internal sealed class OpRow {
        internal string Name { get; set; } = string.Empty;
        internal string ScriptType { get; set; } = string.Empty;
        internal string ScriptPath { get; set; } = string.Empty;
        internal Dictionary<string, object?> Op { get; set; } = new Dictionary<string, object?>();
    }

    private sealed class GuiStdinRedirectReader:System.IO.TextReader {
        private readonly Core.ProcessRunner.StdinProvider _provider;
        internal GuiStdinRedirectReader(Core.ProcessRunner.StdinProvider provider) { _provider = provider; }
        public override string? ReadLine() { return _provider(); }
    }

    private event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName: name));

    private sealed class Cmd:System.Windows.Input.ICommand {
        private readonly System.Func<object?, System.Threading.Tasks.Task> _run;
        internal Cmd(System.Func<object?, System.Threading.Tasks.Task> run) { _run = run; }
        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter) => await _run(parameter);
        public event System.EventHandler? CanExecuteChanged;
    }
    /* :: :: Nested Types :: END :: */
}
