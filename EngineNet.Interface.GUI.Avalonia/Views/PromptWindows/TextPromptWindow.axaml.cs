using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RemakeEngine.Interface.GUI.Avalonia.Views.PromptWindows;

public partial class TextPromptWindow : Window
{
    public string? Result { get; private set; }

    // Parameterless constructor for XAML loader
    public TextPromptWindow()
    {
        InitializeComponent();
    }

    public TextPromptWindow(string title)
    {
        InitializeComponent();
        Title = title;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Result = this.FindControl<TextBox>("Input")!.Text;
        Close(Result);
    }

    public Task<string?> ShowAsync(Window owner)
    {
        return ShowDialog<string?>(owner);
    }
}
