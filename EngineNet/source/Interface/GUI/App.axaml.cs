
namespace EngineNet.GUI;

public sealed class App:Avalonia.Application {

    public override void Initialize() {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(obj: this);
    }

    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop) {
            desktop.MainWindow = new Pages.MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

