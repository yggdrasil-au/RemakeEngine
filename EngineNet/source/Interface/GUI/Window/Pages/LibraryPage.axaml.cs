
namespace EngineNet.Interface.GUI.Pages;

/// <summary>
/// library page in the Graphical Interface.
/// </summary>
public partial class LibraryPage:UserControl {

    /* :: :: Vars :: START :: */
    // //
    public ObservableCollection<Row> Items {
        get;
    } = new ObservableCollection<Row>();

    // //

    public System.Windows.Input.ICommand? Button_Refresh_Click {
        get;
    }
    public System.Windows.Input.ICommand? Button_Details_Click {
        get;
    }

    public Row? SelectedRow {
        get; set;
    }

    /* :: :: Vars :: END :: */
    // //
    /* :: :: Constructors :: START :: */

    /// <summary>
    /// Constructs the LibraryPage with the given OperationsEngine.
    /// </summary>
    /// <param name="engine"></param>
    public LibraryPage() {
        try {
            Button_Refresh_Click = new SimpleCommand(_ => Load());

            Button_Details_Click = new SimpleCommand(p => {
                if (p is Row r && !string.IsNullOrWhiteSpace(r.ModuleName)) {
                    ShowDetails(r.ModuleName);
                }
            });

            DataContext = this;
            InitializeComponent();

            Load();
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[GUI :: LibraryPage.axaml.cs::LibraryPage()::constructor] Error during initialization: {ex}");
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
            if (GuiBootstrapper.MiniEngine == null) {
                Shared.IO.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::Load()] Load() aborted: GuiBootstrapper.MiniEngine is null.");
                throw new System.InvalidOperationException(message: "Engine is not initialized.");
            }

            var modules = GuiBootstrapper.MiniEngine.GameRegistry_GetModules(Core.Data.ModuleFilter.Installed);
#if DEBUG
            Shared.IO.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::Load()] Found {modules.Count} modules.");
            // list all modules
            foreach (var kv in modules) {
                var m = kv.Value;
                Shared.IO.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::Load()]   Module: {m.Name}, Installed: {m.IsInstalled}, Built: {m.IsBuilt}, Unverified: {m.IsUnverified}, Registered: {m.IsRegistered}");
            }
#endif
            foreach (var item in modules.Values.Select(m => (
                Name: m.Name,
                ExePath: m.ExePath,
                Title: string.IsNullOrWhiteSpace(m.Title) ? m.Name : m.Title,
                GameRoot: m.GameRoot,
                IsBuilt: m.IsBuilt,
                IsInstalled: m.IsInstalled,
                IsRegistered: m.IsRegistered,
                IsUnverified: m.IsUnverified
            ))) {
                if (string.IsNullOrWhiteSpace(item.GameRoot)) {
                    Shared.IO.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::Load(): Module '{item.Name}' has no game root defined.");
                }

                bool isBuilt = item.IsBuilt;
                bool isInstalled = item.IsInstalled;
                bool isRegistered = item.IsRegistered;
                bool isUnverified = item.IsUnverified;
                bool isUnbuilt = isInstalled && !isBuilt;

                string primaryActionText = isBuilt ? "Play" : "Run All Build Operations";

                Items.Add(new Row {
                    ModuleName = item.Name,
                    Title = item.Title,
                    ExePath = item.ExePath,
                    Image = ResolveCoverUri(item.GameRoot),
                    IsBuilt = isBuilt,
                    IsInstalled = isInstalled,
                    IsRegistered = isRegistered,
                    IsUnverified = isUnverified,
                    IsUnbuilt = isUnbuilt,
                    PrimaryActionText = primaryActionText
                });
            }

            if (Items.Count == 0) {
                Shared.IO.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::Load()] No games found. Adding placeholder row.");
                Items.Add(item: new Row {
                    Title = "No games found.",
                    ModuleName = "",
                    PrimaryActionText = "",
                    ExePath = string.Empty
                });
            }
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[GUI :: LibraryPage.axaml.cs::Load()] Exception during Load(): {ex}");
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
        if (GuiBootstrapper.MiniEngine == null) {
            Shared.IO.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::ResolveCoverUri() aborted: GuiBootstrapper.MiniEngine is null.");
            throw new System.InvalidOperationException(message: "Engine is not initialized.");
        }
        if (string.IsNullOrWhiteSpace(gameRoot)) {
            return null;
        }
        // 1) try <game_root>/icon.png
        string? icon = null;
        if (string.IsNullOrWhiteSpace(gameRoot)) {
            Shared.IO.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::ResolveCoverUri() aborted: gameRoot is null/whitespace; skipping icon.png.");
        } else {
            icon = System.IO.Path.Combine(gameRoot, "icon.png");
        }

        // 2) fallback to <project_root>/placeholder.png
        string placeholder = System.IO.Path.Combine(EngineNet.Core.Lib.RootPath, "placeholder.png");

        string pick;
        if (!string.IsNullOrWhiteSpace(icon) && System.IO.File.Exists(icon)) {
            pick = icon;
        } else {
            if (System.IO.File.Exists(placeholder)) {
                Shared.IO.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::ResolveCoverUri() Using placeholder image at '{placeholder}'.");
                pick = placeholder;
            } else {
                Shared.IO.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::ResolveCoverUri() Placeholder missing at '{placeholder}'. Returning URI may reference a non-existent file.");
                // Keep the same behavior as original (still set to placeholder path even if missing)
                pick = placeholder;
            }
        }

        if (System.IO.File.Exists(pick)) {
            try {
                return new Bitmap(pick); // Load the image
            } catch (System.Exception ex) {
                Shared.IO.Diagnostics.Bug($"[GUI :: LibraryPage.axaml.cs::ResolveCoverUri() Failed to load bitmap at '{pick}': {ex.Message}");
                return null; // Return null if loading fails
            }
        } else {
            Shared.IO.Diagnostics.Log($"[GUI :: LibraryPage.axaml.cs::ResolveCoverUri() Image file missing at '{pick}'.");
            return null; // Return null if no file exists
        }
    }

    /* :: :: Methods :: END :: */
    // //
    public void ShowDetailsPublic(string moduleName) => ShowDetails(moduleName);

    private void ShowDetails(string moduleName) {
        try {
            ContentControl? host = this.FindControl<ContentControl>(name: "DetailsHost");
            ScrollViewer? cards = this.FindControl<ScrollViewer>(name: "CardsGrid");
            if (host is null || cards is null) return;
            if (GuiBootstrapper.MiniEngine is null) return;
            host.Content = new ModulePage(moduleName);
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
    public sealed class Row {
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
        public event System.EventHandler? CanExecuteChanged {
            add { }
            remove { }
        }
    }

    /* :: :: Nested Types :: END :: */
}

