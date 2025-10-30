using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia;
using Avalonia.Threading;

namespace EngineNet.Interface.GUI.Pages;

internal partial class BuildingPage:UserControl {
    internal OperationOutputService Service => OperationOutputService.Instance;

    internal Cmd Button_ClearOutput_Click {
        get;
    }

    internal BuildingPage() : this(null) { }

    internal BuildingPage(Core.Engine? engine) {
        Button_ClearOutput_Click = new Cmd(async _ => await Service.ClearAsync());

        InitializeComponent();
        DataContext = this;

        TryWireAutoScroll();

        if (Avalonia.Controls.Design.IsDesignMode) {
            SeedDesignData();
        }
    }

    private ScrollViewer? _outputScroll;
    private bool _autoScrollEnabled = true;

    private void TryWireAutoScroll() {
        try {
            _outputScroll = this.FindControl<ScrollViewer>(name: "OutputScroll");
            if (_outputScroll != null) {
                // Track user-initiated scroll to disable auto-scroll when scrolled up significantly
                _outputScroll.ScrollChanged += (_, __) => UpdateAutoScrollFlag();

                // When new lines are added, attempt to keep the view pinned to bottom if user hasn't scrolled up
                Service.Lines.CollectionChanged += (_, __) => {
                    if (_autoScrollEnabled) {
                        Dispatcher.UIThread.Post(ScrollToEndSafe, DispatcherPriority.Background);
                    }
                };
            }
        } catch { /* ignore; non-critical */ }
    }

    private void UpdateAutoScrollFlag() {
        if (_outputScroll is null) return;
        try {
            // Consider within 24px of bottom as "at bottom"
            double bottomThreshold = 24.0;
            var extent = _outputScroll.Extent;
            var viewport = _outputScroll.Viewport;
            var offset = _outputScroll.Offset;
            double remaining = (extent.Height - viewport.Height) - offset.Y;
            _autoScrollEnabled = remaining <= bottomThreshold;
        } catch { _autoScrollEnabled = true; }
    }

    private void ScrollToEndSafe() {
        if (_outputScroll is null) return;
        try {
            var extent = _outputScroll.Extent;
            var offset = _outputScroll.Offset;
            // Set Y to max extent to pin bottom; X unchanged
            _outputScroll.Offset = new Avalonia.Vector(x: offset.X, y: extent.Height);
        } catch {
            // Fallback: no-op
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

    internal sealed class Cmd:System.Windows.Input.ICommand {
        private readonly System.Func<object?, Task> _run;

        public Cmd(System.Func<object?, Task> run) {
            _run = run;
        }

        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter) => await _run(parameter);
        public event System.EventHandler? CanExecuteChanged;
    }
}
