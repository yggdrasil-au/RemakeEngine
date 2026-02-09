using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EngineNet.Interface.GUI.Pages;

public partial class MainWindow:Window {

    // //
    /* :: :: Constructors :: START :: */

    /// <summary>
    /// Main constructor
    /// </summary>
    public MainWindow() {
        DataContext = this;
        InitializeComponent();
        TryWireBottomPanel();
        TryWirePromptOverlay();
        ShowLibrary(); // default page
    }

    private void ShowLibrary() {
        ContentHost.Content = new Pages.LibraryPage();
    }

    private void ShowStore() {
        ContentHost.Content = new Pages.StorePage();
    }

    internal void ShowModule(string moduleName) {
        ContentHost.Content = new Pages.ModulePage(moduleName);
    }

    internal void ShowLibraryFor(string moduleName) {
        Pages.LibraryPage page = new Pages.LibraryPage();
        ContentHost.Content = page;
        page.ShowDetailsPublic(moduleName);
    }

    private void ShowBuilding() {
        if (AvaloniaGui.Engine is null) {
            ContentHost.Content = new Pages.BuildingPage();
            return;
        }
        ContentHost.Content = new Pages.BuildingPage();
    }

    private void ShowSettings() {
        if (AvaloniaGui.Engine is null) {
            ContentHost.Content = new Pages.SettingsPage();
            return;
        }
        ContentHost.Content = new Pages.SettingsPage();
    }

    private void TryWireBottomPanel() {
        try {
            var bottom = this.FindControl<Border>("BottomPanel");
            if (bottom != null) {
                bottom.DataContext = OperationOutputService.Instance;
            }
        } catch {
            Core.Diagnostics.Bug("GUI :: MainWindow.axaml.cs::TryWireBottomPanel() Failed to wire bottom panel");
        }
    }

    private void TryWirePromptOverlay() {
        try {
            var overlay = this.FindControl<Border>("PromptOverlay");
            if (overlay != null) {
                overlay.DataContext = OperationOutputService.Instance;
            }
        } catch {
            Core.Diagnostics.Bug("GUI :: MainWindow.axaml.cs::TryWirePromptOverlay() Failed to wire prompt overlay");
        }
    }

    // navbar button handlers
    private void OnLibrary(object? s, RoutedEventArgs e) => ShowLibrary();
    private void OnStore(object? s, RoutedEventArgs e) => ShowStore();
    private void OnBuilding(object? s, RoutedEventArgs e) => ShowBuilding();
    private void OnSettings(object? s, RoutedEventArgs e) => ShowSettings();

    // prompt handlers
    private void OnPromptSubmit(object? s, RoutedEventArgs e) => OperationOutputService.Instance.SubmitPrompt();
    private void OnPromptCancel(object? s, RoutedEventArgs e) => OperationOutputService.Instance.CancelPrompt();
}
