
using Avalonia.Media;
using Avalonia.Media.Imaging;
using EngineNet.Interface.GUI.ViewModels;
using System.Diagnostics;

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
    private ICommand PlayCommand {
        get;
    }
    private ICommand RunOpsCommand {
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
        PlayCommand = new SimpleCommand(_ => { });
        RunOpsCommand = new SimpleCommand(_ => { });
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

            PlayCommand = new SimpleCommand(p => {
                if (p is Row r && !string.IsNullOrWhiteSpace(r.ExePath)) {
                    _engine.LaunchGame(r.ModuleName);
                }
            });

            RunOpsCommand = new SimpleCommand(p => {
                if (p is Row r && string.IsNullOrWhiteSpace(r.ExePath)) {
                    DebugWriteLine(message: $"[LibraryPage] No ExePath for '{r.ModuleName}'. Triggering headless install/run.");
                    _ = _engine.InstallModuleAsync(r.ModuleName); // fire-and-forget headless install/run
                }
            });

            OpenFolderCommand = new SimpleCommand(async p => {
                if (p is Row r) {
                    try {
                        string? path = _engine.GetGamePath(r.ModuleName);
                        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) {
                            DebugWriteLine(message: $"[LibraryPage] OpenFolder skipped for '{r.ModuleName}'. Path missing or doesn't exist: '{path ?? "<null>"}'");
                            return;
                        }
                        ProcessStartInfo? psi = new System.Diagnostics.ProcessStartInfo { UseShellExecute = true };
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
                        DebugWriteLine($"[LibraryPage] Exception while opening folder for '{r.ModuleName}': {ex}");
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
                DebugWriteLine("[LibraryPage] Load() aborted: _engine is null.");
                throw new InvalidOperationException(message: "Engine is not initialized.");
            }

            Dictionary<string, object?>? games = _engine.ListGames(); // installed + discovered
            foreach (KeyValuePair<string, object?> kv in games) {
                string? name = kv.Key;
                IDictionary<string, object?>? info = kv.Value as IDictionary<string, object?>;

                string? exe = null;
                if (info != null && info.TryGetValue(key: "exe", out var e)) {
                    exe = e?.ToString();
                } else {
                    Debug.WriteLine($"[LibraryPage] 'exe' missing for '{name}'.");
                }

                string? title;
                if (info != null &&
                    info.TryGetValue(key: "title", out var t) &&
                    !string.IsNullOrWhiteSpace(t?.ToString())) {
                    title = t!.ToString()!;
                } else {
                    title = name;
                    Debug.WriteLine($"[LibraryPage] Title missing/blank for '{name}'. Falling back to module name.");
                }

                string? gameRoot = null;
                if (info != null && info.TryGetValue(key: "game_root", out var gr)) {
                    gameRoot = gr?.ToString();
                } else {
                    DebugWriteLine($"[LibraryPage] 'game_root' missing for '{name}'.");
                }

                //string? imageUri = ResolveCoverUri(gameRoot);

                bool IsBuilt = !string.IsNullOrWhiteSpace(exe) || _engine.IsModuleInstalled(name);

                string primaryActionText;
                if (IsBuilt) {
                    primaryActionText = "Play";
                } else {
                    primaryActionText = "Run All Build Operations";
                }

                Items.Add(new Row {
                    ModuleName = name,
                    Title = title,
                    ExePath = exe,
                    Image = ResolveCoverUri(gameRoot),
                    IsBuilt = IsBuilt,
                    PrimaryActionText = primaryActionText
                });
            }

            if (Items.Count == 0) {
                DebugWriteLine("[LibraryPage] No games found. Adding placeholder row.");
                Items.Add(new Row {
                    Title = "No games found.",
                    ModuleName = "",
                    PrimaryActionText = "—"
                });
            }
        } catch (Exception ex) {
            DebugWriteLine($"[LibraryPage] Exception during Load(): {ex}");
            Items.Add(new Row {
                Title = "Error loading games.",
                ModuleName = "",
                PrimaryActionText = "—"
            });
        }
    }

    /// <summary>
    /// Resolves the cover image URI for a game based on its root directory.
    /// </summary>
    /// <param name="gameRoot"></param>
    /// <returns>
    private Bitmap? ResolveCoverUri(string? gameRoot) {
        if (_engine == null) {
            DebugWriteLine("[LibraryPage] Load() aborted: _engine is null.");
            throw new InvalidOperationException(message: "Engine is not initialized.");
        }
        // 1) try <game_root>/icon.png
        string? icon = null;
        if (string.IsNullOrWhiteSpace(gameRoot)) {
            DebugWriteLine("[LibraryPage] ResolveCoverUri: gameRoot is null/whitespace; skipping icon.png.");
        } else {
            icon = Path.Combine(gameRoot, "icon.png");
        }

        // 2) fallback to <project_root>/placeholder.png
        string placeholder = Path.Combine(_engine.GetRootPath(), "placeholder.png");

        string pick;
        if (!string.IsNullOrWhiteSpace(icon) && File.Exists(icon)) {
            pick = icon;
        } else {
            if (File.Exists(placeholder)) {
                DebugWriteLine($"[LibraryPage] ResolveCoverUri: Using placeholder image at '{placeholder}'.");
                pick = placeholder;
            } else {
                DebugWriteLine($"[LibraryPage] ResolveCoverUri: Placeholder missing at '{placeholder}'. Returning URI may reference a non-existent file.");
                // Keep the same behavior as original (still set to placeholder path even if missing)
                pick = placeholder;
            }
        }

        if (File.Exists(pick)) {
            try {
                return new Bitmap(pick); // Load the image
            } catch (Exception ex) {
                DebugWriteLine($"[LibraryPage] Failed to load bitmap at '{pick}': {ex.Message}");
                return null; // Return null if loading fails
            }
        } else {
            DebugWriteLine($"[LibraryPage] ResolveCoverUri: Image file missing at '{pick}'.");
            return null; // Return null if no file exists
        }
    }

    /* :: :: Methods :: END :: */
    // //
    /* :: :: Nested Types :: START :: */

    /// <summary>
    /// Represents a single row/item in the library list.
    /// </summary>
    private sealed class Row {
        public string ModuleName {
            get; set;
        } = "???";
        public string Title {
            get; set;
        } = "??";
        public string? ExePath {
            get; set;
        }
        public Bitmap? Image {
            get; set;
        }
        public bool IsBuilt {
            get; set;
        }
        public string PrimaryActionText {
            get; set;
        } = "Run Built Output";
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

    /* :: :: Nested Types :: END :: */

    private static void DebugWriteLine(string message) {
#if DEBUG
        Debug.WriteLine(message);
#endif
    }
}
