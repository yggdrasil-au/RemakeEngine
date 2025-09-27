using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RemakeEngine.Interface.GUI.Avalonia.ViewModels;
using Xunit;

namespace EngineNet.Tests;

public sealed class MainViewModelTests
{
    [AvaloniaFact]
    public async Task InitializesPlaceholdersWhenNoGamesOrModules()
    {
        using var cwd = new TemporaryWorkingDirectory();
        var engine = new FakeAvaloniaEngine();

        var vm = new MainViewModel(engine);

        await FlushUiAsync();

        Assert.Single(vm.Library);
        Assert.True(vm.Library[0].IsPlaceholder);
        Assert.Equal("No games found.", vm.Library[0].Name);

        Assert.Single(vm.Store);
        Assert.True(vm.Store[0].IsPlaceholder);
        Assert.Equal("No registry entries found.", vm.Store[0].Name);

        Assert.Empty(vm.Installing);
    }

    [AvaloniaFact]
    public async Task InstallCommandAddsRowAndUpdatesProgress()
    {
        using var cwd = new TemporaryWorkingDirectory();
        var engine = new FakeAvaloniaEngine
        {
            InstallEvents =
            {
                new Dictionary<string, object?>
                {
                    ["event"] = "progress",
                    ["label"] = "Downloading",
                    ["current"] = 3,
                    ["total"] = 6
                }
            },
            EmitEndEvent = false
        };

        var vm = new MainViewModel(engine);

        var rowAdded = new TaskCompletionSource<InstallRow>(TaskCreationOptions.RunContinuationsAsynchronously);
        NotifyCollectionChangedEventHandler? handler = null;
        handler = (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems?.Count > 0 && e.NewItems[0] is InstallRow added)
            {
                rowAdded.TrySetResult(added);
                vm.Installing.CollectionChanged -= handler!;
            }
        };
        vm.Installing.CollectionChanged += handler;

        vm.InstallModuleCommand.Execute(new StoreItem { Name = "Example" });

        await WaitWithPumpAsync(engine.WaitForInstallAsync(), TimeSpan.FromMilliseconds(200));
        var row = await WaitWithPumpAsync(rowAdded.Task, TimeSpan.FromMilliseconds(200));
        Assert.Equal("Example", row.Name);

        if (!IsDesiredProgress(row))
        {
            var progressUpdated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            PropertyChangedEventHandler? rowHandler = null;
            rowHandler = (_, args) =>
            {
                if ((args.PropertyName == nameof(InstallRow.Label) || args.PropertyName == nameof(InstallRow.Progress)) && IsDesiredProgress(row))
                {
                    progressUpdated.TrySetResult(true);
                    row.PropertyChanged -= rowHandler!;
                }
            };
            row.PropertyChanged += rowHandler;
            await WaitWithPumpAsync(progressUpdated.Task, TimeSpan.FromMilliseconds(200));
        }

        Assert.Equal("Downloading (3/6)", row.Label);
        Assert.InRange(row.Progress, 0.49, 0.51);
        Assert.Equal(2, vm.CurrentTabIndex);
    }

    [AvaloniaFact]
    public async Task PromptEventShowsAndSubmitProvidesAnswer()
    {
        using var cwd = new TemporaryWorkingDirectory();
        var engine = new FakeAvaloniaEngine
        {
            RequestPromptDuringInstall = true,
            PromptMessage = "Provide value"
        };

        var vm = new MainViewModel(engine);

        var promptShown = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler? promptHandler = null;
        promptHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.PromptIsVisible) && vm.PromptIsVisible)
            {
                promptShown.TrySetResult(true);
                vm.PropertyChanged -= promptHandler!;
            }
        };
        vm.PropertyChanged += promptHandler;
        if (vm.PromptIsVisible)
        {
            promptShown.TrySetResult(true);
        }

        vm.InstallModuleCommand.Execute(new StoreItem { Name = "NeedsInput" });

        await WaitWithPumpAsync(engine.WaitForInstallAsync(), TimeSpan.FromMilliseconds(200));
        await WaitWithPumpAsync(promptShown.Task, TimeSpan.FromMilliseconds(200));
        Assert.Equal("Provide value", vm.PromptQuestion);

        var promptHidden = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler? hiddenHandler = null;
        hiddenHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.PromptIsVisible) && !vm.PromptIsVisible)
            {
                promptHidden.TrySetResult(true);
                vm.PropertyChanged -= hiddenHandler!;
            }
        };
        vm.PropertyChanged += hiddenHandler;
        if (!vm.PromptIsVisible)
        {
            promptHidden.TrySetResult(true);
        }

        vm.PromptAnswer = "abc";
        vm.SubmitPromptCommand.Execute(null);

        await WaitWithPumpAsync(promptHidden.Task, TimeSpan.FromMilliseconds(200));
        await FlushUiAsync();

        Assert.False(vm.PromptIsVisible);
        Assert.Equal(new[] { "abc" }, engine.CapturedPromptAnswers);
    }

    private static bool IsDesiredProgress(InstallRow row)
        => row.Label == "Downloading (3/6)" && Math.Abs(row.Progress - 0.5) < 0.01;

    private static async Task FlushUiAsync()
    {
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        await Task.Yield();
    }

    private static async Task<T> WaitWithPumpAsync<T>(Task<T> task, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (sw.Elapsed >= timeout)
            {
                throw new TimeoutException("The operation has timed out.");
            }
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            await Task.Yield();
        }
        return await task;
    }

    private static async Task WaitWithPumpAsync(Task task, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (sw.Elapsed >= timeout)
            {
                throw new TimeoutException("The operation has timed out.");
            }
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            await Task.Yield();
        }
        await task;
    }

    private sealed class TemporaryWorkingDirectory : IDisposable
    {
        private readonly string _previous;

        public TemporaryWorkingDirectory()
        {
            _previous = Directory.GetCurrentDirectory();
            var temp = Path.Combine(Path.GetTempPath(), "RemakeEngineTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            Directory.SetCurrentDirectory(temp);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_previous);
        }
    }
}

