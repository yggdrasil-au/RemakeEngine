
namespace EngineNet.Interface.GUI.Pages;

internal sealed partial class MainWindow:Window {

    // //
    /* :: :: Constructors :: START :: */

    /// <summary>
    /// Main constructor
    /// </summary>
    internal MainWindow() {
        DataContext = OperationOutputService.Instance;
        InitializeComponent();
        ShowLibrary(); // default page
    }

    private void ShowLibrary() {
        ContentHost.Content = new Pages.LibraryPage();
    }

    private void ShowStore() {
        ContentHost.Content = new Pages.StorePage();
    }

    internal void ShowModule(string moduleName) {
        ContentHost.Content = new Pages.ModulePage(moduleName: moduleName);
    }

    internal void ShowLibraryFor(string moduleName) {
        Pages.LibraryPage page = new();
        ContentHost.Content = page;
        page.ShowDetailsPublic(moduleName: moduleName);
    }

    private void ShowBuilding() {
        ContentHost.Content = new Pages.BuildingPage();
    }

    private void ShowSettings() {
        ContentHost.Content = new Pages.SettingsPage();
    }

    // navbar button handlers
    private void OnLibrary(object? s, RoutedEventArgs e) => ShowLibrary();
    private void OnStore(object? s, RoutedEventArgs e) => ShowStore();
    private void OnBuilding(object? s, RoutedEventArgs e) => ShowBuilding();
    private void OnSettings(object? s, RoutedEventArgs e) => ShowSettings();

    // prompt handlers
    private void OnPromptSubmit(object? s, RoutedEventArgs e) => OperationOutputService.Instance.SubmitPrompt();
    private void OnPromptYes(object? s, RoutedEventArgs e) => OperationOutputService.Instance.SubmitPrompt();
    private void OnPromptNo(object? s, RoutedEventArgs e) => OperationOutputService.Instance.SubmitNoPrompt();
    private void OnPromptCancel(object? s, RoutedEventArgs e) => OperationOutputService.Instance.CancelPrompt();
}
