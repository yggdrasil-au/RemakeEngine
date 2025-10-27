
namespace EngineNet.Interface.GUI.Views.Pages;

public partial class HomePage:UserControl {
    private readonly Core.OperationsEngine? _engine;

    public string Greeting { get; private set; } = "Welcome!";
    public ICommand RefreshCommand {
        get;
    }
    public HomePage() {
        InitializeComponent();
        DataContext = this;

        Greeting = "Welcome! (Design Mode)";
        // Initialize command to avoid binding errors
        RefreshCommand = new RelayCommand(_ => { });

    }
    public HomePage(Core.OperationsEngine engine) {
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

    private sealed class RelayCommand:ICommand {
        private readonly Action<object?> _a; public RelayCommand(Action<object?> a) => _a = a;
        public bool CanExecute(object? p) => true; public void Execute(object? p) => _a(p);
        public event EventHandler? CanExecuteChanged;
    }
}