public sealed class FakeAvaloniaEngine
{
    public Dictionary<string, Dictionary<string, object?>> InstalledGames { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ModuleStates { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string?> CapturedPromptAnswers { get; } = new();
    public List<Dictionary<string, object?>> InstallEvents { get; } = new();
    public bool EmitEndEvent { get; set; } = true;
    public bool RequestPromptDuringInstall { get; set; }
    public string PromptMessage { get; set; } = "Input required";
    public bool InstallResult { get; set; } = true;

    private readonly TaskCompletionSource<string> _installStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _promptSatisfied = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitForInstallAsync() => _installStarted.Task;
    public Task WaitForPromptResponseAsync() => _promptSatisfied.Task;

    public IDictionary<string, object?> GetInstalledGames() => InstalledGames.ToDictionary(
        kvp => kvp.Key,
        kvp => (object?)new Dictionary<string, object?>(kvp.Value),
        StringComparer.OrdinalIgnoreCase);

    public IDictionary<string, object?> ListGames() => GetInstalledGames();
    public bool LaunchGame(string name) => false;

    public string? GetGamePath(string name) => null;

    public bool DownloadModule(string url) => true;

    public string GetModuleState(string name)
    {
        return ModuleStates.TryGetValue(name, out var state) ? state : "not_downloaded";
    }

    public async Task<bool> InstallModuleAsync(string moduleName, Delegate? outputHandler, Delegate? eventHandler, Delegate? stdinProvider)
    {
        _installStarted.TrySetResult(moduleName);
        await Task.Yield();

        foreach (var evt in InstallEvents)
        {
            if (eventHandler is not null)
            {
                var payload = Clone(evt);
                Dispatcher.UIThread.Post(() => eventHandler.DynamicInvoke(payload), DispatcherPriority.Background);
            }
            await Task.Yield();
        }

        if (RequestPromptDuringInstall && eventHandler is not null)
        {
            var payload = new Dictionary<string, object?>
            {
                ["event"] = "prompt",
                ["message"] = PromptMessage
            };
            Dispatcher.UIThread.Post(() => eventHandler.DynamicInvoke(payload), DispatcherPriority.Background);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            if (stdinProvider is not null)
            {
                var answer = await Task.Run(() => (string?)stdinProvider.DynamicInvoke());
                CapturedPromptAnswers.Add(answer);
                _promptSatisfied.TrySetResult(true);
            }
            else
            {
                _promptSatisfied.TrySetResult(true);
            }
        }
        else
        {
            _promptSatisfied.TrySetResult(true);
        }

        if (EmitEndEvent && eventHandler is not null)
        {
            eventHandler.DynamicInvoke(new Dictionary<string, object?> { ["event"] = "end" });
        }

        return InstallResult;
    }

    private static Dictionary<string, object?> Clone(Dictionary<string, object?> source)
    {
        var clone = new Dictionary<string, object?>(source.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in source)
        {
            clone[kvp.Key] = kvp.Value;
        }
        return clone;
    }
}




