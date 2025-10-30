
using System;
using Avalonia.Controls;

namespace EngineNet.Interface.GUI.Pages;

internal partial class HomePage:UserControl {
    private readonly Core.OperationsEngine? _engine;

    internal string Greeting { get; private set; } = "Welcome!";
    internal System.Windows.Input.ICommand RefreshCommand {
        get;
    }
    internal HomePage() {
        InitializeComponent();
        DataContext = this;

        Greeting = "Welcome! (Design Mode)";
        // Initialize command to avoid binding errors
        RefreshCommand = new RelayCommand(_ => { });

    }
    internal HomePage(Core.OperationsEngine engine) {
        _engine = engine;
        InitializeComponent();

        // page owns its binding context
        DataContext = this;

        RefreshCommand = new RelayCommand(_ => {
            try {
                // pull something from engine if you like
                Greeting = $"Hello @ {DateTime.Now:T}";
            } catch {
                Greeting = "Hello";
            }
            Raise(nameof(Greeting));
        });
    }

    private event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    private sealed class RelayCommand:System.Windows.Input.ICommand {
        private readonly Action<object?> _a; internal RelayCommand(Action<object?> a) => _a = a;
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) => _a(p);
        public event EventHandler? CanExecuteChanged;
    }
}
