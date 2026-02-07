using MoonSharp.Interpreter;


namespace EngineNet.ScriptEngines.Lua;

public static class LuaAction {

    /// <summary>
    /// Define important core functions as Lua globals
    /// </summary>
    /// <param name="_LuaWorld"></param>
    /// <param name="_tools"></param>
    /// <param name="_args"></param>
    /// <param name="_gameRoot"></param>
    /// <param name="_projectRoot"></param>
    /// <param name="_scriptPath"></param>
    public static void SetupCoreFunctions(
        LuaWorld _LuaWorld,
        Core.ExternalTools.IToolResolver _tools,
        string[] _args,
        string _gameRoot,
        string _projectRoot,
        string _scriptPath
    ) {
        // Setup SDK and modules
        _LuaWorld.LuaScript.Globals["sdk"] = Global.Sdk.CreateSdkModule(_LuaWorld, _tools);
        _LuaWorld.LuaScript.Globals["sqlite"] = Global.Sqlite.CreateSqliteModule(_LuaWorld);

        // Expose a function to resolve external tool path
        _LuaWorld.LuaScript.Globals["ResolveToolPath"] = (string id, string?ver) => _tools.ResolveToolPath(id, ver);
        _LuaWorld.LuaScript.Globals["tool"] = _LuaWorld.LuaScript.Globals["ResolveToolPath"]; // alias for convenience

        // Expose script arguments as argv array and argc count
        Table argvTable = new Table(_LuaWorld.LuaScript);
        for (int index = 0; index < _args.Length; index++) {
            argvTable[index + 1] = DynValue.NewString(_args[index]);
        }
        _LuaWorld.LuaScript.Globals["argv"] = argvTable; // array of arguments
        _LuaWorld.LuaScript.Globals["argc"] = _args.Length; // number of arguments

        // get gameroot and projectroot paths
        _LuaWorld.LuaScript.Globals["Game_Root"] = _gameRoot;
        Core.Diagnostics.Log($"[LuaScriptAction.cs::SetupCoreFunctions()] Set Game_Root to '{_gameRoot}'");
        _LuaWorld.LuaScript.Globals["Project_Root"] = _projectRoot;
        Core.Diagnostics.Log($"[LuaScriptAction.cs::SetupCoreFunctions()] Set Project_Root to '{_projectRoot}'");

        // script_dir constant - directory containing the executing script
        string scriptDir = System.IO.Path.GetDirectoryName(_scriptPath)?.Replace("\\", "/") ?? "";
        _LuaWorld.LuaScript.Globals["script_dir"] = scriptDir;

        // :: start :: methods for emitting engineSDK events from Lua scripts ::
        events(_LuaWorld);

        // :: Progress System ::
        progress(_LuaWorld);

        // :: end ::
        //
        // :: start :: Debugging features ::

        // integrate #if DEBUG checks into lua to allow C# debug-only code paths when running engine with debugger attached
#if DEBUG
        _LuaWorld.LuaScript.Globals["DEBUG"] = true;
#else
        _LuaWorld.LuaScript.Globals["DEBUG"] = false;
#endif

        string mode = "unknown";
        if (Program.isCli) mode = "cli";
        else if (Program.isGui) mode = "gui";
        else if (Program.isTui) mode = "tui";

        _LuaWorld.LuaScript.Globals["UIMode"] = mode;

        // :: Lua Diagnostics logging ::
        _LuaWorld.DiagnosticsMethods["Log"] = (System.Action<string>)Core.Diagnostics.LuaLogger.LuaLog;
        _LuaWorld.DiagnosticsMethods["Trace"] = (System.Action<string>)Core.Diagnostics.LuaLogger.LuaTrace;

        _LuaWorld.LuaScript.Globals["Diagnostics"] = _LuaWorld.DiagnosticsMethods;

        // :: end ::

    }

