
namespace EngineNet.Interface.GUI;

public static class AvaloniaGui {
    internal static object? Engine {
        get; private set;
    }

    public static int Run(object engine) {
        try {
            Engine = engine;
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(Array.Empty<string>());
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(value: $"GUI error: {ex.Message}");
            return 1;
        } finally {
            Engine = null;
        }
    }

    public static AppBuilder BuildAvaloniaApp() {
        return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
    }
}

