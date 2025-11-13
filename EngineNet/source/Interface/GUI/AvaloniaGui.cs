using Avalonia;


namespace EngineNet.Interface.GUI;

/// <summary>
/// Entry point and bootstrapper for the Avalonia desktop UI.
/// Persists the host application's engine object long enough to
/// construct view models and start the UI loop.
/// </summary>
internal static class AvaloniaGui {
    /// <summary>
    /// Engine instance provided by the host application at startup.
    /// Stored temporarily so the App (and its view models) can access it
    /// during initialization.
    /// </summary>
    internal static Core.Engine Engine {
        get; private set;
    } = null!; // Initialized in Run()

    /// <summary>
    /// Launches the Avalonia desktop application with the provided engine.
    /// </summary>
    /// <param name="engine">
    /// An application-specific engine/service root passed to the UI layer.
    /// Typically used to construct the MainViewModel and other services.
    /// </param>
    /// <returns>
    /// 0 on normal shutdown; 1 if an exception is caught during startup/run.
    /// </returns>
    internal static async System.Threading.Tasks.Task<int> RunAsync(Core.Engine engine) {
        try {
            // 1) Stash the engine so App.OnFrameworkInitializationCompleted (or similar)
            //    can pull it to compose view models.
            Engine = engine;

            // 2) Build the app and start the desktop lifetime.
            //    This call blocks until the window closes / lifetime ends.
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(System.Array.Empty<string>());

            // 3) If we reached here without exceptions, return success.
            return 0;
        } catch (System.Exception ex) {
            // If anything goes wrong during startup or run, print a concise error
            // and return a non-zero exit code to signal failure to the host.
            System.Console.Error.WriteLine(value: $"GUI error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Configures and returns the Avalonia <see cref="AppBuilder"/> used to start the app.
    /// </summary>
    /// <remarks>
    /// Uses platform detection for the current OS and routes Avalonia logs to trace.
    /// Customize here to add DI, theming, or platform-specific options.
    /// </remarks>
    internal static AppBuilder BuildAvaloniaApp() {
        // Configure the application type, detect platform backends, and enable tracing.
        return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
    }
}

