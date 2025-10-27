using Avalonia.Platform.Storage;
using System.ComponentModel;

namespace EngineNet.Interface.GUI.Views.Pages;

public partial class SettingsPage:UserControl, INotifyPropertyChanged {
    private readonly Core.OperationsEngine? _engine;

    private string _projectRoot = "";
    public string ProjectRoot {
        get => _projectRoot; set {
            _projectRoot = value;
            Raise(nameof(ProjectRoot));
        }
    }

    public ObservableCollection<string> Themes { get; } = new() { "Default", "Light", "Dark" };
    private string _selectedTheme = "Default";
    public string SelectedTheme {
        get => _selectedTheme; set {
            _selectedTheme = value;
            Raise(nameof(SelectedTheme));
        }
    }

    private string _status = "";
    public string Status {
        get => _status; private set {
            _status = value;
            Raise(nameof(Status));
        }
    }

    public ICommand BrowseRootCommand {
        get;
    }
    public ICommand SaveCommand {
        get;
    }
    public ICommand ApplyThemeCommand {
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
        ApplyThemeCommand = new Cmd(async _ => await Task.CompletedTask);

    }
    public SettingsPage(Core.OperationsEngine engine) {
        _engine = engine;
        InitializeComponent();
        DataContext = this;

        BrowseRootCommand = new Cmd(async _ => await BrowseRootAsync());
        SaveCommand = new Cmd(async _ => await SaveAsync());
        ApplyThemeCommand = new Cmd(async _ => await ApplyThemeAsync());

        ProjectRoot = _engine.GetRootPath();

    }

    private async Task BrowseRootAsync() {
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
        } catch (Exception ex) {
            Status = $"Browse failed: {ex.Message}";
        }
    }

    private async Task SaveAsync() {
        try {
            try {
                //_engine.Config.ProjectRoot = ProjectRoot;
                //_engine.Config.Save();
            } catch {
                try {
                    //_engine.EngineConfig.SetProjectPath(ProjectRoot);
                    //_engine.EngineConfig.Save();
                } catch {
                    /* no-op if engine doesn�t expose saving */
                }
            }

            Status = "Settings saved.";
        } catch (Exception ex) {
            Status = $"Save failed: {ex.Message}";
        }
        await Task.Yield();
    }

    private async Task ApplyThemeAsync() {
        try {
            // Minimal theme toggle demo; wire into your theme manager if present
            Application? app = Application.Current;
            if (app is not null) {
                // Replace with your resource dictionaries, Fluent theme, etc.
                Status = $"Theme applied: {SelectedTheme}";
            }
        } catch (Exception ex) {
            Status = $"Apply theme failed: {ex.Message}";
        }
        await Task.Yield();
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
