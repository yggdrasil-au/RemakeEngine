
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Interactivity;


namespace EngineNet.Interface.GUI.Pages.PromptWindows;

public partial class TextPromptWindow:Window {
    public string? Result {
        get; private set;
    }

    private readonly TextBox? _textInput;

    // Parameterless constructor for XAML loader
    public TextPromptWindow() {
        DataContext = this;
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
                _textInput.PasswordChar = '●';
                _textInput.RevealPassword = false;
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

    public System.Threading.Tasks.Task<string?> ShowAsync(Window owner) {
        return ShowDialog<string?>(owner);
    }
}