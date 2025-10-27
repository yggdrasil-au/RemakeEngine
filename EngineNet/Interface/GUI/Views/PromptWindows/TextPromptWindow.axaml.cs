using Avalonia.Interactivity;

namespace EngineNet.Interface.GUI.Avalonia.Views.PromptWindows;

public partial class TextPromptWindow:Window {
    public string? Result {
        get; private set;
    }

    private readonly TextBox? _textInput;

    // Parameterless constructor for XAML loader
    public TextPromptWindow() {
        InitializeComponent();
    }

    public TextPromptWindow(string title, string message, string? defaultValue, bool secret) {
        InitializeComponent();
        Title = title;

        _textInput = this.FindControl<TextBox>("Input");
        var messageBlock = this.FindControl<TextBlock>("PromptMessage");

        if (messageBlock is not null)
            messageBlock.Text = message;

        if (_textInput is not null) {
            // When secret, mask the characters
            if (secret) {
                _textInput.PasswordChar = '•';   // or '*'
                _textInput.RevealPassword = false; // optional
            }

            _textInput.Text = defaultValue ?? string.Empty;
            _textInput.CaretIndex = _textInput.Text?.Length ?? 0;
            _textInput.Focus();
        }
    }

    private void OnOk(object? sender, RoutedEventArgs e) {
        Result = _textInput?.Text;
        Close(Result);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) {
        Result = null;
        Close(Result);
    }

    public Task<string?> ShowAsync(Window owner) {
        return ShowDialog<string?>(owner);
    }
}