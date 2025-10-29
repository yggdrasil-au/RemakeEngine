
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Interactivity;

using Avalonia.Interactivity;

namespace EngineNet.Interface.GUI.Pages;

public partial class MainWindow:Window {
    /* :: :: Vars :: START :: */
    private readonly Core.OperationsEngine? _engine;

    /* :: :: Vars :: END :: */
    // //
    /* :: :: Constructors :: START :: */
    /// <summary>
    /// previewer constructor
    /// </summary>
    public MainWindow() {
        InitializeComponent();
        //ShowHome();
    }

    /// <summary>
    /// Main constructor
    /// </summary>
    internal MainWindow(Core.OperationsEngine engine) {
        _engine = engine;
        DataContext = this;
        InitializeComponent();
        //ShowHome(); // default page
    }

    private void ShowHome() {
        if (_engine is null) {
            ContentHost.Content = new Pages.HomePage();
            return;
        }
        ContentHost.Content = new Pages.HomePage(_engine);
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

    // navbar button handlers
    private void OnHome(object? s, RoutedEventArgs e) => ShowHome();
    private void OnLibrary(object? s, RoutedEventArgs e) => ShowLibrary();
    private void OnStore(object? s, RoutedEventArgs e) => ShowStore();
    private void OnBuilding(object? s, RoutedEventArgs e) => ShowBuilding();
    private void OnSettings(object? s, RoutedEventArgs e) => ShowSettings();
}
