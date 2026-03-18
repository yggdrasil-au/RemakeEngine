
using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua;

public static class LuaAction {

    public static void SetupCoreFunctions(
        LuaWorld _LuaWorld,
        Core.ExternalTools.JsonToolResolver _tools,
        string[] _args,
        string _gameRoot,
        string _projectRoot,
        string _scriptPath
    ) {
        // --- 1. Global Variables & Environment Constants ---

        globalVars(_LuaWorld, _args, _gameRoot, _projectRoot, _scriptPath);

        // --- 2. Global Functions ---

        globalFunctions(_LuaWorld, _tools);

        // --- 3. Global Modules & Sub-Module Setup ---

        globalModules(_LuaWorld, _tools);

        // --- 4. Diagnostics & Logging ---

        globalDiagnostics(_LuaWorld, _gameRoot, _projectRoot);

    }

    private static void globalVars(LuaWorld _LuaWorld, string[] _args, string _gameRoot, string _projectRoot, string _scriptPath) {
        // Game and Project path constants
        _LuaWorld.LuaScript.Globals["Game_Root"] = _gameRoot;
        _LuaWorld.LuaScript.Globals["Project_Root"] = _projectRoot;

        // script_dir - directory containing the executing script
        string scriptDir = System.IO.Path.GetDirectoryName(_scriptPath)?.Replace("\\", "/") ?? "";
        _LuaWorld.LuaScript.Globals["script_dir"] = scriptDir;

        // script arguments
        Table argvTable = new Table(_LuaWorld.LuaScript);
        for (int index = 0; index < _args.Length; index++) {
            argvTable[index + 1] = DynValue.NewString(_args[index]);
        }
        _LuaWorld.LuaScript.Globals["argv"] = argvTable; // array of arguments
        _LuaWorld.LuaScript.Globals["argc"] = _args.Length; // number of arguments

        // UI Mode (cli, gui, tui)
        string mode = "unknown";
        if (Program.isCli) mode = "cli";
        else if (Program.isGui) mode = "gui";
        else if (Program.isTui) mode = "tui";
        _LuaWorld.LuaScript.Globals["UIMode"] = mode;

        // Debug state
#if DEBUG
        _LuaWorld.LuaScript.Globals["DEBUG"] = true;
#else
        _LuaWorld.LuaScript.Globals["DEBUG"] = false;
#endif
    }

