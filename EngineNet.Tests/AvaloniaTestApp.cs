using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(EngineNet.Tests.AvaloniaTestApp))]

namespace EngineNet.Tests;

public sealed class AvaloniaTestApp : Application
{
    public override void Initialize()
    {
        // No XAML to load for tests.
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
