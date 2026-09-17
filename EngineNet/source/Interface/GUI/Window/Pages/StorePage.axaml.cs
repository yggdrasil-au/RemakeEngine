
using EngineNet.Shared.IO.UI;

namespace EngineNet.Interface.GUI.Pages;

public sealed partial class StorePage:UserControl, INotifyPropertyChanged {

    /* :: :: Vars :: START :: */
    // //

    public ObservableCollection<StoreItem> Items { get; } = new();

    // //

    private string _status = "";
    public string Status {
        get => _status; private set {
            _status = value;
            Raise(name: nameof(Status));
        }
    }

    private string _query = "";
    public string Query {
        get => _query; set {
            _query = value;
            Raise(name: nameof(Query));
        }
    }

    public System.Windows.Input.ICommand Button_Refresh_Click {
        get;
    }
    public System.Windows.Input.ICommand Button_Search_Click {
        get;
    }
    public System.Windows.Input.ICommand Button_Download_Click {
        get;
    }
    public System.Windows.Input.ICommand Button_OpenModule_Click {
        get;
    }

    /* :: :: Vars :: END :: */
    // //
    /* :: :: Constructors :: START :: */

    /// <summary>
    /// Constructs the StorePage with the given OperationsEngine.
    /// </summary>
    /// <param name="engine"></param>
    public StorePage() {

        Button_Refresh_Click = new Cmd(run: async _ => await LoadAsync());
        Button_Search_Click = new Cmd(run: async _ => await LoadAsync(query: Query));
        Button_Download_Click = new Cmd(run: async item => await DownloadAsync(item: item as StoreItem));
        Button_OpenModule_Click = new Cmd(run: async item => await OpenModuleAsync(item: item as StoreItem));

        DataContext = this;
        InitializeComponent();

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
    private async System.Threading.Tasks.Task LoadAsync(string? query = null) {
        try {
            Status = "Loading…";
            Items.Clear();

            if (GuiBootstrapper.MiniEngine == null) {
                throw new InvalidOperationException("Engine is not initialized.");
            }

            // Get registered modules from EngineApps\Registries\Modules\Main.json
            GuiBootstrapper.MiniEngine.GameRegistry_RefreshModules();
            IReadOnlyDictionary<string, object?> modules = GuiBootstrapper.MiniEngine.GameRegistry_GetRegisteredModules();

            // Get already downloaded games
            Core.Data.GameModules downloadedGames = GuiBootstrapper.MiniEngine.GameRegistry_GetModules(filter: Core.Data.ModuleFilter.Installed);

            foreach (KeyValuePair<string, object?> kv in modules) {
                string moduleName = kv.Key;

                // Apply search filter if provided
                if (!string.IsNullOrWhiteSpace(query) &&
                    !moduleName.Contains(query, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                IDictionary<string, object?>? moduleInfo = kv.Value as IDictionary<string, object?>;

                string? url = null;
                string? title = null;
                string? description = null;

                if (moduleInfo != null) {
                    url = moduleInfo.TryGetValue(key: "url", out object? u) ? u?.ToString() : null;
                    title = moduleInfo.TryGetValue(key: "title", out object? t) ? t?.ToString() : null;
                    description = moduleInfo.TryGetValue(key: "description", out object? d) ? d?.ToString() : null;
                }

                // Check if already downloaded
                bool isDownloaded = downloadedGames.ContainsKey(key: moduleName);

                // Check if installed (has game.toml with exe)
                bool isInstalled = false;
                if (isDownloaded && downloadedGames.TryGetValue(key: moduleName, out Core.Data.GameModuleInfo? gameInfo)) {
                    isInstalled = gameInfo.IsInstalled;
                }

                Items.Add(item: new StoreItem {
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
        } catch (System.Exception ex) {
            Status = "Failed to load store.";
            Items.Clear();
            Items.Add(item: new StoreItem {
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
    private async System.Threading.Tasks.Task DownloadAsync(StoreItem? item) {
        if (item is null || string.IsNullOrWhiteSpace(item.Url)) {
            return;
        }

        try {
            if (GuiBootstrapper.MiniEngine == null) {
                return;
            }

            Status = $"Downloading {item.Name}…";

            OperationOutputService.Instance.AddOutput(text: $"Starting download for {item.Name}…", stream: "stdout");

            bool success = await EngineOperationRunner.RunAsync(
                moduleName: item.Name,
                operationName: $"Download {item.Name}",
                executor: async (onOutput, onEvent, _) => {
                    onEvent(evt: new Dictionary<string, object?> {
                        [key: "event"] = EngineSdk.Events.Start,
                        [key: "name"] = item.Name,
                        [key: "url"] = item.Url ?? string.Empty
                    });

                    bool result = await Task.Run(function: () => GuiBootstrapper.MiniEngine.GitService_CloneModule(url: item.Url!));

                    onOutput(line: result ? $"Download complete for {item.Name}." : $"Download failed for {item.Name}.", streamName: result ? "stdout" : "stderr");
                    onEvent(evt: new Dictionary<string, object?> {
                        [key: "event"] = EngineSdk.Events.End,
                        [key: "success"] = result,
                        [key: "name"] = item.Name
                    });

                    return result;
                }
            );

            OperationOutputService.Instance.AddOutput(
                text: success ? $"Finished downloading {item.Name}." : $"Unable to download {item.Name}.",
                stream: success ? "stdout" : "stderr"
            );
            if (success) {
                Status = $"Downloaded {item.Name} successfully.";
                await LoadAsync(query: Query);
            } else {
                Status = $"Failed to download {item.Name}.";
            }
        } catch (System.Exception ex) {
            Status = $"Download failed: {ex.Message}";
        }
    }


    /* :: :: Methods :: END :: */
    // //
    /* :: :: Nested Types :: START :: */

    private async System.Threading.Tasks.Task OpenModuleAsync(StoreItem? item) {
        if (item is null) return;
        try {
            Window? w = TopLevel.GetTopLevel(visual: this) as Window;
            if (w is MainWindow mw && GuiBootstrapper.MiniEngine is not null) {
                mw.ShowLibraryFor(moduleName: item.Name);
            }
        } catch { /* ignore */ }
        await System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>
    /// Represents a store item (module from registry).
    /// </summary>
    public sealed class StoreItem {
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

    private event PropertyChangedEventHandler? _propertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged {
        add => _propertyChanged += value;
        remove => _propertyChanged -= value;
    }

    private void Raise(string name) => _propertyChanged?.Invoke(sender: this, e: new PropertyChangedEventArgs(propertyName: name));

    private sealed class Cmd(System.Func<object?, Task> run):System.Windows.Input.ICommand {
        private readonly Func<object?, Task> _run = run;
        public bool CanExecute(object? p) => true;
        public async void Execute(object? p) => await _run(arg: p);
        public event EventHandler? CanExecuteChanged {
            add { }
            remove { }
        }
    }
}