    private static void globalFunctions(LuaWorld _LuaWorld, Core.ExternalTools.JsonToolResolver _tools) {

        // Methods for emitting engineSDK events (warn, error, prompt)
        EngineSdkGlobals(_LuaWorld);

        // Global path join (soft join, always uses forward slashes)
        _LuaWorld.LuaScript.Globals["join"] = (System.Func<ScriptExecutionContext, CallbackArguments, DynValue>)((context, args) => {
            var parts = Enumerable.Range(0, args.Count)
                .Select(i => args[i])
                .Where(v => v.Type != DataType.Nil && v.Type != DataType.Void)
                .Select(v => v.Type == DataType.String ? v.String : v.ToPrintString())
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s.Replace("\\", "/"))
                .ToList();

            if (parts.Count == 0) return DynValue.NewString("");

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < parts.Count; i++) {
                var p = parts[i];
                if (i > 0) {
                    p = p.TrimStart('/');
                    if (sb.Length > 0 && sb[^1] != '/') sb.Append('/');
                }
                sb.Append(p);
            }
            return DynValue.NewString(sb.ToString());
        });

        // Resolve external tool path
        _LuaWorld.LuaScript.Globals["ResolveToolPath"] = (string id, string? ver) => _tools.ResolveToolPath(id, ver);
        _LuaWorld.LuaScript.Globals["tool"] = _LuaWorld.LuaScript.Globals["ResolveToolPath"]; // alias for convenience

        // Global 'import' function - loads and executes Lua files relative to current script_dir global
        _LuaWorld.LuaScript.Globals["import"] = (System.Func<ScriptExecutionContext, string, DynValue>)((context, path) => {
            // Re-fetch script_dir from globals at runtime to allow dynamic updates
            string currentScriptDir = _LuaWorld.LuaScript.Globals.Get("script_dir").String ?? "";
            string absolutePath = System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(currentScriptDir, path);

            if (!absolutePath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) {
                absolutePath += ".lua";
            }

            if (!System.IO.File.Exists(absolutePath)) {
                throw new ScriptRuntimeException($"import error: file not found '{absolutePath}'");
            }

            try {
                return _LuaWorld.LuaScript.DoFile(absolutePath);
            } catch (Exception ex) {
                throw new ScriptRuntimeException($"import error in '{absolutePath}': {ex.Message}");
            }
        });

        // Custom 'require' implementation that matches the 'import' behavior
        _LuaWorld.LuaScript.Globals["require"] = _LuaWorld.LuaScript.Globals["import"];
    }

    private static void globalModules(LuaWorld _LuaWorld, Core.ExternalTools.JsonToolResolver _tools) {
        // Core Modules
        _LuaWorld.LuaScript.Globals["sdk"] = Global.Sdk.CreateSdkModule(_LuaWorld, _tools);
        _LuaWorld.LuaScript.Globals["sqlite"] = Global.Sqlite.CreateSqliteModule(_LuaWorld);
        // Progress System internal components
        CreateProgressModule(_LuaWorld);
    }

    private static void globalDiagnostics(LuaWorld _LuaWorld, string _gameRoot, string _projectRoot) {
        // Lua Diagnostics logging methods
        _LuaWorld.DiagnosticsMethods["Log"] = (System.Action<string>)Core.Diagnostics.LuaLogger.LuaLog;
        _LuaWorld.DiagnosticsMethods["Trace"] = (System.Action<string>)Core.Diagnostics.LuaLogger.LuaTrace;
        _LuaWorld.LuaScript.Globals["Diagnostics"] = _LuaWorld.DiagnosticsMethods;

        // Final startup logs
        Core.Diagnostics.Log($"[LuaScriptAction.cs::SetupCoreFunctions()] Set Game_Root to '{_gameRoot}'");
        Core.Diagnostics.Log($"[LuaScriptAction.cs::SetupCoreFunctions()] Set Project_Root to '{_projectRoot}'");
    }


    /// <summary>
    /// Defines methods for emitting engine SDK events (warn, error, prompt) to allow Lua scripts to interact with the user through the engine's UI.
    /// </summary>
    /// <param name="_LuaWorld"></param>
    private static void EngineSdkGlobals(LuaWorld _LuaWorld) {
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
    private static void CreateProgressModule(LuaWorld _LuaWorld) {
        Core.UI.EngineSdk.ScriptProgress? activeScriptProgress = null;

        // progress.new(total, id, label) -> Core.UI.EngineSdk.PanelProgress userdata
        _LuaWorld.Progress["new"] = (System.Func<int, string?, string?, Core.UI.EngineSdk.PanelProgress>)((total, id, label) => {
            string pid = string.IsNullOrEmpty(id) ? "p1" : id!;
            return new Core.UI.EngineSdk.PanelProgress(total, pid, label);
        });

        // progress.start(total, label) -> Core.UI.EngineSdk.ScriptProgress userdata
        _LuaWorld.Progress["start"] = (System.Func<int, string?, Core.UI.EngineSdk.ScriptProgress>)((total, label) => {
            activeScriptProgress = new Core.UI.EngineSdk.ScriptProgress(total, "s1", label);
            return activeScriptProgress;
        });

        // progress.step(label?) -> increments current progress by 1, optionally updates label
        _LuaWorld.Progress["step"] = (System.Action<string?>)((label) => {
            if (activeScriptProgress != null) {
                activeScriptProgress.Update(1, label);
                if (!string.IsNullOrEmpty(label)) {
                    Core.UI.EngineSdk.PrintLine($"[Step {activeScriptProgress.Current}/{activeScriptProgress.Total}] {label}", System.ConsoleColor.Magenta);
                }
            }
        });

        // progress.add_steps(count) -> increases total steps
        _LuaWorld.Progress["add_steps"] = (System.Action<int>)((count) => {
            if (activeScriptProgress != null) {
                activeScriptProgress.SetTotal(activeScriptProgress.Total + count);
            }
        });

        // progress.finish() -> completes the progress
        _LuaWorld.Progress["finish"] = (System.Action)(() => {
            if (activeScriptProgress != null) {
                activeScriptProgress.Complete();
            }
        });

        _LuaWorld.LuaScript.Globals["progress"] = _LuaWorld.Progress;
    }

}
