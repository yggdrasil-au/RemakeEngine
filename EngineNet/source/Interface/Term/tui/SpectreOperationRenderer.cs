using EngineNet.Shared.IO.UI;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace EngineNet.Term;

/// <summary>
/// Renders operations purely using Spectre.Console with a single persistent Live display lifecycle.
/// Unifies the stable single-lifecycle architecture with rich task progress tracking and markup logging.
/// </summary>
internal sealed class SpectreOperationRenderer {
    private const int MaxLogLines = 10000;

    private readonly Lock _lock = new();
    private readonly List<string> _logLines = new();
    private readonly Dictionary<string, TaskProgressState> _taskProgress = new();
    private LiveDisplayContext? _liveContext;

    private int _opCurrent;
    private int _opTotal;
    private string _opName = string.Empty;
    private string? _promptMessage;

    private sealed class TaskProgressState {
        public string Label { get; set; } = string.Empty;
        public int Processed { get; set; }
        public int Total { get; set; }
        public int Ok { get; set; }
        public int Skip { get; set; }
        public int Err { get; set; }
        public double Percent { get; set; }
    }

    internal async Task<TResult> RunAsync<TResult>(
        string operationName,
        Func<Core.ProcessRunner.OutputHandler,
        Core.ProcessRunner.EventHandler,
        Core.ProcessRunner.StdinProvider,
        Task<TResult>> operation
    ) {
        lock (_lock) {
            _logLines.Clear();
            _taskProgress.Clear();
            _opCurrent = 0;
            _opTotal = 0;
            _opName = string.Empty;
            _logLines.Add($"Starting: {operationName}");
        }

        TResult? result = default;
        Exception? executionException = null;

        try {
            await AnsiConsole.Live(BuildLayout())
                .AutoClear(false)
                .Overflow(VerticalOverflow.Visible)
                .StartAsync(async context => {
                    _liveContext = context;
                    Refresh();
                    try {
                        result = await operation(HandleOutput, HandleEvent, ReadInput);
                    }
                    catch (Exception ex) {
                        executionException = ex;
                    }
                    finally {
                        _liveContext = null;
                    }
                });
        }
        catch (Exception ex) {
            executionException ??= ex;
        }

        if (executionException != null) {
            throw executionException;
        }

        return result!;
    }

    internal void Clear() {
        AnsiConsole.Clear();
    }

    private string ReadInput() {
        string prompt;
        lock (_lock) {
            prompt = _promptMessage ?? (_logLines.Count > 0 ? _logLines.Last() : "Input required");
        }

        // Clear the prompt message so the live display panel doesn't duplicate while AnsiConsole.Ask runs
        ClearPrompt();

        // Push cursor to a new line below the Live display so prompt input works cleanly
        System.Console.WriteLine();
        string response = AnsiConsole.Ask<string>($"[cyan]{Markup.Escape(prompt)}[/]");

        lock (_lock) {
            _logLines.Add($"[cyan]?[/] {prompt}: [grey]{response}[/]");
        }
        Refresh();

        return response;
    }

    internal void HandleOutput(string line, string stream) {
        string formatted = stream == "stderr" ? $"[red]{Markup.Escape(line)}[/]" : Markup.Escape(line);
        AddLog(formatted);
    }

    internal void HandleEvent(Dictionary<string, object?> evt) {
        if (!evt.TryGetValue("event", out object? typeValue) || typeValue == null) {
            return;
        }

        string type = typeValue.ToString() ?? string.Empty;
        switch (type) {
            case EngineSdk.Events.Print:
                AddLog(Markup.Escape(GetText(evt, "message")));
                break;
            case EngineSdk.Events.Warning:
                AddLog($"[yellow]WARNING: {Markup.Escape(GetText(evt, "message"))}[/]");
                break;
            case EngineSdk.Events.Error:
                AddLog($"[red]ERROR: {Markup.Escape(GetText(evt, "message"))}[/]");
                break;
            case EngineSdk.Events.Prompt:
            case EngineSdk.Events.ColorPrompt:
            case EngineSdk.Events.Confirm:
                SetPrompt(GetText(evt, "message"));
                break;
            case EngineSdk.Events.ScriptActiveStart:
                AddLog($"[green]▶ Starting script: {Markup.Escape(GetText(evt, "name"))}[/]");
                break;
            case EngineSdk.Events.ScriptActiveEnd:
                AddLog($"[green]◀ Finished script: {Markup.Escape(GetText(evt, "name"))}[/]");
                break;
            case EngineSdk.Events.RunAllStart:
                UpdateOperationProgress(0, GetInt(evt, "total"), string.Empty);
                break;
            case EngineSdk.Events.RunAllOpStart:
                UpdateOperationProgress(GetInt(evt, "index"), GetInt(evt, "total"), GetText(evt, "name"));
                break;
            case EngineSdk.Events.RunAllOpEnd:
                UpdateOperationProgress(GetInt(evt, "index") + 1, GetInt(evt, "total"), GetText(evt, "name"));
                break;
            case EngineSdk.Events.RunAllComplete:
                UpdateOperationProgress(GetInt(evt, "total"), GetInt(evt, "total"), string.Empty);
                break;
            case EngineSdk.Events.ProgressPanel:
                UpdateTaskProgress(evt);
                break;
            case EngineSdk.Events.ProgressPanelEnd:
                RemoveTaskProgress(evt);
                break;
        }
    }

    private void AddLog(string markupLine) {
        lock (_lock) {
            foreach (string line in markupLine.Split(["\r\n", "\n"], StringSplitOptions.None)) {
                _logLines.Add(line);
            }

            while (_logLines.Count > MaxLogLines) {
                _logLines.RemoveAt(0);
            }
            Refresh();
        }
    }

