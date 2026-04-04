


using IronPython.Hosting;

namespace EngineNet.ScriptEngines.Python;

internal class PyProgressProxy {
    internal Shared.UI.EngineSdk.ScriptProgress? ActiveScriptProgress { get; set; }

    internal Func<int, string?, string?, Shared.UI.EngineSdk.PanelProgress>? NewFunc { get; set; }
    internal Func<int, string?, Shared.UI.EngineSdk.ScriptProgress>? StartFunc { get; set; }
    internal Action<string?>? StepAction { get; set; }
    internal Action<int>? AddStepsAction { get; set; }
    internal Action? FinishAction { get; set; }

    internal Shared.UI.EngineSdk.PanelProgress @new(int total, string? id = null, string? label = null) => NewFunc!(total, id, label);
    internal Shared.UI.EngineSdk.ScriptProgress start(int total, string? label = null) => StartFunc!(total, label);
    internal void step(string? label = null) => StepAction!(label);
    internal void add_steps(int count) => AddStepsAction!(count);
    internal void finish() => FinishAction!();
}

internal class PyDiagnosticsProxy {
    internal Action<string>? LogAction { get; set; }
    internal Action<string>? TraceAction { get; set; }

    internal void Log(string message) => LogAction!(message);
    internal void Trace(string message) => TraceAction!(message);
}

internal static class PyAction {

    /// <summary>
    /// Define important core functions as Python globals
    /// </summary>
    /// <param name="world"></param>
    /// <param name="tools"></param>
    /// <param name="args"></param>
    /// <param name="gameRoot"></param>
    /// <param name="projectRoot"></param>
    /// <param name="scriptPath"></param>
    internal static void SetupCoreFunctions(
        PyWorld world,
        Core.ExternalTools.JsonToolResolver tools,
        string[] args,
        string gameRoot,
        string projectRoot,
        string scriptPath
    ) {
        world.PythonScope.SetVariable("print", (Action<object>)((o) => Shared.UI.EngineSdk.PrintLine(o?.ToString() ?? "", ConsoleColor.White)));
        world.PythonScope.SetVariable("PrintLine", (Action<string>)((message) => Shared.UI.EngineSdk.PrintLine(message, ConsoleColor.White)));
        world.PythonScope.SetVariable("PrintLineColor", (Action<string, ConsoleColor>)((message, color) => Shared.UI.EngineSdk.PrintLine(message, color)));
        world.PythonScope.SetVariable("warn", (Action<string>)Shared.UI.EngineSdk.Warn);
        world.PythonScope.SetVariable("error", (Action<string>)Shared.UI.EngineSdk.Error);

        // Expose a function to resolve tool path
        world.PythonScope.SetVariable("tool", (Func<string, string, string>)((id, ver) => tools.ResolveToolPath(id, ver)));
        world.PythonScope.SetVariable("ResolveToolPath", (Func<string, string, string>)((id, ver) => tools.ResolveToolPath(id, ver)));

        // Expose script arguments as argv array and argc count
        world.PythonScope.SetVariable("argv", args);
        world.PythonScope.SetVariable("argc", args.Length);

        // get gameroot and projectroot paths
        world.PythonScope.SetVariable("Game_Root", gameRoot);
        world.PythonScope.SetVariable("Project_Root", projectRoot);

        // script_dir constant - directory containing the executing script
        string scriptDir = Path.GetDirectoryName(scriptPath)?.Replace("\\", "/") ?? "";
        world.PythonScope.SetVariable("script_dir", scriptDir);

        // emits the prompt query to the engine/ui and returns the user input
        world.PythonScope.SetVariable("prompt", (Func<string, string, bool, string>)((message, id, secret) => {
            return Shared.UI.EngineSdk.Prompt(message, id ?? "q1", secret);
        }));

        world.PythonScope.SetVariable("color_prompt", (Func<string, string, string, bool, string>)((message, color, id, secret) => {
            return Shared.UI.EngineSdk.color_prompt(message, color, id ?? "q1", secret);
        }));

        // Alias for AU/UK spelling
        world.PythonScope.SetVariable("colour_prompt", world.PythonScope.GetVariable("color_prompt"));

        // :: Progress System ::
        var progressProxy = new PyProgressProxy();

        // progress.new(total, id, label) -> Shared.UI.EngineSdk.PanelProgress userdata
        progressProxy.NewFunc = (total, id, label) => {
            string pid = string.IsNullOrEmpty(id) ? "p1" : id;
            return new Shared.UI.EngineSdk.PanelProgress(total, pid, label ?? "");
        };

        // progress.start(total, label) -> Shared.UI.EngineSdk.ScriptProgress userdata
        progressProxy.StartFunc = (total, label) => {
            progressProxy.ActiveScriptProgress = new Shared.UI.EngineSdk.ScriptProgress(total, "s1", label ?? "");
            return progressProxy.ActiveScriptProgress;
        };

        // progress.step(label?)
        progressProxy.StepAction = (label) => {
            if (progressProxy.ActiveScriptProgress != null) {
                progressProxy.ActiveScriptProgress.Update(1, label ?? "");
                if (!string.IsNullOrEmpty(label)) {
                    Shared.UI.EngineSdk.PrintLine($"[Step {progressProxy.ActiveScriptProgress.Current}/{progressProxy.ActiveScriptProgress.Total}] {label}", ConsoleColor.Magenta);
                }
            }
        };

        // progress.add_steps(count)
        progressProxy.AddStepsAction = (count) => {
            if (progressProxy.ActiveScriptProgress != null) {
                progressProxy.ActiveScriptProgress.SetTotal(progressProxy.ActiveScriptProgress.Total + count);
            }
        };

        // progress.finish()
        progressProxy.FinishAction = () => {
            if (progressProxy.ActiveScriptProgress != null) {
                progressProxy.ActiveScriptProgress.Complete();
            }
        };

        world.PythonScope.SetVariable("progress", progressProxy);
        world.PythonScope.SetVariable("sdk", world.Sdk);

        // :: Debugging features ::
#if DEBUG
        world.PythonScope.SetVariable("DEBUG", true);
#else
        world.PythonScope.SetVariable("DEBUG", false);
#endif

        // :: Python Diagnostics logging ::
        var diagnosticsProxy = new PyDiagnosticsProxy();
        diagnosticsProxy.LogAction = (Action<string>)Shared.Diagnostics.PythonLogger.PythonLog;
        diagnosticsProxy.TraceAction = (Action<string>)Shared.Diagnostics.PythonLogger.PythonTrace;

        world.PythonScope.SetVariable("Diagnostics", diagnosticsProxy);

        // :: Mock Typing Module ::
        // This allows 'from typing import ...' to work in IDEs while remaining a no-op in IronPython
        var typingModule = world.PythonEngine.CreateModule("typing");
        typingModule.SetVariable("TYPE_CHECKING", false);
        typingModule.SetVariable("Any", null);
        typingModule.SetVariable("Dict", null);
        typingModule.SetVariable("List", null);
        typingModule.SetVariable("Optional", null);
        typingModule.SetVariable("Union", null);
        typingModule.SetVariable("Callable", null);
        typingModule.SetVariable("TypeVar", null);
        typingModule.SetVariable("Generic", null);
        typingModule.SetVariable("Tuple", null);
        typingModule.SetVariable("Set", null);
        typingModule.SetVariable("Iterable", null);
        typingModule.SetVariable("Sequence", null);
    }
}
