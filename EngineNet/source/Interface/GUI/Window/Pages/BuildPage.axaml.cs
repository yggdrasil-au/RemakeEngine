using Avalonia.Threading;
using Avalonia.VisualTree;
using EngineNet.Shared.IO.UI;

namespace EngineNet.GUI.Pages;

public sealed partial class BuildingPage:UserControl {
    public OperationOutputService Service => OperationOutputService.Instance;

    public Cmd Button_ClearOutput_Click {
        get;
    }

    public BuildingPage() {
        Button_ClearOutput_Click = new Cmd(run: async _ => await Service.ClearAsync());

        InitializeComponent();
        DataContext = this;

        TryWireAutoScroll();

        if (Avalonia.Controls.Design.IsDesignMode) {
            SeedDesignData();
        }
    }

    private TextBox? _outputTextBox;
    private ScrollViewer? _outputScroll;
    private bool _autoScrollEnabled = true;

    private void TryWireAutoScroll() {
        try {
            _outputTextBox = this.FindControl<TextBox>(name: "OutputTextBox");
            if (_outputTextBox != null) {
                _outputTextBox.AttachedToVisualTree += (_, _) => WireScrollViewer();

                // When new lines are added or updated, keep the view pinned to bottom if the user hasn't scrolled up
                Service.Lines.CollectionChanged += (_, _) => TryAutoScroll();
                Service.PropertyChanged += (_, args) => {
                    if (args.PropertyName == nameof(OperationOutputService.FullLogText)) {
                        TryAutoScroll();
                    }
                };
            }
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"TryWireAutoScroll catch triggered: {ex}");
            /* ignore; non-critical */
        }
    }

    private void WireScrollViewer() {
        if (_outputTextBox is null) return;
        _outputScroll = _outputTextBox.FindDescendantOfType<ScrollViewer>();
        if (_outputScroll != null) {
            // Track user-initiated scroll to disable auto-scroll when scrolled up significantly
            _outputScroll.ScrollChanged += (_, _) => UpdateAutoScrollFlag();
            UpdateAutoScrollFlag();
        }
    }

    private void TryAutoScroll() {
        if (!_autoScrollEnabled) return;
        Dispatcher.UIThread.Post(action: ScrollToEndSafe, priority: DispatcherPriority.Background);
    }

    private void UpdateAutoScrollFlag() {
        if (_outputScroll is null) return;
        try {
            // Consider within 24px of bottom as "at bottom"
            double bottomThreshold = 24.0;
            Size extent = _outputScroll.Extent;
            Size viewport = _outputScroll.Viewport;
            Vector offset = _outputScroll.Offset;
            double remaining = (extent.Height - viewport.Height) - offset.Y;
            _autoScrollEnabled = remaining <= bottomThreshold;
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"UpdateAutoScrollFlag catch triggered: {ex}");
            _autoScrollEnabled = true;
        }
    }

    private void ScrollToEndSafe() {
        if (_outputScroll is null) return;
        try {
            Size extent = _outputScroll.Extent;
            Vector offset = _outputScroll.Offset;
            // Set Y to max extent to pin bottom; X unchanged
            _outputScroll.Offset = new Avalonia.Vector(x: offset.X, y: extent.Height);
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"ScrollToEndSafe catch triggered: {ex}");
            // Fallback: no-op
        }
    }

    private void SeedDesignData() {
        OperationOutputService svc = Service;
        if (svc.Lines.Count == 0) {
            svc.StartOperation(operationName: "Build Assets", gameName: "Sample Game");
            svc.AddOutput(text: "Preparing workspace...");
            svc.AddOutput(text: "Downloading dependencies...");
        }

        Dictionary<string, object?> progressStart = new Dictionary<string, object?> {
            [key: "event"] = EngineSdk.Events.ProgressPanelStart,
            [key: "reserve"] = 6
        };
        svc.HandleEvent(evt: progressStart);

        Dictionary<string, object?> progressPayload = new Dictionary<string, object?> {
            [key: "event"] = EngineSdk.Events.ProgressPanel,
            [key: "label"] = "Packaging",
            [key: "spinner"] = "/",
            [key: "active_total"] = 2,
            [key: "stats"] = new Dictionary<string, object?> {
                [key: "total"] = 10,
                [key: "processed"] = 4,
                [key: "ok"] = 3,
                [key: "skip"] = 0,
                [key: "err"] = 1,
                [key: "percent"] = 0.4
            },
            [key: "active_jobs"] = new List<Dictionary<string, object?>> {
                new Dictionary<string, object?> {
                    [key: "tool"] = "ffmpeg",
                    [key: "file"] = "intro_cutscene.mp4",
                    [key: "elapsed"] = "00:12"
                },
                new Dictionary<string, object?> {
                    [key: "tool"] = "texturec",
                    [key: "file"] = "characters/player/body_diffuse.png",
                    [key: "elapsed"] = "00:03"
                }
            }
        };
        svc.HandleEvent(evt: progressPayload);
    }

    /// <summary>
    /// Command wrapper for button actions
    /// </summary>
    public sealed class Cmd:System.Windows.Input.ICommand {
        // Constructor parameter
        private readonly System.Func<object?, Task> _run;

        // Constructor
        public Cmd(System.Func<object?, Task> run) {
            _run = run;
        }

        // Unused, but required by interface
        public bool CanExecute(object? parameter) => true;
        // Unused, but required by interface
        public async void Execute(object? parameter) => await _run(arg: parameter);
        // Unused, but required by interface
        public event System.EventHandler? CanExecuteChanged {
            add { }
            remove { }
        }
    }
}
