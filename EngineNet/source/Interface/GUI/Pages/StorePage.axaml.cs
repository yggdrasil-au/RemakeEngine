
using System;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;

using Avalonia.Controls;


namespace EngineNet.Interface.GUI.Pages;

public partial class StorePage:UserControl, INotifyPropertyChanged {

    /* :: :: Vars :: START :: */
    // //
    private readonly Core.Engine? _engine;

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

    internal System.Windows.Input.ICommand Button_Refresh_Click {
        get;
    }
    internal System.Windows.Input.ICommand Button_Search_Click {
        get;
    }
    internal System.Windows.Input.ICommand Button_Download_Click {
        get;
    }
    internal System.Windows.Input.ICommand Button_OpenModule_Click {
        get;
    }

    /* :: :: Vars :: END :: */
    // //
    /* :: :: Constructors :: START :: */

    // used only for previewer
    public StorePage() {

        DataContext = this; // make every instance var an available binding
        InitializeComponent();

        Button_Refresh_Click = new Cmd(async _ => await LoadAsync());
        Button_Search_Click = new Cmd(async _ => await LoadAsync(Query));
        Button_Download_Click = new Cmd(async item => await DownloadAsync(item as StoreItem));
        Button_OpenModule_Click = new Cmd(async item => await OpenModuleAsync(item as StoreItem));

        _ = LoadAsync();
    }

    /// <summary>
    /// Constructs the StorePage with the given OperationsEngine.
    /// </summary>
    /// <param name="engine"></param>
    internal StorePage(Core.Engine engine) {
        _engine = engine;
        DataContext = this;
        InitializeComponent();

        Button_Refresh_Click = new Cmd(async _ => await LoadAsync());
        Button_Search_Click = new Cmd(async _ => await LoadAsync(Query));
        Button_Download_Click = new Cmd(async item => await DownloadAsync(item as StoreItem));
        Button_OpenModule_Click = new Cmd(async item => await OpenModuleAsync(item as StoreItem));

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

            if (_engine == null) {
                throw new InvalidOperationException(message: "Engine is not initialized.");
            }

            // Get registered modules from EngineApps\Registries\Modules\Main.json
            _engine.GameRegistry.RefreshModules();
            IReadOnlyDictionary<string, object?> modules = _engine.GameRegistry.GetRegisteredModules();

            // Get already downloaded games
            Dictionary<string, Core.Utils.GameModuleInfo> downloadedGames = _engine.Modules(Core.Utils.ModuleFilter.Installed);

            foreach (KeyValuePair<string, object?> kv in modules) {
                string moduleName = kv.Key;

                // Apply search filter if provided
                if (!string.IsNullOrWhiteSpace(query) &&
                    !moduleName.Contains(query, System.StringComparison.OrdinalIgnoreCase)) {
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
                if (isDownloaded && downloadedGames.TryGetValue(moduleName, out Core.Utils.GameModuleInfo? gameInfo)) {
                    isInstalled = gameInfo.IsInstalled;
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
        } catch (System.Exception ex) {
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
    private async System.Threading.Tasks.Task DownloadAsync(StoreItem? item) {
        if (item is null || string.IsNullOrWhiteSpace(item.Url)) {
            return;
        }

        try {
            if (_engine == null) {
                return;
            }

            Status = $"Downloading {item.Name}…";

            OperationOutputService.Instance.AddOutput($"Starting download for {item.Name}…", "stdout");

            bool success = await GUI.Utils.ExecuteEngineOperationAsync(
                _engine,
                item.Name,
                $"Download {item.Name}",
                async (onOutput, onEvent, stdin) => {
                    onEvent(new Dictionary<string, object?> {
                        ["event"] = "start",
                        ["name"] = item.Name,
                        ["url"] = item.Url ?? string.Empty
                    });

                    bool result = await Task.Run(() => _engine.DownloadModule(item.Url!));

                    onOutput(result ? $"Download complete for {item.Name}." : $"Download failed for {item.Name}.", result ? "stdout" : "stderr");
                    onEvent(new Dictionary<string, object?> {
                        ["event"] = "end",
                        ["success"] = result,
                        ["name"] = item.Name
                    });

                    return result;
                }
            );

            OperationOutputService.Instance.AddOutput(
                success ? $"Finished downloading {item.Name}." : $"Unable to download {item.Name}.",
                success ? "stdout" : "stderr"
            );
            if (success) {
                Status = $"Downloaded {item.Name} successfully.";
                await LoadAsync(Query);
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
            Window? w = TopLevel.GetTopLevel(this) as Window;
            if (w is MainWindow mw && _engine is not null) {
                mw.ShowLibraryFor(item.Name);
            }
        } catch { /* ignore */ }
        await System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>
    /// Represents a store item (module from registry).
    /// </summary>
    internal sealed class StoreItem {
        internal string Id {
            get; set;
        } = "";
        internal string Name {
            get; set;
        } = "";
        internal string Title {
            get; set;
        } = "";
        internal string Description {
            get; set;
        } = "";
        internal string? Url {
            get; set;
        }
        internal bool IsDownloaded {
            get; set;
        }
        internal bool IsInstalled {
            get; set;
        }
        internal bool CanDownload {
            get; set;
        }
        internal bool CanInstall {
            get; set;
        }
    }

    private event PropertyChangedEventHandler? _propertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged {
        add => _propertyChanged += value;
        remove => _propertyChanged -= value;
    }

    private void Raise(string name) => _propertyChanged?.Invoke(this, e: new PropertyChangedEventArgs(name));

    private sealed class Cmd(System.Func<object?, Task> run):System.Windows.Input.ICommand {
        private readonly Func<object?, Task> _run = run;
        public bool CanExecute(object? p) => true;
        public async void Execute(object? p) => await _run(p);
        public event EventHandler? CanExecuteChanged;
    }
}
