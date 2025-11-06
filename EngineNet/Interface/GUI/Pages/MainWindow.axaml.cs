using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EngineNet.Interface.GUI.Pages;

public partial class MainWindow:Window {
    /* :: :: Vars :: START :: */
    private readonly Core.Engine? _engine;

    /* :: :: Vars :: END :: */
    // //
    /* :: :: Constructors :: START :: */
    /// <summary>
    /// previewer constructor
    /// </summary>
    public MainWindow() {
        InitializeComponent();
        ShowLibrary(); // default page
    }

    /// <summary>
    /// Main constructor
    /// </summary>
    internal MainWindow(Core.Engine engine) {
        _engine = engine;
        DataContext = this;
        InitializeComponent();
        TryWireBottomPanel();
        ShowLibrary(); // default page
    }

    private void ShowLibrary() {
        if (_engine is null) {
            ContentHost.Content = new Pages.LibraryPage();
            return;
        }
        ContentHost.Content = new Pages.LibraryPage(_engine);
    }

    private void ShowStore() {
        if (_engine is null) {

            return;
        }
        ContentHost.Content = new Pages.StorePage(_engine);
    }

    internal void ShowModule(string moduleName) {
        if (_engine is null) {
            return;
        }
        ContentHost.Content = new Pages.ModulePage(_engine, moduleName);
    }

    internal void ShowLibraryFor(string moduleName) {
        if (_engine is null) {
            return;
        }
        Pages.LibraryPage page = new Pages.LibraryPage(_engine);
        ContentHost.Content = page;
        page.ShowDetailsPublic(moduleName);
    }

    private void ShowBuilding() {
        if (_engine is null) {
            ContentHost.Content = new Pages.BuildingPage();
            return;
        }
        ContentHost.Content = new Pages.BuildingPage(_engine);
    }

    private void ShowSettings() {
        if (_engine is null) {
            ContentHost.Content = new Pages.SettingsPage();
            return;
        }
        ContentHost.Content = new Pages.SettingsPage(_engine);
    }

    private void TryWireBottomPanel() {
        try {
            var bottom = this.FindControl<Border>("BottomPanel");
            if (bottom != null) {
                bottom.DataContext = OperationOutputService.Instance;
            }
        } catch {
#if DEBUG
System.Diagnostics.Trace.WriteLine("Failed to wire bottom panel");
#endif
}
    }

    // navbar button handlers
    private void OnLibrary(object? s, RoutedEventArgs e) => ShowLibrary();
    private void OnStore(object? s, RoutedEventArgs e) => ShowStore();
    private void OnBuilding(object? s, RoutedEventArgs e) => ShowBuilding();
    private void OnSettings(object? s, RoutedEventArgs e) => ShowSettings();
}
