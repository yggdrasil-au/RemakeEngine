


using IronPython.Hosting;

namespace EngineNet.ScriptEngines.Python;

using Microsoft.Scripting.Hosting;

internal sealed class PyProgressProxy {
    internal Shared.IO.UI.EngineSdk.ScriptProgress? ActiveScriptProgress { get; set; }

    internal Func<int, string?, string?, Shared.IO.UI.EngineSdk.PanelProgress>? NewFunc { get; set; }
    internal Func<int, string?, Shared.IO.UI.EngineSdk.ScriptProgress>? StartFunc { get; set; }
    internal Action<string?>? StepAction { get; set; }
    internal Action<int>? AddStepsAction { get; set; }
    internal Action? FinishAction { get; set; }

    internal Shared.IO.UI.EngineSdk.PanelProgress @new(int total, string? id = null, string? label = null) => NewFunc!(arg1: total, arg2: id, arg3: label);
    internal Shared.IO.UI.EngineSdk.ScriptProgress start(int total, string? label = null) => StartFunc!(arg1: total, arg2: label);
    internal void step(string? label = null) => StepAction!(obj: label);
    internal void add_steps(int count) => AddStepsAction!(obj: count);
    internal void finish() => FinishAction!();
}

internal sealed class PyDiagnosticsProxy {
    internal Action<string>? LogAction { get; set; }
    internal Action<string>? TraceAction { get; set; }

