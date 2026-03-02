using Avalonia.Controls;

namespace EngineNet.Interface.GUI.Pages.PromptWindows;

internal partial class ConfirmWindow:Window {
    internal bool Result {
        get; private set;
    }

    // Parameterless constructor for XAML loader
    internal ConfirmWindow() {
        InitializeComponent();
    }

    internal ConfirmWindow(string title, string question) {
        InitializeComponent();
        Title = title;
        this.FindControl<TextBlock>(name: "Question")!.Text = question;
    }

    internal void EnableOptOut() {
        this.FindControl<CheckBox>("DontAskAgainCheck")!.IsVisible = true;
    }

    private void OnYes(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
        Result = true;
        Close(Result);
    }
    private void OnNo(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
        Result = false;
        Close(Result);
    }

    internal System.Threading.Tasks.Task<bool> ShowAsync(Window owner) {
        return ShowDialog<bool>(owner);
    }

    internal async System.Threading.Tasks.Task<(bool Result, bool DontAskAgain)> ShowWithOptOutAsync(Window owner) {
        bool result = await ShowDialog<bool>(owner);
        bool dontAskAgain = this.FindControl<CheckBox>("DontAskAgainCheck")?.IsChecked ?? false;
        return (result, dontAskAgain);
    }
}
