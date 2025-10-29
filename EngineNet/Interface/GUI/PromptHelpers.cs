
using Avalonia.Controls;
using Avalonia.Interactivity;


namespace EngineNet.Interface.GUI;

public static class PromptHelpers {
    private static Window? TryGetMainWindow() {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime l) {
            return l.MainWindow;
        }

        return null;
    }

    public static async System.Threading.Tasks.Task<string?> TextAsync(string title, string message, string? defaultValue = null, bool secret = false) {
        Window? window = TryGetMainWindow();
        if (window is null) return null;
        Pages.PromptWindows.TextPromptWindow? dlg = new Pages.PromptWindows.TextPromptWindow(title, message, defaultValue, secret);
        return await dlg.ShowAsync(window);
    }

}
