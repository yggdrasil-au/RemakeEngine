
using System;
using System.Threading.Tasks;
using System.ComponentModel;

using Avalonia.Controls;

namespace EngineNet.Interface.GUI.Pages;

public partial class SettingsPage:UserControl, INotifyPropertyChanged {
    //
    /** :: :: Vars :: START :: **/
    private readonly Core.Engine? _engine;
    private string ProjectRoot { get; set; } = String.Empty;
    private string Status { get; set; } = String.Empty;

    /** :: :: Vars :: END :: **/
    //
    /** :: :: Constructors :: START :: **/

    // preview only constructor
    public SettingsPage() {
        // set preview values
        ProjectRoot = @"A:\RemakeEngine\";
        Status = "Preview";

        // init axaml
        InitializeComponent();
        // set data context to this
        DataContext = this;
    }

    internal SettingsPage(Core.Engine engine) {
        _engine = engine;
        ProjectRoot = _engine.rootPath;

        InitializeComponent();
        DataContext = this;
    }

    /** :: :: Constructors :: END :: **/
    //
    // ** :: :: Methods :: START :: **/

    internal event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class Cmd:System.Windows.Input.ICommand {
        private readonly Func<object?, Task> _run;
        public Cmd(System.Func<object?, Task> run) => _run = run;
        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter) => await _run(parameter);
        public event EventHandler? CanExecuteChanged;
    }

    /** :: :: Methods :: END :: **/

}
