using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia.Controls;

namespace EngineNet.Interface.GUI.Pages;

public partial class BuildingPage : UserControl {
    public OperationOutputService Service => OperationOutputService.Instance;

    public Cmd ClearOutputCommand { get; }

    public BuildingPage() : this(null) { }

    internal BuildingPage(Core.OperationsEngine? engine) {
        ClearOutputCommand = new Cmd(async _ => await Service.ClearAsync());

        InitializeComponent();
        DataContext = this;

        if (Avalonia.Controls.Design.IsDesignMode) {
            SeedDesignData();
        }
    }

    private void SeedDesignData() {
        OperationOutputService svc = Service;
        if (svc.Lines.Count == 0) {
            svc.StartOperation("Build Assets", "Sample Game");
            svc.AddOutput("Preparing workspace...");
            svc.AddOutput("Downloading dependencies...");
        }

        Dictionary<string, object?> progressStart = new Dictionary<string, object?> {
            ["event"] = "progress_panel_start",
            ["reserve"] = 6
        };
        svc.HandleEvent(progressStart);

        Dictionary<string, object?> progressPayload = new Dictionary<string, object?> {
            ["event"] = "progress_panel",
            ["label"] = "Packaging",
            ["spinner"] = "/",
            ["active_total"] = 2,
            ["stats"] = new Dictionary<string, object?> {
                ["total"] = 10,
                ["processed"] = 4,
                ["ok"] = 3,
                ["skip"] = 0,
                ["err"] = 1,
                ["percent"] = 0.4
            },
            ["active_jobs"] = new List<Dictionary<string, object?>> {
                new Dictionary<string, object?> {
                    ["tool"] = "ffmpeg",
                    ["file"] = "intro_cutscene.mp4",
                    ["elapsed"] = "00:12"
                },
                new Dictionary<string, object?> {
                    ["tool"] = "texturec",
                    ["file"] = "characters/player/body_diffuse.png",
                    ["elapsed"] = "00:03"
                }
            }
        };
        svc.HandleEvent(progressPayload);
    }

    public sealed class Cmd : System.Windows.Input.ICommand {
        private readonly System.Func<object?, Task> _run;

        public Cmd(System.Func<object?, Task> run) {
            _run = run;
        }

        public event System.EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public async void Execute(object? parameter) => await _run(parameter);
    }
}