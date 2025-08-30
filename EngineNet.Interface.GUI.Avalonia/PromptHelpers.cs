using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;

namespace RemakeEngine.Interface.GUI.Avalonia;

internal static class PromptHelpers
{
    private static Window? TryGetMainWindow()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime l)
            return l.MainWindow;
        return null;
    }

    public static async Task<string?> TextAsync(string title)
    {
        var window = TryGetMainWindow();
        if (window is null) return null;
        var dlg = new Views.PromptWindows.TextPromptWindow(title);
        return await dlg.ShowAsync(window);
    }

    public static async Task<bool> ConfirmAsync(string question, string title)
    {
        var window = TryGetMainWindow();
        if (window is null) return false;
        var dlg = new Views.PromptWindows.ConfirmWindow(title, question);
        return await dlg.ShowAsync(window);
    }

    public static async Task InfoAsync(string message, string title)
    {
        var window = TryGetMainWindow();
        if (window is null) return;
        var dlg = new Window { Width = 400, Height = 160, Title = title, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        var text = new TextBlock { Text = message, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        var ok = new Button { Content = "OK", HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right, Width = 80, Margin = new global::Avalonia.Thickness(0,8,0,0) };
        ok.Click += (_, __) => dlg.Close();
        var panel = new DockPanel { Margin = new global::Avalonia.Thickness(8) };
        DockPanel.SetDock(ok, Dock.Bottom);
        panel.Children.Add(ok);
        panel.Children.Add(text);
        dlg.Content = panel;
        await dlg.ShowDialog(window);
    }

    public static async Task<string?> PickAsync(string title, System.Collections.Generic.IList<string> options)
    {
        var window = TryGetMainWindow();
        if (window is null) return null;
        var dlg = new Window { Width = 480, Height = 340, Title = title, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        var list = new ListBox { ItemsSource = options };
        list.SelectionMode = SelectionMode.Single;
        var ok = new Button { Content = "OK", HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right, Width = 80, Margin = new global::Avalonia.Thickness(0,8,0,0) };
        ok.Click += (_, __) => dlg.Close(list.SelectedItem as string);
        var panel = new DockPanel { Margin = new global::Avalonia.Thickness(8) };
        DockPanel.SetDock(ok, Dock.Bottom);
        panel.Children.Add(ok);
        panel.Children.Add(list);
        dlg.Content = panel;
        return await dlg.ShowDialog<string?>(window);
    }
}
