
using System.Collections.ObjectModel;
using System.Collections.Generic;

using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace EngineNet.Interface.GUI.Pages;

/// <summary>
/// library page in the Graphical Interface.
/// </summary>
public partial class LibraryPage:UserControl {

    /* :: :: Vars :: START :: */
    // //
    private readonly Core.Engine? _engine;

    private ObservableCollection<Row> Items {
        get;
    } = new ObservableCollection<Row>();

    // //

    private System.Windows.Input.ICommand? Button_Refresh_Click {
        get;
    }
    private System.Windows.Input.ICommand? Button_Details_Click {
        get;
    }

    private Row? SelectedRow {
        get; set;
    }

    /* :: :: Vars :: END :: */
    // //
    /* :: :: Constructors :: START :: */

    // used only for previewer
    public LibraryPage() {
        InitializeComponent();
        DataContext = this; // Set DataContext for design-time bindings

        // Initialize commands to prevent binding errors in the designer
        Button_Refresh_Click = new SimpleCommand(_ => { });
        Button_Details_Click = new SimpleCommand(_ => { });
        // You'll need to fix this separately. See note below.

        // Add some sample data so the previewer isn't empty
        Items.Add(item: new Row {
            Title = "Example Game (Installed)",
            IsBuilt = true,
            PrimaryActionText = "Play",
            ExePath = string.Empty,
            ModuleName = string.Empty
        });
        Items.Add(item: new Row {
            Title = "Another Game (Not Installed)",
            IsBuilt = false,
            PrimaryActionText = "Run All Build Operations",
            ExePath = string.Empty,
            ModuleName = string.Empty
        });

    }

    /// <summary>
    /// Constructs the LibraryPage with the given OperationsEngine.
    /// </summary>
    /// <param name="engine"></param>
    internal LibraryPage(Core.Engine engine) {
        try {
            _engine = engine;
            InitializeComponent();
            DataContext = this;

            Button_Refresh_Click = new SimpleCommand(_ => Load());

            Button_Details_Click = new SimpleCommand(p => {
                if (p is Row r && !string.IsNullOrWhiteSpace(r.ModuleName)) {
                    ShowDetails(r.ModuleName);
                }
            });

            Load();
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"[GUI :: LibraryPage.axaml.cs::LibraryPage()::constructor] Error during initialization: {ex}");
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
                Core.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::Load()] Load() aborted: _engine is null.");
                throw new System.InvalidOperationException(message: "Engine is not initialized.");
            }

            var modules = _engine.Modules(Core.Utils.ModuleFilter.Installed);
#if DEBUG
        Core.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::Load()] Found {modules.Count} modules.");
            // list all modules
            foreach (var kv in modules) {
                var m = kv.Value;
                Core.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::Load()]   Module: {m.Name}, Installed: {m.IsInstalled}, Built: {m.IsBuilt}, Unverified: {m.IsUnverified}, Registered: {m.IsRegistered}");
            }
