using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RemakeEngine.Interface.GUI.Avalonia.Views.PromptWindows;

public partial class ConfirmWindow : Window
{
    public bool Result { get; private set; }

    // Parameterless constructor for XAML loader
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    public ConfirmWindow(string title, string question)
    {
        InitializeComponent();
        Title = title;
        this.FindControl<TextBlock>("Question")!.Text = question;
    }

    private void OnYes(object? sender, RoutedEventArgs e) { Result = true; Close(Result); }
    private void OnNo(object? sender, RoutedEventArgs e) { Result = false; Close(Result); }

    public Task<bool> ShowAsync(Window owner)
    {
        return ShowDialog<bool>(owner);
    }
}