    private static void events(LuaWorld _LuaWorld) {

        // basic outputs for warning and error events
        _LuaWorld.LuaScript.Globals["warn"] = (System.Action<string>)Core.UI.EngineSdk.Warn;
        _LuaWorld.LuaScript.Globals["error"] = (System.Action<string>)Core.UI.EngineSdk.Error;

        // emits the prompt query to the engine/ui and returns the user input
        _LuaWorld.LuaScript.Globals["prompt"] = (System.Func<DynValue, DynValue, DynValue, string>)((message, id, secret) => {
            string msg = message.Type == DataType.String ? message.String : message.ToPrintString();
            string pid = id.Type == DataType.Nil || id.Type == DataType.Void ? "q1" : id.Type == DataType.String ? id.String : id.ToPrintString();
            bool sec = secret.Type == DataType.Boolean && secret.Boolean;
            return Core.UI.EngineSdk.Prompt(msg, pid, sec);
        });
        _LuaWorld.LuaScript.Globals["color_prompt"] = (System.Func<DynValue, DynValue, DynValue, DynValue, string>)((message, color, id, secret) => {
            string msg = message.Type == DataType.String ? message.String : message.ToPrintString();
            string col = color.Type == DataType.String ? color.String : color.ToPrintString();
            string pid = id.Type == DataType.Nil || id.Type == DataType.Void ? "q1" : id.Type == DataType.String ? id.String : id.ToPrintString();
            bool sec = secret.Type == DataType.Boolean && secret.Boolean;
            return Core.UI.EngineSdk.color_prompt(msg, col, pid, sec);
        });
        _LuaWorld.LuaScript.Globals["colour_prompt"] = _LuaWorld.LuaScript.Globals["color_prompt"]; // (Correct) AU spelling

    }

    /// <summary>
    /// Defines the progress API for Lua scripts, allowing them to create and update progress bars in the engine UI.
    /// </summary>
    /// <param name="_LuaWorld"></param>
    private static void progress(LuaWorld _LuaWorld) {
        Core.UI.EngineSdk.ScriptProgress? activeScriptProgress = null;

        // progress.new(total, id, label) -> Core.UI.EngineSdk.PanelProgress userdata
        _LuaWorld.progress["new"] = (System.Func<int, string?, string?, Core.UI.EngineSdk.PanelProgress>)((total, id, label) => {
            string pid = string.IsNullOrEmpty(id) ? "p1" : id!;
            return new Core.UI.EngineSdk.PanelProgress(total, pid, label);
        });

        // progress.start(total, label) -> Core.UI.EngineSdk.ScriptProgress userdata
        _LuaWorld.progress["start"] = (System.Func<int, string?, Core.UI.EngineSdk.ScriptProgress>)((total, label) => {
            activeScriptProgress = new Core.UI.EngineSdk.ScriptProgress(total, "s1", label);
            return activeScriptProgress;
        });

        // progress.step(label?) -> increments current progress by 1, optionally updates label
        _LuaWorld.progress["step"] = (System.Action<string?>)((label) => {
            if (activeScriptProgress != null) {
                activeScriptProgress.Update(1, label);
                if (!string.IsNullOrEmpty(label)) {
                    Core.UI.EngineSdk.PrintLine($"[Step {activeScriptProgress.Current}/{activeScriptProgress.Total}] {label}", System.ConsoleColor.Magenta);
                }
            }
        });

        // progress.add_steps(count) -> increases total steps
        _LuaWorld.progress["add_steps"] = (System.Action<int>)((count) => {
            if (activeScriptProgress != null) {
                activeScriptProgress.SetTotal(activeScriptProgress.Total + count);
            }
        });

        // progress.finish() -> completes the progress
        _LuaWorld.progress["finish"] = (System.Action)(() => {
            if (activeScriptProgress != null) {
                activeScriptProgress.Complete();
            }
        });

        _LuaWorld.LuaScript.Globals["progress"] = _LuaWorld.progress;
    }

}
