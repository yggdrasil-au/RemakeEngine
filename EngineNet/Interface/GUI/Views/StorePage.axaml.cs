
namespace EngineNet.Interface.GUI.Views.Pages;

internal partial class StorePage:UserControl {

    /* :: :: Vars :: START :: */
    // //
    private readonly Core.OperationsEngine? _engine;

    internal ObservableCollection<StoreItem> Items {
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
    internal string Query {
        get => _query; set {
            _query = value;
            Raise(nameof(Query));
        }
    }

    internal ICommand RefreshCommand {
        get;
    }
    internal ICommand SearchCommand {
        get;
    }
    internal ICommand DownloadCommand {
        get;
    }
    internal ICommand InstallCommand {
        get;
    }

    /* :: :: Vars :: END :: */
    // // 
    /* :: :: Constructors :: START :: */

    // used only for previewer
    public StorePage() {

        DataContext = this; // make every instance var an available binding
        InitializeComponent();

        RefreshCommand = new Cmd(async _ => await LoadAsync());
        SearchCommand = new Cmd(async _ => await LoadAsync(Query));
        DownloadCommand = new Cmd(async item => await DownloadAsync(item as StoreItem));
        InstallCommand = new Cmd(async item => await InstallAsync(item as StoreItem));

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
        DownloadCommand = new Cmd(async item => await DownloadAsync(item as StoreItem));
        InstallCommand = new Cmd(async item => await InstallAsync(item as StoreItem));

        _ = LoadAsync();
    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Methods :: START :: */

    /// <summary>
    /// Loads the list of available modules from the registry into the Items collection.
    /// </summary>
    /// <param name="query">Optional search query to filter by module name</param>
    /// <returns></returns>
    private async Task LoadAsync(string? query = null) {
        try {
            Status = "Loading…";
            Items.Clear();

            if (_engine == null) {
                throw new InvalidOperationException(message: "Engine is not initialized.");
            }

            // Get registered modules from RemakeRegistry/register.json
            _engine.GetRegistries().RefreshModules();
            IReadOnlyDictionary<string, object?> modules = _engine.GetRegistries().GetRegisteredModules();
            
            // Get already downloaded games
            Dictionary<string, object?> downloadedGames = _engine.ListGames();

            foreach (KeyValuePair<string, object?> kv in modules) {
                string moduleName = kv.Key;
                
                // Apply search filter if provided
                if (!string.IsNullOrWhiteSpace(query) && 
                    !moduleName.Contains(query, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                IDictionary<string, object?>? moduleInfo = kv.Value as IDictionary<string, object?>;
                
                string? url = null;
                string? title = null;
                string? description = null;

                if (moduleInfo != null) {
                    url = moduleInfo.TryGetValue("url", out object? u) ? u?.ToString() : null;
                    title = moduleInfo.TryGetValue("title", out object? t) ? t?.ToString() : null;
                    description = moduleInfo.TryGetValue("description", out object? d) ? d?.ToString() : null;
                }

                // Check if already downloaded
                bool isDownloaded = downloadedGames.ContainsKey(moduleName);
                
                // Check if installed (has game.toml with exe)
                bool isInstalled = false;
                if (isDownloaded && downloadedGames.TryGetValue(moduleName, out object? gameObj) &&
                    gameObj is IDictionary<string, object?> gameInfo) {
                    isInstalled = gameInfo.TryGetValue("exe", out object? exe) && 
                                  !string.IsNullOrWhiteSpace(exe?.ToString());
                }

                Items.Add(new StoreItem {
                    Id = moduleName,
                    Name = moduleName,
                    Title = title ?? moduleName,
                    Description = description ?? "No description available",
                    Url = url,
                    IsDownloaded = isDownloaded,
                    IsInstalled = isInstalled,
                    CanDownload = !isDownloaded && !string.IsNullOrWhiteSpace(url),
                    CanInstall = isDownloaded && !isInstalled
                });
            }

            Status = Items.Count == 0 ? "No modules found." : $"{Items.Count} module(s)";
            
            await Task.CompletedTask;
        } catch (Exception ex) {
            Status = "Failed to load store.";
            Items.Clear();
            Items.Add(new StoreItem { 
                Name = "Error", 
                Title = "Error",
                Description = ex.Message,
                IsDownloaded = false,
                CanDownload = false,
                CanInstall = false
            });
        }
    }

    /// <summary>
    /// Downloads a module from its Git URL.
    /// </summary>
    /// <param name="item"></param>
    /// <returns></returns>
    private async Task DownloadAsync(StoreItem? item) {
        if (item is null || string.IsNullOrWhiteSpace(item.Url)) {
            return;
        }

        try {
            if (_engine == null) {
                throw new InvalidOperationException(message: "Engine is not initialized.");
            }
            
            Status = $"Downloading {item.Name}…";

            // Use the engine's DownloadModule method (wraps git clone)
            bool success = await Task.Run(() => _engine.DownloadModule(item.Url!));

            if (success) {
                Status = $"Downloaded {item.Name} successfully.";
                // Reload to update download status
                await LoadAsync(Query);
            } else {
                Status = $"Failed to download {item.Name}.";
            }
        } catch (Exception ex) {
            Status = $"Download failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Installs a downloaded module by running its initialization operations.
    /// </summary>
    /// <param name="item"></param>
    /// <returns></returns>
    private async Task InstallAsync(StoreItem? item) {
        if (item is null || !item.IsDownloaded) {
            return;
        }

        try {
            if (_engine == null) {
                throw new InvalidOperationException(message: "Engine is not initialized.");
            }
            
            Status = $"Installing {item.Name}…";
            
            // Start operation in output service
            OperationOutputService.StartOperation("Install Module", item.Name);

            // Use InstallModuleAsync with event routing to BuildingPage
            bool success = await _engine.InstallModuleAsync(
                item.Name,
                onOutput: (line, streamName) => {
                    OperationOutputService.AddOutput(line, streamName);
                },
                onEvent: (evt) => {
                    OperationOutputService.HandleEvent(evt);
                }
            );

            if (success) {
                Status = $"Installed {item.Name} successfully.";
                // Reload to update install status
                await LoadAsync(Query);
            } else {
                Status = $"Failed to install {item.Name}.";
            }
        } catch (Exception ex) {
            Status = $"Install failed: {ex.Message}";
        }
    }

    /* :: :: Methods :: END :: */
    // //
    /* :: :: Nested Types :: START :: */

    /// <summary>
    /// Represents a store item (module from registry).
    /// </summary>
    internal sealed class StoreItem {
        public string Id {
            get; set;
        } = "";
        public string Name {
            get; set;
        } = "";
        public string Title {
            get; set;
        } = "";
        public string Description {
            get; set;
        } = "";
        public string? Url {
            get; set;
        }
        public bool IsDownloaded {
            get; set;
        }
        public bool IsInstalled {
            get; set;
        }
        public bool CanDownload {
            get; set;
        }
        public bool CanInstall {
            get; set;
        }
    }

    private event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, e: new PropertyChangedEventArgs(name));

    private sealed class Cmd(Func<object?, Task> run):ICommand {
        private readonly Func<object?, Task> _run = run;

        public bool CanExecute(object? p) => true;
        public async void Execute(object? p) => await _run(p);
        public event EventHandler? CanExecuteChanged;
    }
}
