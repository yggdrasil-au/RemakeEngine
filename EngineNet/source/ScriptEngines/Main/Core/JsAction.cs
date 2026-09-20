using Jint;
using Jint.Native;

namespace EngineNet.ScriptEngines.Js;

internal static class JsAction {

    /// <summary>
    /// Define important core functions as Js globals
    /// </summary>
    /// <param name="_JSWorld"></param>
    /// <param name="_tools"></param>
    /// <param name="_args"></param>
    /// <param name="_gameRoot"></param>
    /// <param name="_projectRoot"></param>
    /// <param name="_scriptPath"></param>
    internal static void SetupCoreFunctions(
        JsWorld _JSWorld,
        Core.Abstractions.IJsonToolResolver _tools,
        string[] _args,
        string _gameRoot,
        string _projectRoot,
        string _scriptPath
    ) {
        // Setup SDK and modules
        //_JSWorld.JsScript.SetValue("sdk", Global.Sdk.CreateSdkModule(_JSWorld, _tools));
        //_JSWorld.JsScript.SetValue("sqlite", Global.Sqlite.CreateSqliteModule(_JSWorld));

        // expose a console object for logging, mapped to Shared.IO.UI.EngineSdk.PrintLine
        _JSWorld.console[key: "log"] = (Action<string>)((message) => Shared.IO.UI.EngineSdk.PrintLine(message));
        _JSWorld.console[key: "warn"] = (Action<string>)((message) => Shared.IO.UI.EngineSdk.Warn(message));
        _JSWorld.console[key: "error"] = (Action<string>)((message) => Shared.IO.UI.EngineSdk.Error(message));
        _JSWorld.JsScript.SetValue(name: "console", obj: _JSWorld.console);

        // Expose a function to resolve tool path
        _JSWorld.JsScript.SetValue(name: "tool", obj: (Func<string, string?, string>)((id, ver) => _tools.ResolveToolPath(toolId: id, version: ver)));
        _JSWorld.JsScript.SetValue(name: "ResolveToolPath", obj: (Func<string, string?, string>)((id, ver) => _tools.ResolveToolPath(toolId: id, version: ver)));

        // Expose script arguments as argv array and argc count
        // Jint maps string[] directly to a JS Array
        _JSWorld.JsScript.SetValue(name: "argv", obj: _args);
        _JSWorld.JsScript.SetValue(name: "argc", _args.Length);

        // get gameroot and projectroot paths
        _JSWorld.JsScript.SetValue(name: "Game_Root", _gameRoot);
        _JSWorld.JsScript.SetValue(name: "Project_Root", _projectRoot);

        // script_dir constant - directory containing the executing script
        string scriptDir = Path.GetDirectoryName(path: _scriptPath)?.Replace(oldValue: "\\", newValue: "/") ?? "";
        _JSWorld.JsScript.SetValue(name: "script_dir", scriptDir);


        // :: start :: methods for emitting Shared.IO.UI.EngineSdk. events from JS scripts ::

        // basic outputs for warning and error events
        _JSWorld.JsScript.SetValue(name: "warn", obj: (Action<string>)Shared.IO.UI.EngineSdk.Warn);
        _JSWorld.JsScript.SetValue(name: "error", obj: (Action<string>)Shared.IO.UI.EngineSdk.Error);

        // emits the prompt query to the engine/ui and returns the user input
        _JSWorld.JsScript.SetValue(name: "prompt", obj: (Func<JsValue, JsValue, JsValue, string>)((message, id, secret) => {
            string msg = message.IsString() ? message.AsString() : message.ToString();
            string pid = (id.IsNull() || id.IsUndefined()) ? "q1" : (id.IsString() ? id.AsString() : id.ToString());
            bool sec = secret.IsBoolean() && secret.AsBoolean();
            return Shared.IO.UI.EngineSdk.Prompt(msg, id: pid, secret: sec);
        }));

        _JSWorld.JsScript.SetValue(name: "color_prompt", obj: (Func<JsValue, JsValue, JsValue, JsValue, string>)((message, color, id, secret) => {
            string msg = message.IsString() ? message.AsString() : message.ToString();
            string col = color.IsString() ? color.AsString() : color.ToString();
            string pid = (id.IsNull() || id.IsUndefined()) ? "q1" : (id.IsString() ? id.AsString() : id.ToString());
            bool sec = secret.IsBoolean() && secret.AsBoolean();
            return Shared.IO.UI.EngineSdk.color_prompt(msg, color: col, id: pid, secret: sec);
        }));

        // Alias for AU/UK spelling
        _JSWorld.JsScript.SetValue(name: "colour_prompt", _JSWorld.JsScript.GetValue(propertyName: "color_prompt"));

        // :: Progress System ::
        Shared.IO.UI.EngineSdk.ScriptProgress? activeScriptProgress = null;

        // progress.new(total, id, label) -> Shared.IO.UI.EngineSdk.PanelProgress userdata
        _JSWorld.Progress[key: "new"] = (Func<int, string?, string?, Shared.IO.UI.EngineSdk.PanelProgress>)((total, id, label) => {
            string pid = string.IsNullOrEmpty(id) ? "p1" : id;
            return new Shared.IO.UI.EngineSdk.PanelProgress(total: total, id: pid, label: label);
        });

        // progress.start(total, label) -> Shared.IO.UI.EngineSdk.ScriptProgress userdata
        _JSWorld.Progress[key: "start"] = (Func<int, string?, Shared.IO.UI.EngineSdk.ScriptProgress>)((total, label) => {
            activeScriptProgress = new Shared.IO.UI.EngineSdk.ScriptProgress(total: total, id: "s1", label: label);
            return activeScriptProgress;
        });

        // progress.step(label?)
        _JSWorld.Progress[key: "step"] = (Action<string?>)((label) => {
            if (activeScriptProgress != null) {
                activeScriptProgress.Update(inc: 1, newLabel: label);
                if (!string.IsNullOrEmpty(label)) {
                    Shared.IO.UI.EngineSdk.PrintLine($"[Step {activeScriptProgress.Current}/{activeScriptProgress.Total}] {label}", color: ConsoleColor.Magenta);
                }
            }
        });

        // progress.add_steps(count)
        _JSWorld.Progress[key: "add_steps"] = (Action<int>)((count) => {
            if (activeScriptProgress != null) {
                activeScriptProgress.SetTotal(total: activeScriptProgress.Total + count);
            }
        });

        // progress.finish()
        _JSWorld.Progress[key: "finish"] = (Action)(() => {
            if (activeScriptProgress != null) {
                activeScriptProgress.Complete();
            }
        });

        _JSWorld.JsScript.SetValue(name: "progress", obj: _JSWorld.Progress);

        // :: end ::
        //
        // :: start :: Debugging features ::

#if DEBUG
        _JSWorld.JsScript.SetValue(name: "DEBUG", true);
#else
        _JSWorld.JsScript.SetValue("DEBUG", false);
#endif

        // :: JS Diagnostics logging ::
        _JSWorld.DiagnosticsMethods[key: "Log"] = (Action<string>)Shared.IO.Diagnostics.JsLogger.JsLog;
        _JSWorld.DiagnosticsMethods[key: "Trace"] = (Action<string>)Shared.IO.Diagnostics.JsLogger.JsTrace;

        _JSWorld.JsScript.SetValue(name: "Diagnostics", obj: _JSWorld.DiagnosticsMethods);

        // :: end ::
    }
}
