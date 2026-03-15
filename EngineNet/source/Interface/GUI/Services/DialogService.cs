using Avalonia.Controls;

namespace EngineNet.Interface.GUI.Services;

public static class DialogService {
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

    public static async System.Threading.Tasks.Task<bool> ConfirmAsync(string title, string message, bool defaultValue = false) {
        Window? window = TryGetMainWindow();
        if (window is null) return defaultValue;
        Pages.PromptWindows.ConfirmWindow? dlg = new Pages.PromptWindows.ConfirmWindow(title, message);
        bool result = await dlg.ShowAsync(window);
        return result;
    }

    public static async System.Threading.Tasks.Task<(bool Result, bool DontAskAgain)> ConfirmWithOptOutAsync(string title, string message, bool defaultValue = false) {
        Window? window = TryGetMainWindow();
        if (window is null) return (defaultValue, false);
        Pages.PromptWindows.ConfirmWindow dlg = new Pages.PromptWindows.ConfirmWindow(title, message);
        dlg.EnableOptOut();
        return await dlg.ShowWithOptOutAsync(window);
    }
}
