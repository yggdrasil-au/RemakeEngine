
using System;
using System.Threading.Tasks;
using System.ComponentModel;

using Avalonia.Controls;

namespace EngineNet.Interface.GUI.Pages;

public partial class SettingsPage:UserControl, INotifyPropertyChanged {
    //
    /** :: :: Vars :: START :: **/
    public string ProjectRoot { get; set; } = Program.rootPath;
    public string Status { get; set; } = String.Empty;

    /** :: :: Vars :: END :: **/
    //
    /** :: :: Constructors :: START :: **/

    public SettingsPage() {
        InitializeComponent();
        DataContext = this;
    }

    /** :: :: Constructors :: END :: **/
    //
    // ** :: :: Methods :: START :: **/

    private event PropertyChangedEventHandler? _propertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged {
        add => _propertyChanged += value;
        remove => _propertyChanged -= value;
    }

    private void Raise(string name) => _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class Cmd:System.Windows.Input.ICommand {
        private readonly Func<object?, Task> _run;
        public Cmd(System.Func<object?, Task> run) => _run = run;
        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter) => await _run(parameter);
        public event EventHandler? CanExecuteChanged;
    }

    /** :: :: Methods :: END :: **/

}
