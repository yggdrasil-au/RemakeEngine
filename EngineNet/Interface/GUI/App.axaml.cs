using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace EngineNet.Interface.GUI;

public partial class App:Application {
    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            desktop.MainWindow = new Views.MainWindow(AvaloniaGui.Engine!) {
                DataContext = new ViewModels.MainViewModel(AvaloniaGui.Engine)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