    private void UpdateOperationProgress(int current, int total, string name) {
        lock (_lock) {
            if (total > 0) _opTotal = total;
            _opCurrent = Math.Clamp(current, 0, Math.Max(0, _opTotal));
            if (!string.IsNullOrWhiteSpace(name)) _opName = name;
            Refresh();
        }
    }

    private void UpdateTaskProgress(IReadOnlyDictionary<string, object?> evt) {
        string id = GetText(evt, "id");
        string label = GetText(evt, "label");
        var stats = evt.TryGetValue("stats", out object? s) ? s as Dictionary<string, object?> : null;

        lock (_lock) {
            if (!_taskProgress.TryGetValue(id, out TaskProgressState? state)) {
                state = new TaskProgressState();
                _taskProgress[id] = state;
            }

            state.Label = string.IsNullOrWhiteSpace(label) ? "Processing" : label;
            if (stats != null) {
                state.Total = GetInt(stats, "total");
                state.Processed = GetInt(stats, "processed");
                state.Ok = GetInt(stats, "ok");
                state.Skip = GetInt(stats, "skip");
                state.Err = GetInt(stats, "err");
                if (stats.TryGetValue("percent", out object? pct) && pct != null &&
                    double.TryParse(pct.ToString(), out double p)) {
                    state.Percent = p;
                }
                else {
                    state.Percent = state.Total > 0 ? (double)state.Processed / state.Total : 0.0;
                }
            }
            Refresh();
        }
    }

    private void RemoveTaskProgress(IReadOnlyDictionary<string, object?> evt) {
        string id = GetText(evt, "id");
        lock (_lock) {
            _taskProgress.Remove(id);
            Refresh();
        }
    }

    private void Refresh() {
        _liveContext?.UpdateTarget(BuildLayout());
        _liveContext?.Refresh();
    }

    private IRenderable BuildLayout() {
        lock (_lock) {
            int termHeight = Math.Max(10, AnsiConsole.Profile.Height);
            int reservedRows = 3; // header, borders

            if (_opTotal > 0) reservedRows += 2;
            reservedRows += _taskProgress.Count * 2;
            if (!string.IsNullOrWhiteSpace(_promptMessage)) reservedRows += 2;

            int logHeight = Math.Max(3, termHeight - reservedRows);
            var visibleLogs = _logLines.TakeLast(logHeight);

            var logPanel = new Panel(new Markup(string.Join("\n", visibleLogs))) {
                Header = new PanelHeader("Output"),
                Border = BoxBorder.Rounded,
                Expand = true
            };

            var renderables = new List<IRenderable> { logPanel };

            // Render Aggregate Operations Progress Bar (using double brackets [[ ]] to escape ASCII bars)
            if (_opTotal > 0) {
                double pct = (double)_opCurrent / Math.Max(1, _opTotal);
                string barText = RenderAsciiBar(pct, 30);
                string opInfo = $"{Markup.Escape(_opName)} [[{Markup.Escape(barText)}]] {_opCurrent}/{_opTotal}";
                renderables.Add(new Panel(new Markup($"[cyan]{opInfo}[/]")) {
                    Header = new PanelHeader("Operations"),
                    Border = BoxBorder.Rounded,
                    Expand = true
                });
            }

            // Render Task Progress Bars
            if (_taskProgress.Count > 0) {
                var taskRows = new List<IRenderable>();
                foreach (TaskProgressState task in _taskProgress.Values) {
                    string bar = RenderAsciiBar(task.Percent, 28);
                    string pctText = $"{(int)Math.Round(task.Percent * 100)}%";
                    string details =
                        $"[green]{Markup.Escape(task.Label)}[/] [[{Markup.Escape(bar)}]] {pctText} {task.Processed}/{task.Total} (ok={task.Ok}, skip={task.Skip}, err={task.Err})";
                    taskRows.Add(new Markup(details));
                }

                renderables.Add(new Panel(new Rows(taskRows)) {
                    Header = new PanelHeader("Tasks"),
                    Border = BoxBorder.Rounded,
                    Expand = true
                });
            }

            // Render Prompt Panel if active
            if (!string.IsNullOrWhiteSpace(_promptMessage)) {
                renderables.Add(new Panel(new Markup($"[yellow]{Markup.Escape(_promptMessage)}[/]")) {
                    Header = new PanelHeader("Prompt"),
                    Border = BoxBorder.Rounded,
                    Expand = true
                });
            }

            return new Rows(renderables);
        }
    }

    private static string RenderAsciiBar(double percent, int width) {
        double clamped = Math.Clamp(percent, 0.0, 1.0);
        int filled = (int)Math.Round(clamped * width);
        return new string('=', Math.Max(0, filled - 1)) + (filled > 0 ? ">" : "") + new string(' ', width - filled);
    }

    private void SetPrompt(string message) {
        lock (_lock) {
            _promptMessage = string.IsNullOrWhiteSpace(message) ? "Input required" : message;
            Refresh();
        }
    }

    private void ClearPrompt() {
        lock (_lock) {
            _promptMessage = null;
            Refresh();
        }
    }

    private static string GetText(IReadOnlyDictionary<string, object?> values, string key) => values.TryGetValue(key, out object? val) ? val?.ToString() ?? string.Empty : string.Empty;

    private static int GetInt(IReadOnlyDictionary<string, object?> values, string key) => values.TryGetValue(key, out object? val) && val != null && int.TryParse(val.ToString(), out int r) ? r : 0;
}