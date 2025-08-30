using System;
using Avalonia;

namespace RemakeEngine.Interface.GUI.Avalonia;

public static class AvaloniaGui
{
    internal static object? Engine { get; private set; }

    public static int Run(object engine)
    {
        try
        {
            Engine = engine;
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(Array.Empty<string>());
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"GUI error: {ex.Message}");
            return 1;
        }
        finally
        {
            Engine = null;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

