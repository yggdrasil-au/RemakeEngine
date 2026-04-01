
using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua;

/// <summary>
/// LuaAction sets up the core global variables, functions, modules, and diagnostics for Lua scripts in the EngineNet environment.
/// </summary>
internal static class LuaAction {

    /// <summary>
    /// Sets up the core global variables, functions, modules, and diagnostics for Lua scripts.
    /// </summary>
    /// <param name="_LuaWorld"></param>
    /// <param name="_tools"></param>
    /// <param name="_commandService"></param>
    /// <param name="_args"></param>
    /// <param name="_gameRoot"></param>
    /// <param name="_projectRoot"></param>
    /// <param name="_scriptPath"></param>
    internal static void CreateGlobals(
        LuaWorld _LuaWorld,
        Core.ExternalTools.JsonToolResolver _tools,
        Core.Services.CommandService _commandService,
        string[] _args,
        string _gameRoot,
        string _projectRoot,
        string _scriptPath
    ) {
        // --- 1. Global Variables & Environment Constants ---
        CreateGlobalVars(_LuaWorld, _args, _gameRoot, _projectRoot, _scriptPath);

        // --- 2. Global Functions ---
        CreateGlobalFunctions(_LuaWorld, _tools);

        // --- 3. Global Modules & Sub-Module Setup ---
        CreateGlobalModules(_LuaWorld, _tools, _commandService);

        // --- 4. Diagnostics & Logging ---
        CreateGlobalDiagnostics(_LuaWorld, _gameRoot, _projectRoot);
    }

    /// <summary>
    /// Defines global variables and environment constants for Lua scripts, such as Game_Root, Project_Root, script_dir, argv, argc, UIMode, and DEBUG.
    /// </summary>
    /// <param name="_LuaWorld"></param>
    /// <param name="_args"></param>
    /// <param name="_gameRoot"></param>
    /// <param name="_projectRoot"></param>
    /// <param name="_scriptPath"></param>
    private static void CreateGlobalVars(LuaWorld _LuaWorld, string[] _args, string _gameRoot, string _projectRoot, string _scriptPath) {
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
        if (EngineNet.Core.Main.IsCli) mode = "cli";
        else if (EngineNet.Core.Main.IsGui) mode = "gui";
        else if (EngineNet.Core.Main.IsTui) mode = "tui";
        _LuaWorld.LuaScript.Globals["UIMode"] = mode;

        // Debug state
#if DEBUG
        _LuaWorld.LuaScript.Globals["DEBUG"] = true;
#else
        _LuaWorld.LuaScript.Globals["DEBUG"] = false;
#endif
    }

    /// <summary>
    /// Defines global functions available to Lua scripts,
    /// including utility functions like 'join' for path manipulation, 'import' for loading other Lua files, and 'tool' for resolving external tool paths.
    /// Also defines functions for emitting engine SDK events (warn, error, prompt) to allow Lua scripts to interact with the user through the engine's UI.
    /// </summary>
    /// <param name="_LuaWorld"></param>
    /// <param name="_tools"></param>
    /// <exception cref="ScriptRuntimeException"></exception>
    private static void CreateGlobalFunctions(LuaWorld _LuaWorld, Core.ExternalTools.JsonToolResolver _tools) {

        // Methods for emitting engineSDK events (warn, error, prompt)
        EngineSdkGlobals(_LuaWorld);

        // Global path join (soft join, always uses forward slashes)
        _LuaWorld.LuaScript.Globals["join"] = (System.Func<ScriptExecutionContext, CallbackArguments, DynValue>)((_, args) => {
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
        _LuaWorld.LuaScript.Globals["import"] = (System.Func<ScriptExecutionContext, string, DynValue>)((_, path) => {
            // Re-fetch script_dir from globals at runtime to allow dynamic updates
            string currentScriptDir = _LuaWorld.LuaScript.Globals.Get("script_dir").String ?? "";
            string absolutePath = System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(currentScriptDir, path);

            if (!absolutePath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) {
                absolutePath += ".lua";
            }

            if (!Security.TryGetAllowedCanonicalPathWithPrompt(absolutePath, out string safePath)) {
                throw new ScriptRuntimeException($"import error: access denied '{absolutePath}'");
            }

            if (!System.IO.File.Exists(safePath)) {
                throw new ScriptRuntimeException($"import error: file not found '{safePath}'");
            }

            string previousScriptDir = currentScriptDir;
            string nextScriptDir = System.IO.Path.GetDirectoryName(safePath)?.Replace("\\", "/") ?? "";

            try {
                // Ensure nested imports resolve relative to the currently imported file.
                _LuaWorld.LuaScript.Globals["script_dir"] = nextScriptDir;
                return _LuaWorld.LuaScript.DoFile(safePath);
            } catch (Exception ex) {
                throw new ScriptRuntimeException($"import error in '{safePath}': {ex.Message}");
            } finally {
                _LuaWorld.LuaScript.Globals["script_dir"] = previousScriptDir;
            }
        });

        // Custom 'require' implementation that matches the 'import' behavior
        _LuaWorld.LuaScript.Globals["require"] = _LuaWorld.LuaScript.Globals["import"];
    }

    /// <summary>
    /// Defines the global modules available to Lua scripts,
    /// including the core SDK module and any additional modules for functionality like file system access, process execution, JSON handling, etc.
    /// </summary>
    /// <param name="_LuaWorld"></param>
    /// <param name="_tools"></param>
    /// <param name="_commandService"></param>
    private static void CreateGlobalModules(LuaWorld _LuaWorld, Core.ExternalTools.JsonToolResolver _tools, Core.Services.CommandService _commandService) {
        Global.Sdk.CreateSdkModule(_LuaWorld, _tools, _commandService);
        Global.Sqlite.CreateSqliteModule(_LuaWorld);
        Global.Progress.CreateProgressModule(_LuaWorld);
    }

    /// <summary>
    /// Defines global diagnostics and logging functions for Lua scripts,
    /// allowing them to log messages and trace information through the engine's logging system.
    /// Also logs initial environment information at startup.
    /// </summary>
    /// <param name="_LuaWorld"></param>
    /// <param name="_gameRoot"></param>
    /// <param name="_projectRoot"></param>
    private static void CreateGlobalDiagnostics(LuaWorld _LuaWorld, string _gameRoot, string _projectRoot) {
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


}
