
using System;

namespace EngineNet.Interface.GUI.Views.Pages;

public partial class LibraryPage:UserControl {

    /* :: :: Vars :: START :: */
    // //
    private readonly Core.OperationsEngine? _engine;

    private ObservableCollection<Row> Items {
        get;
    } = new ObservableCollection<Row>();

    // //

    private ICommand RefreshCommand {
        get;
    }
    private ICommand RunCommand {
        get;
    }
    private ICommand OpenFolderCommand {
        get;
    }

    /* :: :: Vars :: END :: */
    // // 
    /* :: :: Constructors :: START :: */

    // used only for previewer
    public LibraryPage() {
        InitializeComponent();
        DataContext = this; // Set DataContext for design-time bindings

        // Initialize commands to prevent binding errors in the designer
        RefreshCommand = new SimpleCommand(_ => { });
        RunCommand = new SimpleCommand(_ => { });
        OpenFolderCommand = new SimpleCommand(_ => { });
        // NOTE: Your XAML binds to PlayCommand and RunOpsCommand, which don't exist.
        // You'll need to fix this separately. See note below.

        // Add some sample data so the previewer isn't empty
        Items.Add(new Row {
            Title = "Example Game (Installed)",
            IsBuilt = true,
            PrimaryActionText = "Play"
        });
        Items.Add(new Row {
            Title = "Another Game (Not Installed)",
            IsBuilt = false,
            PrimaryActionText = "Run All Build Operations"
        });

    }

    /// <summary>
    /// Constructs the LibraryPage with the given OperationsEngine.
    /// </summary>
    /// <param name="engine"></param>
    public LibraryPage(Core.OperationsEngine engine) {
        try {
            _engine = engine;
            InitializeComponent();
            DataContext = this;

            RefreshCommand = new SimpleCommand(_ => Load());
            RunCommand = new SimpleCommand(p => {
                if (p is Row r) {
                    // If installed (has an exe), launch the game; otherwise run the module (e.g., install)
                    if (!string.IsNullOrWhiteSpace(r.ExePath))
                        _engine.LaunchGame(r.ModuleName);
                    else
                        _ = _engine.InstallModuleAsync(r.ModuleName); // fire-and-forget headless install/run
                }
            });
            OpenFolderCommand = new SimpleCommand(async p => {
                if (p is Row r) {
                    try {
                        var path = _engine.GetGamePath(r.ModuleName);
                        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                            return;
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
                    } catch (Exception ex) {
                        await EngineNet.Interface.GUI.Avalonia.PromptHelpers.InfoAsync($"An error occurred while trying to open the folder for '{r.ModuleName}': {ex.Message}", "Open Folder");
                    }
                }
            });

            Load();
        } catch (Exception ex) {
            Console.WriteLine($"[LibraryPage] Error during initialization: {ex}");
        }
    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Methods :: START :: */

    /// <summary>
    /// Loads the list of available games/modules into the Items collection.
    /// </summary>
    private void Load() {
        try {
            Items.Clear(); // reset
            if (_engine == null) {
                throw new InvalidOperationException(message: "Engine is not initialized.");
            }

            Dictionary<string, object?>? games = _engine.ListGames(); // installed + discovered
            foreach (KeyValuePair<string, object?> kv in games) {
                string? name = kv.Key;
                IDictionary<string, object?>? info = kv.Value as IDictionary<string, object?>;

                string? exe = info != null && info.TryGetValue(key: "exe", out var e) ? e?.ToString() : null;
                string? title = info != null && info.TryGetValue(key: "title", out var t) && !string.IsNullOrWhiteSpace(t?.ToString()) ? t!.ToString()! : name;
                string? gameRoot = info != null && info.TryGetValue(key: "game_root", out var gr) ? gr?.ToString() : null;

                string? imageUri = ResolveCoverUri(gameRoot);
                bool IsBuilt = !string.IsNullOrWhiteSpace(exe) || _engine.IsModuleInstalled(name);

                Items.Add(item: new Row {
                    ModuleName = name,
                    Title = title,
                    ExePath = exe,
                    ImageUri = imageUri,
                    IsBuilt = IsBuilt,
                    PrimaryActionText = IsBuilt ? "Play" : "Run All Build Operations"
                });
            }

            if (Items.Count == 0) {
                Items.Add(item: new Row { Title = "No games found.", ModuleName = "", PrimaryActionText = "—" });
            }
        } catch {
            Items.Add(item: new Row { Title = "Error loading games.", ModuleName = "", PrimaryActionText = "—" });
        }
    }

    /// <summary>
    /// Resolves the cover image URI for a game based on its root directory.
    /// </summary>
    /// <param name="gameRoot"></param>
    /// <returns>
    private static string ResolveCoverUri(string? gameRoot) {
        // 1) try <game_root>/icon.png
        string? icon = string.IsNullOrWhiteSpace(gameRoot) ? null : Path.Combine(gameRoot!, "icon.png");
        // 2) fallback to <project_root>/placeholder.png
        string? placeholder = Path.Combine(Directory.GetCurrentDirectory(), "placeholder.png");

        string pick = (!string.IsNullOrWhiteSpace(icon) && File.Exists(icon)) ? icon : File.Exists(placeholder) ? placeholder : placeholder; // placeholder, if missing just show nothing

        // Image.Source in Avalonia accepts file URIs
        return new Uri(pick, UriKind.Absolute).AbsoluteUri.StartsWith(value: "file:", StringComparison.OrdinalIgnoreCase)
            ? new Uri(pick).AbsoluteUri
            : new Uri(uriString: $"file:///{pick.Replace(oldValue: "\\", newValue: "/")}").AbsoluteUri;
    }

    /* :: :: Methods :: END :: */
    // //
    /* :: :: Nested Types :: START :: */

    /// <summary>
    /// Represents a single row/item in the library list.
    /// </summary>
    private sealed class Row {
        /* :: :: Properties :: START :: */
        public string ModuleName {
            get; set;
        } = "???";
        public string Title {
            get; set;
        } = "??";
        public string? ExePath {
            get; set;
        }
        public string ImageUri {
            get; set;
        } = "placeholder";
        public bool IsBuilt {
            get; set;
        }
        public string PrimaryActionText {
            get; set;
        } = "Run Built Output";
        /* :: :: Properties :: END :: */
    }

    /// <summary>
    /// A simple implementation of ICommand that executes a given action.
    /// </summary>
    private sealed class SimpleCommand:ICommand {

        private readonly Action<object?> _a;
        public SimpleCommand(Action<object?> a) => _a = a;
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) => _a(p);
        public event EventHandler? CanExecuteChanged;
    }
}
