


namespace EngineNet.ScriptEngines.Python;

public static class PyAction {

    /// <summary>
    /// Define important core functions as Python globals
    /// </summary>
    /// <param name="world"></param>
    /// <param name="tools"></param>
    /// <param name="args"></param>
    /// <param name="gameRoot"></param>
    /// <param name="projectRoot"></param>
    /// <param name="scriptPath"></param>
    public static void SetupCoreFunctions(
        PyWorld world,
        Core.ExternalTools.IToolResolver tools,
        string[] args,
        string gameRoot,
        string projectRoot,
        string scriptPath
    ) {
        // expose a print function for logging, mapped to EngineSdk.PrintLine
        world.PythonScope.SetVariable("print", (Action<string>)((message) => Core.UI.EngineSdk.PrintLine(message)));
        world.PythonScope.SetVariable("warn", (Action<string>)Core.UI.EngineSdk.Warn);
        world.PythonScope.SetVariable("error", (Action<string>)Core.UI.EngineSdk.Error);

        // Expose a function to resolve tool path
        world.PythonScope.SetVariable("tool", (Func<string, string?, string>)((id, ver) => tools.ResolveToolPath(id, ver)));
        world.PythonScope.SetVariable("ResolveToolPath", (Func<string, string?, string>)((id, ver) => tools.ResolveToolPath(id, ver)));

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
        world.PythonScope.SetVariable("prompt", (Func<string, string?, bool, string>)((message, id, secret) => {
            return Core.UI.EngineSdk.Prompt(message, id ?? "q1", secret);
        }));

        world.PythonScope.SetVariable("color_prompt", (Func<string, string, string?, bool, string>)((message, color, id, secret) => {
            return Core.UI.EngineSdk.color_prompt(message, color, id ?? "q1", secret);
        }));

        // Alias for AU/UK spelling
        world.PythonScope.SetVariable("colour_prompt", world.PythonScope.GetVariable("color_prompt"));

        // :: Progress System ::
        Core.UI.EngineSdk.ScriptProgress? activeScriptProgress = null;

        // progress.new(total, id, label) -> Core.UI.EngineSdk.PanelProgress userdata
        world.Progress["new"] = (Func<int, string?, string?, Core.UI.EngineSdk.PanelProgress>)((total, id, label) => {
            string pid = string.IsNullOrEmpty(id) ? "p1" : id!;
            return new Core.UI.EngineSdk.PanelProgress(total, pid, label);
        });

        // progress.start(total, label) -> Core.UI.EngineSdk.ScriptProgress userdata
        world.Progress["start"] = (Func<int, string?, Core.UI.EngineSdk.ScriptProgress>)((total, label) => {
            activeScriptProgress = new Core.UI.EngineSdk.ScriptProgress(total, "s1", label);
            return activeScriptProgress;
        });

        // progress.step(label?)
        world.Progress["step"] = (Action<string?>)((label) => {
            if (activeScriptProgress != null) {
                activeScriptProgress.Update(1, label);
                if (!string.IsNullOrEmpty(label)) {
                    Core.UI.EngineSdk.PrintLine($"[Step {activeScriptProgress.Current}/{activeScriptProgress.Total}] {label}", ConsoleColor.Magenta);
                }
            }
        });

        // progress.add_steps(count)
        world.Progress["add_steps"] = (Action<int>)((count) => {
            if (activeScriptProgress != null) {
                activeScriptProgress.SetTotal(activeScriptProgress.Total + count);
            }
        });

        // progress.finish()
        world.Progress["finish"] = (Action)(() => {
            if (activeScriptProgress != null) {
                activeScriptProgress.Complete();
            }
        });

        world.PythonScope.SetVariable("progress", world.Progress);
        world.PythonScope.SetVariable("sdk", world.Sdk);

        // :: Debugging features ::
#if DEBUG
        world.PythonScope.SetVariable("DEBUG", true);
#else
        world.PythonScope.SetVariable("DEBUG", false);
#endif

        // :: Python Diagnostics logging ::
        world.Diagnostics["Log"] = (Action<string>)Core.Diagnostics.PythonLogger.PythonLog;
        world.Diagnostics["Trace"] = (Action<string>)Core.Diagnostics.PythonLogger.PythonTrace;

        world.PythonScope.SetVariable("Diagnostics", world.Diagnostics);
    }
}
