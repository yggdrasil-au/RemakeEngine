
namespace EngineNet.Interface.GUI;

public partial class App:Application {
    public override void Initialize() {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop) {
            desktop.MainWindow = new Views.MainWindow(AvaloniaGui.Engine!) {
                DataContext = new ViewModels.MainViewModel(AvaloniaGui.Engine!)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

