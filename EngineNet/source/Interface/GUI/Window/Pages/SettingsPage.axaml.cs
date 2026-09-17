
namespace EngineNet.Interface.GUI.Pages;

public sealed partial class SettingsPage:UserControl, INotifyPropertyChanged {
    //
    /** :: :: Vars :: START :: **/
    public string ProjectRoot { get; set; } = EngineNet.Shared.State.RootPath;
    public string Status { get; set; } = String.Empty;

    //** :: :: Vars :: END :: **/
    //
    //** :: :: Constructors :: START :: **/

    public SettingsPage() {
        InitializeComponent();
        DataContext = this;
    }

    //** :: :: Constructors :: END :: **/
    //
    //** :: :: Methods :: START :: **/

    private event PropertyChangedEventHandler? _propertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged {
        add => _propertyChanged += value;
        remove => _propertyChanged -= value;
    }

    // ReSharper disable once UnusedMember.Local
#pragma warning disable IDE0051
    private void Raise(string name) => _propertyChanged?.Invoke(sender: this, e: new PropertyChangedEventArgs(propertyName: name));
#pragma warning restore IDE0051

    private sealed class Cmd:System.Windows.Input.ICommand {
        private readonly Func<object?, Task> _run;
        public Cmd(System.Func<object?, Task> run) => _run = run;
        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter) => await _run(arg: parameter);
        public event EventHandler? CanExecuteChanged {
            add { }
            remove { }
        }
    }

    //** :: :: Methods :: END :: **/

}