    internal void Log(string message) => LogAction!(obj: message);
    internal void Trace(string message) => TraceAction!(obj: message);
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
        Core.Abstractions.IJsonToolResolver tools,
        string[] args,
        string gameRoot,
        string projectRoot,
        string scriptPath
    ) {
        world.PythonScope.SetVariable(name: "print", (Action<object>)((o) => Shared.IO.UI.EngineSdk.PrintLine(o?.ToString() ?? "", color: ConsoleColor.White)));
        world.PythonScope.SetVariable(name: "PrintLine", (Action<string>)((message) => Shared.IO.UI.EngineSdk.PrintLine(message, color: ConsoleColor.White)));
        world.PythonScope.SetVariable(name: "PrintLineColor", (Action<string, ConsoleColor>)((message, color) => Shared.IO.UI.EngineSdk.PrintLine(message, color: color)));
        world.PythonScope.SetVariable(name: "warn", (Action<string>)Shared.IO.UI.EngineSdk.Warn);
        world.PythonScope.SetVariable(name: "error", (Action<string>)Shared.IO.UI.EngineSdk.Error);

        // Expose a function to resolve tool path
        world.PythonScope.SetVariable(name: "tool", (Func<string, string, string>)((id, ver) => tools.ResolveToolPath(toolId: id, version: ver)));
        world.PythonScope.SetVariable(name: "ResolveToolPath", (Func<string, string, string>)((id, ver) => tools.ResolveToolPath(toolId: id, version: ver)));

        // Expose script arguments as argv array and argc count
        world.PythonScope.SetVariable(name: "argv", args);
        world.PythonScope.SetVariable(name: "argc", args.Length);

        // get gameroot and projectroot paths
        world.PythonScope.SetVariable(name: "Game_Root", gameRoot);
        world.PythonScope.SetVariable(name: "Project_Root", projectRoot);

        // script_dir constant - directory containing the executing script
        string scriptDir = Path.GetDirectoryName(path: scriptPath)?.Replace(oldValue: "\\", newValue: "/") ?? "";
        world.PythonScope.SetVariable(name: "script_dir", scriptDir);

        // emits the prompt query to the engine/ui and returns the user input
        world.PythonScope.SetVariable(name: "prompt", (Func<string, string, bool, string>)((message, id, secret) => {
            return Shared.IO.UI.EngineSdk.Prompt(message, id: id ?? "q1", secret: secret);
        }));

        world.PythonScope.SetVariable(name: "color_prompt", (Func<string, string, string, bool, string>)((message, color, id, secret) => {
            return Shared.IO.UI.EngineSdk.color_prompt(message, color: color, id: id ?? "q1", secret: secret);
        }));

        // Alias for AU/UK spelling
        world.PythonScope.SetVariable(name: "colour_prompt", value: world.PythonScope.GetVariable(name: "color_prompt"));

        // :: Progress System ::
        PyProgressProxy progressProxy = new();

        // progress.new(total, id, label) -> Shared.IO.UI.EngineSdk.PanelProgress userdata
        progressProxy.NewFunc = (total, id, label) => {
            string pid = string.IsNullOrEmpty(id) ? "p1" : id;
            return new Shared.IO.UI.EngineSdk.PanelProgress(total: total, id: pid, label: label ?? "");
        };

        // progress.start(total, label) -> Shared.IO.UI.EngineSdk.ScriptProgress userdata
        progressProxy.StartFunc = (total, label) => {
            progressProxy.ActiveScriptProgress = new Shared.IO.UI.EngineSdk.ScriptProgress(total: total, id: "s1", label: label ?? "");
            return progressProxy.ActiveScriptProgress;
        };

        // progress.step(label?)
        progressProxy.StepAction = (label) => {
            if (progressProxy.ActiveScriptProgress != null) {
                progressProxy.ActiveScriptProgress.Update(inc: 1, newLabel: label ?? "");
                if (!string.IsNullOrEmpty(label)) {
                    Shared.IO.UI.EngineSdk.PrintLine($"[Step {progressProxy.ActiveScriptProgress.Current}/{progressProxy.ActiveScriptProgress.Total}] {label}", color: ConsoleColor.Magenta);
                }
            }
        };

        // progress.add_steps(count)
        progressProxy.AddStepsAction = (count) => {
            if (progressProxy.ActiveScriptProgress != null) {
                progressProxy.ActiveScriptProgress.SetTotal(total: progressProxy.ActiveScriptProgress.Total + count);
            }
        };

        // progress.finish()
        progressProxy.FinishAction = () => {
            if (progressProxy.ActiveScriptProgress != null) {
                progressProxy.ActiveScriptProgress.Complete();
            }
        };

        world.PythonScope.SetVariable(name: "progress", progressProxy);
        world.PythonScope.SetVariable(name: "sdk", world.Sdk);

        // :: Debugging features ::
#if DEBUG
        world.PythonScope.SetVariable(name: "DEBUG", true);
#else
        world.PythonScope.SetVariable("DEBUG", false);
#endif

        // :: Python Diagnostics logging ::
        PyDiagnosticsProxy diagnosticsProxy = new();
        diagnosticsProxy.LogAction = (Action<string>)Shared.IO.Diagnostics.PythonLogger.PythonLog;
        diagnosticsProxy.TraceAction = (Action<string>)Shared.IO.Diagnostics.PythonLogger.PythonTrace;

        world.PythonScope.SetVariable(name: "Diagnostics", diagnosticsProxy);

        // :: Mock Typing Module ::
        // This allows 'from typing import ...' to work in IDEs while remaining a no-op in IronPython
        ScriptScope typingModule = world.PythonEngine.CreateModule(name: "typing");
        typingModule.SetVariable(name: "TYPE_CHECKING", false);
        typingModule.SetVariable(name: "Any", null);
        typingModule.SetVariable(name: "Dict", null);
        typingModule.SetVariable(name: "List", null);
        typingModule.SetVariable(name: "Optional", null);
        typingModule.SetVariable(name: "Union", null);
        typingModule.SetVariable(name: "Callable", null);
        typingModule.SetVariable(name: "TypeVar", null);
        typingModule.SetVariable(name: "Generic", null);
        typingModule.SetVariable(name: "Tuple", null);
        typingModule.SetVariable(name: "Set", null);
        typingModule.SetVariable(name: "Iterable", null);
        typingModule.SetVariable(name: "Sequence", null);
    }
}
