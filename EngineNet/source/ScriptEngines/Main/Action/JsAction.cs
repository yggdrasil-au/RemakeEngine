using Jint;
using Jint.Native;

namespace EngineNet.ScriptEngines.Js;

public static class JsAction {

    /// <summary>
    /// Define important core functions as Js globals
    /// </summary>
    /// <param name="_JSWorld"></param>
    /// <param name="_tools"></param>
    /// <param name="_args"></param>
    /// <param name="_gameRoot"></param>
    /// <param name="_projectRoot"></param>
    /// <param name="_scriptPath"></param>
    public static void SetupCoreFunctions(
        JsWorld _JSWorld,
        Core.ExternalTools.IToolResolver _tools,
        string[] _args,
        string _gameRoot,
        string _projectRoot,
        string _scriptPath
    ) {
        // Setup SDK and modules
        //_JSWorld.JsScript.SetValue("sdk", Global.Sdk.CreateSdkModule(_JSWorld, _tools));
        //_JSWorld.JsScript.SetValue("sqlite", Global.Sqlite.CreateSqliteModule(_JSWorld));

        // expose a console object for logging, mapped to EngineSdk.PrintLine
        _JSWorld.console["log"] = (Action<string>)((message) => Core.UI.EngineSdk.PrintLine(message));
        _JSWorld.console["warn"] = (Action<string>)((message) => Core.UI.EngineSdk.Warn(message));
        _JSWorld.console["error"] = (Action<string>)((message) => Core.UI.EngineSdk.Error(message));
        _JSWorld.JsScript.SetValue("console", _JSWorld.console);

        // Expose a function to resolve tool path
        _JSWorld.JsScript.SetValue("tool", (Func<string, string?, string>)((id, ver) => _tools.ResolveToolPath(id, ver)));
        _JSWorld.JsScript.SetValue("ResolveToolPath", (Func<string, string?, string>)((id, ver) => _tools.ResolveToolPath(id, ver)));

        // Expose script arguments as argv array and argc count
        // Jint maps string[] directly to a JS Array
        _JSWorld.JsScript.SetValue("argv", _args);
        _JSWorld.JsScript.SetValue("argc", _args.Length);

        // get gameroot and projectroot paths
        _JSWorld.JsScript.SetValue("Game_Root", _gameRoot);
        _JSWorld.JsScript.SetValue("Project_Root", _projectRoot);

        // script_dir constant - directory containing the executing script
        string scriptDir = Path.GetDirectoryName(_scriptPath)?.Replace("\\", "/") ?? "";
        _JSWorld.JsScript.SetValue("script_dir", scriptDir);


        // :: start :: methods for emitting engineSDK events from JS scripts ::

        // basic outputs for warning and error events
        _JSWorld.JsScript.SetValue("warn", (Action<string>)Core.UI.EngineSdk.Warn);
        _JSWorld.JsScript.SetValue("error", (Action<string>)Core.UI.EngineSdk.Error);

        // emits the prompt query to the engine/ui and returns the user input
        _JSWorld.JsScript.SetValue("prompt", (Func<JsValue, JsValue, JsValue, string>)((message, id, secret) => {
            string msg = message.IsString() ? message.AsString() : message.ToString();
            string pid = (id.IsNull() || id.IsUndefined()) ? "q1" : (id.IsString() ? id.AsString() : id.ToString());
            bool sec = secret.IsBoolean() && secret.AsBoolean();
            return Core.UI.EngineSdk.Prompt(msg, pid, sec);
        }));

        _JSWorld.JsScript.SetValue("color_prompt", (Func<JsValue, JsValue, JsValue, JsValue, string>)((message, color, id, secret) => {
            string msg = message.IsString() ? message.AsString() : message.ToString();
            string col = color.IsString() ? color.AsString() : color.ToString();
            string pid = (id.IsNull() || id.IsUndefined()) ? "q1" : (id.IsString() ? id.AsString() : id.ToString());
            bool sec = secret.IsBoolean() && secret.AsBoolean();
            return Core.UI.EngineSdk.color_prompt(msg, col, pid, sec);
        }));

        // Alias for AU/UK spelling
        _JSWorld.JsScript.SetValue("colour_prompt", _JSWorld.JsScript.GetValue("color_prompt"));

        // :: Progress System ::
        Core.UI.EngineSdk.ScriptProgress? activeScriptProgress = null;

        // progress.new(total, id, label) -> Core.UI.EngineSdk.PanelProgress userdata
        _JSWorld.Progress["new"] = (Func<int, string?, string?, Core.UI.EngineSdk.PanelProgress>)((total, id, label) => {
            string pid = string.IsNullOrEmpty(id) ? "p1" : id!;
            return new Core.UI.EngineSdk.PanelProgress(total, pid, label);
        });

        // progress.start(total, label) -> Core.UI.EngineSdk.ScriptProgress userdata
        _JSWorld.Progress["start"] = (Func<int, string?, Core.UI.EngineSdk.ScriptProgress>)((total, label) => {
            activeScriptProgress = new Core.UI.EngineSdk.ScriptProgress(total, "s1", label);
            return activeScriptProgress;
        });

        // progress.step(label?)
        _JSWorld.Progress["step"] = (Action<string?>)((label) => {
            if (activeScriptProgress != null) {
                activeScriptProgress.Update(1, label);
                if (!string.IsNullOrEmpty(label)) {
                    Core.UI.EngineSdk.PrintLine($"[Step {activeScriptProgress.Current}/{activeScriptProgress.Total}] {label}", ConsoleColor.Magenta);
                }
            }
        });

        // progress.add_steps(count)
        _JSWorld.Progress["add_steps"] = (Action<int>)((count) => {
            if (activeScriptProgress != null) {
                activeScriptProgress.SetTotal(activeScriptProgress.Total + count);
            }
        });

        // progress.finish()
        _JSWorld.Progress["finish"] = (Action)(() => {
            if (activeScriptProgress != null) {
                activeScriptProgress.Complete();
            }
        });

        _JSWorld.JsScript.SetValue("progress", _JSWorld.Progress);

        // :: end ::
        //
        // :: start :: Debugging features ::

#if DEBUG
        _JSWorld.JsScript.SetValue("DEBUG", true);
#else
        _JSWorld.JsScript.SetValue("DEBUG", false);
#endif

        // :: JS Diagnostics logging ::
        _JSWorld.DiagnosticsMethods["Log"] = (Action<string>)Core.Diagnostics.JsLogger.JsLog;
        _JSWorld.DiagnosticsMethods["Trace"] = (Action<string>)Core.Diagnostics.JsLogger.JsTrace;

        _JSWorld.JsScript.SetValue("Diagnostics", _JSWorld.DiagnosticsMethods);

        // :: end ::
    }
}