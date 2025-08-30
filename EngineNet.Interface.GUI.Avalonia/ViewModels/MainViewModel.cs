using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace RemakeEngine.Interface.GUI.Avalonia.ViewModels;

public sealed class MainViewModel
{
    private readonly dynamic _engine; // OperationsEngine (dynamic to avoid hard ref)

    public ObservableCollection<GameItem> Library { get; } = new();
    public ObservableCollection<StoreItem> Store { get; } = new();

    public ICommand RefreshLibraryCommand { get; }
    public ICommand RefreshStoreCommand { get; }
    public ICommand RunGameCommand { get; }
    public ICommand InstallModuleCommand { get; }

    public MainViewModel(object? engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));

        RefreshLibraryCommand = new RelayCommand(_ => RefreshLibrary());
        RefreshStoreCommand = new RelayCommand(_ => RefreshStore());
        RunGameCommand = new AsyncRelayCommand(async p =>
        {
            if (p is GameItem gi)
                await RunGameGroupAsync(gi);
        });
        InstallModuleCommand = new AsyncRelayCommand(async p =>
        {
            if (p is StoreItem si)
                await InstallModuleAsync(si);
        });

        // initial load
        RefreshLibrary();
        RefreshStore();
    }

    private void RefreshLibrary()
    {
        Library.Clear();
        try
        {
            IDictionary<string, object?> games = _engine.ListGames();
            foreach (var kv in games)
            {
                Library.Add(new GameItem { Name = kv.Key, Info = kv.Value as IDictionary<string, object?> });
            }
            if (Library.Count == 0)
            {
                Library.Add(new GameItem { Name = "No games found.", Info = null, IsPlaceholder = true });
            }
        }
        catch
        {
            Library.Add(new GameItem { Name = "Error loading games.", Info = null, IsPlaceholder = true });
        }
    }

    private void RefreshStore()
    {
        Store.Clear();
        try
        {
            // Use reflection to avoid direct Core reference
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => string.Equals(a.GetName().Name, "EngineNet", StringComparison.OrdinalIgnoreCase));
            var t = asm?.GetType("RemakeEngine.Core.Registries");
            if (t is not null)
            {
                dynamic regs = Activator.CreateInstance(t, System.IO.Directory.GetCurrentDirectory())!;
                IDictionary<string, object?> reg = regs.GetRegisteredModules();
                foreach (var kv in reg)
                    Store.Add(new StoreItem { Name = kv.Key, Meta = kv.Value });
            }
            if (Store.Count == 0)
                Store.Add(new StoreItem { Name = "No registry entries found.", Meta = null, IsPlaceholder = true });
        }
        catch
        {
            Store.Add(new StoreItem { Name = "Error loading registry.", Meta = null, IsPlaceholder = true });
        }
    }

    private async Task RunGameGroupAsync(GameItem item)
    {
        if (item.Info is null || !item.Info.TryGetValue("ops_file", out var of) || of is not string opsFile)
            return;

        IDictionary<string, List<Dictionary<string, object?>>> doc = _engine.LoadOperations(opsFile);
        if (doc.Count == 0)
            return;

        var group = await PromptHelpers.PickAsync("Select operation group", doc.Keys.ToList());
        if (group is null)
            return;

        var games = _engine.ListGames();
        if (!doc.TryGetValue(group, out var ops))
            return;
        var answers = await CollectAnswersForGroupAsync(ops);

        var ok = await _engine.RunOperationGroupAsync(item.Name, games, group, ops, answers);
        await PromptHelpers.InfoAsync(ok ? "Completed successfully." : "One or more operations failed.", "Run Group");
    }

    private async Task<IDictionary<string, object?>> CollectAnswersForGroupAsync(IList<Dictionary<string, object?>> ops)
    {
        var answers = new Dictionary<string, object?>();
        foreach (var op in ops)
        {
            if (!op.TryGetValue("prompts", out var ps) || ps is not IList<object?> prompts)
                continue;
            foreach (var p in prompts)
            {
                if (p is not Dictionary<string, object?> prompt)
                    continue;
                var name = prompt.TryGetValue("Name", out var n) ? n?.ToString() ?? string.Empty : string.Empty;
                var type = prompt.TryGetValue("type", out var t) ? t?.ToString() ?? string.Empty : string.Empty;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type))
                    continue;
                switch (type)
                {
                    case "confirm":
                        answers[name] = await PromptHelpers.ConfirmAsync(name, "Confirm");
                        break;
                    case "checkbox":
                        // Basic text entry of comma-separated values
                        var txt = await PromptHelpers.TextAsync(name);
                        answers[name] = (txt ?? string.Empty)
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Cast<object?>().ToList();
                        break;
                    case "text":
                    default:
                        answers[name] = await PromptHelpers.TextAsync(name);
                        break;
                }
            }
        }
        return answers;
    }

    private async Task InstallModuleAsync(StoreItem item)
    {
        if (item.Meta is not Dictionary<string, object?> meta)
            return;
        if (!meta.TryGetValue("url", out var u) || u is not string url || string.IsNullOrWhiteSpace(url))
        {
            await PromptHelpers.InfoAsync("No Git URL in registry for this module.", "Install");
            return;
        }

        var ok = await Task.Run(() => (bool)_engine.DownloadModule(url));
        await PromptHelpers.InfoAsync(ok ? $"Installed '{item.Name}'." : $"Failed to install '{item.Name}'.", "Install");
        RefreshLibrary();
        RefreshStore();
    }
}

public sealed class GameItem
{
    public string Name { get; set; } = string.Empty;
    public IDictionary<string, object?>? Info { get; set; }
    public bool IsPlaceholder { get; set; }
}

public sealed class StoreItem
{
    public string Name { get; set; } = string.Empty;
    public object? Meta { get; set; }
    public bool IsPlaceholder { get; set; }
}

internal sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _action;
    private readonly Func<object?, bool>? _canExecute;
    public RelayCommand(Action<object?> action, Func<object?, bool>? canExecute = null)
    { _action = action; _canExecute = canExecute; }
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _action(parameter);
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _action;
    private readonly Func<object?, bool>? _canExecute;
    private bool _busy;
    public AsyncRelayCommand(Func<object?, Task> action, Func<object?, bool>? canExecute = null)
    { _action = action; _canExecute = canExecute; }
    public bool CanExecute(object? parameter) => !_busy && (_canExecute?.Invoke(parameter) ?? true);
    public async void Execute(object? parameter)
    {
        _busy = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await _action(parameter); }
        finally { _busy = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler? CanExecuteChanged;
}