#endif
            foreach (var kv in modules) {
                var m = kv.Value;
                string name = m.Name;
                string? exe = m.ExePath;
                string title = string.IsNullOrWhiteSpace(m.Title) ? name : m.Title!;
                string gameRoot = m.GameRoot;
                if (string.IsNullOrWhiteSpace(gameRoot)) {
        Core.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::Load(): Module '{name}' has no game root defined.");
                }

                bool isBuilt = m.IsBuilt;
                bool isInstalled = m.IsInstalled;
                bool isRegistered = m.IsRegistered;
                bool isUnverified = m.IsUnverified;
                bool isUnbuilt = isInstalled && !isBuilt;

                string primaryActionText = isBuilt ? "Play" : "Run All Build Operations";

                Items.Add(new Row {
                    ModuleName = name,
                    Title = title,
                    ExePath = exe,
                    Image = ResolveCoverUri(gameRoot),
                    IsBuilt = isBuilt,
                    IsInstalled = isInstalled,
                    IsRegistered = isRegistered,
                    IsUnverified = isUnverified,
                    IsUnbuilt = isUnbuilt,
                    PrimaryActionText = primaryActionText
                });
            }

            if (Items.Count == 0) {
                Core.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::Load()] No games found. Adding placeholder row.");
                Items.Add(item: new Row {
                    Title = "No games found.",
                    ModuleName = "",
                    PrimaryActionText = "",
                    ExePath = string.Empty
                });
            }
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"[GUI :: LibraryPage.axaml.cs::Load()] Exception during Load(): {ex}");
            Items.Add(item: new Row {
                Title = "Error loading games.",
                ModuleName = "",
                PrimaryActionText = "",
                ExePath = string.Empty
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
            Core.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::ResolveCoverUri() aborted: _engine is null.");
            throw new System.InvalidOperationException(message: "Engine is not initialized.");
        }
        if (string.IsNullOrWhiteSpace(gameRoot)) {
            return null;
        }
        // 1) try <game_root>/icon.png
        string? icon = null;
        if (string.IsNullOrWhiteSpace(gameRoot)) {
            Core.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::ResolveCoverUri() aborted: gameRoot is null/whitespace; skipping icon.png.");
        } else {
            icon = System.IO.Path.Combine(gameRoot, "icon.png");
        }

        // 2) fallback to <project_root>/placeholder.png
        string placeholder = System.IO.Path.Combine(_engine.RootPath, "placeholder.png");

        string pick;
        if (!string.IsNullOrWhiteSpace(icon) && System.IO.File.Exists(icon)) {
            pick = icon;
        } else {
            if (System.IO.File.Exists(placeholder)) {
                Core.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::ResolveCoverUri() Using placeholder image at '{placeholder}'.");
                pick = placeholder;
            } else {
                Core.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::ResolveCoverUri() Placeholder missing at '{placeholder}'. Returning URI may reference a non-existent file.");
                // Keep the same behavior as original (still set to placeholder path even if missing)
                pick = placeholder;
            }
        }

        if (System.IO.File.Exists(pick)) {
            try {
                return new Bitmap(pick); // Load the image
            } catch (System.Exception ex) {
                Core.Diagnostics.Bug($"[GUI :: LibraryPage.axaml.cs::ResolveCoverUri() Failed to load bitmap at '{pick}': {ex.Message}");
                return null; // Return null if loading fails
            }
        } else {
            Core.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::ResolveCoverUri() Image file missing at '{pick}'.");
            return null; // Return null if no file exists
        }
    }

    /* :: :: Methods :: END :: */
    // //
    internal void ShowDetailsPublic(string moduleName) => ShowDetails(moduleName);

    private void ShowDetails(string moduleName) {
        try {
            ContentControl? host = this.FindControl<ContentControl>(name: "DetailsHost");
            ScrollViewer? cards = this.FindControl<ScrollViewer>(name: "CardsGrid");
            if (host is null || cards is null) return;
            if (_engine is null) return;
            host.Content = new ModulePage(_engine, moduleName);
            host.IsVisible = true;
            cards.IsVisible = false;
        } catch {
            /* ignore */
        }
    }

    private void ShowCards() {
        try {
            ContentControl? host = this.FindControl<ContentControl>(name: "DetailsHost");
            ScrollViewer? cards = this.FindControl<ScrollViewer>(name: "CardsGrid");
            if (host is null || cards is null) return;
            host.Content = null;
            host.IsVisible = false;
            cards.IsVisible = true;
        } catch {
            /* ignore */
        }
    }

    private void OnModuleSelected(object? sender, SelectionChangedEventArgs e) {
        if (SelectedRow is Row r && !string.IsNullOrWhiteSpace(r.ModuleName)) {
            ShowDetails(r.ModuleName);
        }
    }

    private void OnBackToAll(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
        ShowCards();
    }

    /* :: :: Nested Types :: START :: */

    /// <summary>
    /// Represents a single row/item in the library list.
    /// </summary>
    private sealed class Row {
        public required string ModuleName { get; set; }
        public required string Title { get; set; }
        public required string ExePath { get; set; }
        public Bitmap? Image { get; set; }
        public bool IsBuilt { get; set; }
        public bool IsInstalled { get; set; }
        public bool IsRegistered { get; set; }
        public bool IsUnverified { get; set; }
        public bool IsUnbuilt { get; set; }
        public string PrimaryActionText {
            get; set;
        } = "Run Built Output";
    }

    /// <summary>
    /// A simple implementation of System.Windows.Input.ICommand that executes a given action.
    /// </summary>
    private sealed class SimpleCommand:System.Windows.Input.ICommand {

        private readonly System.Action<object?> _a;
        public SimpleCommand(System.Action<object?> a) => _a = a;
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) => _a(p);
        public event System.EventHandler? CanExecuteChanged;
    }

    /* :: :: Nested Types :: END :: */
}

