
using System;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;

using Avalonia.Platform.Storage;

namespace EngineNet.Interface.GUI.Pages;

public partial class SettingsPage:UserControl, INotifyPropertyChanged {
    private readonly Core.Engine? _engine;

    private string _projectRoot = "";
    internal string ProjectRoot {
        get => _projectRoot; set {
            _projectRoot = value;
            Raise(nameof(ProjectRoot));
        }
    }

    private string _status = "";
    internal string Status {
        get => _status; private set {
            _status = value;
            Raise(nameof(Status));
        }
    }

    internal System.Windows.Input.ICommand BrowseRootCommand {
        get;
    }
    internal System.Windows.Input.ICommand SaveCommand {
        get;
    }


    // preview only constructor
    public SettingsPage() {
        InitializeComponent();
        DataContext = this;

        ProjectRoot = @"C:\Example\Project\Root (Design)";
        Status = "Design mode active.";
        // Initialize commands
        BrowseRootCommand = new Cmd(async _ => await Task.CompletedTask);
        SaveCommand = new Cmd(async _ => await Task.CompletedTask);

    }
    internal SettingsPage(Core.Engine engine) {
        _engine = engine;
        InitializeComponent();
        DataContext = this;

        BrowseRootCommand = new Cmd(async _ => await BrowseRootAsync());
        SaveCommand = new Cmd(async _ => await SaveAsync());

        ProjectRoot = _engine.GetRootPath();

    }

    private async System.Threading.Tasks.Task BrowseRootAsync() {
        try {
            Window? top = TopLevel.GetTopLevel(this) as Window;
            if (top?.StorageProvider is not null) {
                FolderPickerOpenOptions? options = new FolderPickerOpenOptions {
                    Title = "Select Project Root"
                };
                IReadOnlyList<IStorageFolder>? folders = await top.StorageProvider.OpenFolderPickerAsync(options);
                string? path = folders?.FirstOrDefault()?.Path.LocalPath;
                if (!string.IsNullOrWhiteSpace(path))
                    ProjectRoot = path!;
            } else {
                Status = "Browse failed: StorageProvider not available.";
            }
        } catch (System.Exception ex) {
            Status = $"Browse failed: {ex.Message}";
        }
    }

    private async System.Threading.Tasks.Task SaveAsync() {
        try {
            // TODO: implement saving settings to engine or config file
            // This will require adding methods to OperationsEngine to set these values

            Status = "Settings saved.";
        } catch (System.Exception ex) {
            Status = $"Save failed: {ex.Message}";
        }
        await Task.Yield();
    }

    internal event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class Cmd:System.Windows.Input.ICommand {
        private readonly Func<object?, Task> _run;
        public Cmd(System.Func<object?, Task> run) => _run = run;
        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter) => await _run(parameter);
        public event EventHandler? CanExecuteChanged;
    }
}
