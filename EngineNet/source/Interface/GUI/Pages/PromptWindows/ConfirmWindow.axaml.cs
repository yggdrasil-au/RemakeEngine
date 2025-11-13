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
}
