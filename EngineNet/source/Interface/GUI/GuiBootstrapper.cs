
namespace EngineNet.Interface.GUI;

/// <summary>
/// Entry point and bootstrapper for the Avalonia desktop UI.
/// Persists the host application's engine object long enough to
/// construct view models and start the UI loop.
/// </summary>
public static class GuiBootstrapper {

    internal static MiniEngineFace MiniEngine { get; set; } = null!;

    /// <summary>
    /// Launches the Avalonia desktop application with the provided engine.
    /// </summary>
    /// <param name="miniEngine">
    /// A simplified interface to the engine, exposing only the methods needed by the UI.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the operation.
    /// </param>
    /// <returns>
    /// 0 on normal shutdown; 1 if an exception is caught during startup/run.
    /// </returns>
    internal static int Run(MiniEngineFace miniEngine, System.Threading.CancellationToken cancellationToken) {
        try {
            MiniEngine = miniEngine;

            Shared.IO.UI.EngineSdk.LocalEventSink = OperationOutputService.Instance.HandleEvent;

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(System.Array.Empty<string>());

            return 0;
        } catch (System.Exception ex) {
            // If anything goes wrong during startup or run, print a concise error
            // and return a non-zero exit code to signal failure to the host.
            Shared.IO.Diagnostics.Bug("GUI error", ex);
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
    public static AppBuilder BuildAvaloniaApp() {
        // Configure the application type, detect platform backends, and enable tracing.
        // takes the app.axaml.cs 'App' class as the application root.
        return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
    }
}
