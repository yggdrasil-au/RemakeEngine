
namespace EngineNet.Interface.GUI.Views.Pages;

internal partial class StorePage:UserControl {

    /* :: :: Vars :: START :: */
    // //
    private readonly Core.OperationsEngine? _engine;

    private ObservableCollection<StoreItem> Items {
        get;
    } = new ObservableCollection<StoreItem>();

    // //

    private string _status = "";
    internal string Status {
        get => _status; private set {
            _status = value;
            Raise(nameof(Status));
        }
    }

    private string _query = "";
    private string Query {
        get => _query; set {
            _query = value;
            Raise(nameof(Query));
        }
    }

    private ICommand RefreshCommand {
        get;
    }
    private ICommand SearchCommand {
        get;
    }
    private ICommand BuildCommand {
        get;
    }
    private ICommand DetailsCommand {
        get;
    }

    /* :: :: Vars :: END :: */
    // // 
    /* :: :: Constructors :: START :: */

    // used only for previewer
    public StorePage() {

        DataContext = this;
        InitializeComponent();

        RefreshCommand = new Cmd(async _ => await LoadAsync());
        SearchCommand = new Cmd(async _ => await LoadAsync(Query));
        BuildCommand = new Cmd(async item => await BuildAsync(item as StoreItem));
        DetailsCommand = new Cmd(async item => await ShowDetailsAsync(item as StoreItem));

        _ = LoadAsync();
    }

    /// <summary>
    /// Constructs the StorePage with the given OperationsEngine.
    /// </summary>
    /// <param name="engine"></param>
    public StorePage(Core.OperationsEngine engine) {
        _engine = engine;
        DataContext = this;
        InitializeComponent();

        RefreshCommand = new Cmd(async _ => await LoadAsync());
        SearchCommand = new Cmd(async _ => await LoadAsync(Query));
        BuildCommand = new Cmd(async item => await BuildAsync(item as StoreItem));
        DetailsCommand = new Cmd(async item => await ShowDetailsAsync(item as StoreItem));

        _ = LoadAsync();
    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Methods :: START :: */

    /// <summary>
    /// loads the list of available store items into the Items collection.
    /// </summary>
    /// <param name="query"></param>
    /// <returns></returns>
    private async Task LoadAsync(string? query = null) {
        try {
            Status = "Loading�";
            Items.Clear();

            if (_engine == null) {
                throw new InvalidOperationException(message: "Engine is not initialized.");
            }

            // Try a few likely engine shapes; fall back to demo list.
            IEnumerable<StoreItem>? list = TryGetStoreList(query) ?? DemoList(query);
            foreach (StoreItem s in list)
                Items.Add(s);

            Status = Items.Count == 0 ? "No results." : $"{Items.Count} item(s)";
        } catch (Exception ex) {
            Status = "Failed to load store.";
            Items.Clear();
            Items.Add(new StoreItem { Name = "Error", Description = ex.Message });
        }
    }

    /// <summary>
    /// Tries to get the store list from the engine using common method shapes.
    /// </summary>
    /// <param name="query"></param>
    /// <returns></returns>
    private System.Collections.Generic.IEnumerable<StoreItem>? TryGetStoreList(string? query) {
        try {
            if (_engine == null) {
                throw new InvalidOperationException(message: "Engine is not initialized.");
            }
            dynamic? games = _engine.ListGames();
            dynamic? all = Project(games);
            // Option 3: _engine.GetAvailableGames()
            if (all is null)
                return null;

            return string.IsNullOrWhiteSpace(query)
                ? all
                : all.Where((Func<StoreItem, bool>)(i => i.Name != null && i.Name.Contains(query, StringComparison.OrdinalIgnoreCase)));
        } catch { /* ignore */ }

        return null;
    }

    private static System.Collections.Generic.IEnumerable<StoreItem> Project(dynamic raw) {
        List<StoreItem>? items = new System.Collections.Generic.List<StoreItem>();
        foreach (dynamic it in raw) {
            try {
                string name = it.Name ?? it.name ?? it.id ?? "Item";
                string desc = it.Description ?? it.description ?? "";
                string id = it.Id ?? it.id ?? name;
                items.Add(new StoreItem { Id = id, Name = name, Description = desc });
            } catch {
                items.Add(new StoreItem { Name = "Unknown", Description = "Unrecognized store item shape" });
            }
        }
        return items;
    }

    private static System.Collections.Generic.IEnumerable<StoreItem> DemoList(string? query) {
        StoreItem[]? demo = new[] {
            new StoreItem{ Id="demo-1", Name="Example Game", Description="Sample item from demo source."},
            new StoreItem{ Id="demo-2", Name="Another Game", Description="Replace with engine.Store.List()."},
        };
        return string.IsNullOrWhiteSpace(query)
            ? demo
            : demo.Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private async Task BuildAsync(StoreItem? item) {
        if (item is null)
            return;

        try {
            if (_engine == null) {
                throw new InvalidOperationException(message: "Engine is not initialized.");
            }
            Status = $"Building {item.Name}�";

            // TODO implemnt

            Status = $"Queued Build for {item.Name}.";
        } catch (Exception ex) {
            Status = $"Build failed: {ex.Message}";
        }

        await Task.Yield();
    }

    /// <summary>
    /// Shows details for the given store item.
    /// </summary>
    /// <param name="item"></param>
    /// <returns></returns>
    private async Task ShowDetailsAsync(StoreItem? item) {
        if (item is null)
            return;
        Status = $"Details: {item.Name}";
        await Task.Yield();
    }

    /* :: :: Methods :: END :: */
    // //
    /* :: :: Nested Types :: START :: */

    /// <summary>
    /// Represents a store item.
    /// </summary>
    private sealed class StoreItem {
        public string Id {
            get; set;
        } = "";
        public string Name {
            get; set;
        } = "";
        public string Description {
            get; set;
        } = "";
    }

    private event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, e: new PropertyChangedEventArgs(name));

    private sealed class Cmd:ICommand {
        private readonly Func<object?, Task> _run;
        public Cmd(Func<object?, Task> run) => _run = run;
        public bool CanExecute(object? p) => true;
        public async void Execute(object? p) => await _run(p);
        public event EventHandler? CanExecuteChanged;
    }
}
