
using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua;

using System.Text;

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
        Core.Services.CommandService.CommandService _commandService,
        string[] _args,
        string _gameRoot,
        string _projectRoot,
        string _scriptPath
    ) {
        // --- 1. Global Variables & Environment Constants ---
        CreateGlobalVars(_LuaWorld: _LuaWorld, _args: _args, _gameRoot: _gameRoot, _projectRoot: _projectRoot, _scriptPath: _scriptPath);

        // --- 2. Global Functions ---
        CreateGlobalFunctions(_LuaWorld: _LuaWorld, _tools: _tools);

        // --- 3. Global Modules & Sub-Module Setup ---
        CreateGlobalModules(_LuaWorld: _LuaWorld, _tools: _tools, _commandService: _commandService);

        // --- 4. Diagnostics & Logging ---
        CreateGlobalDiagnostics(_LuaWorld: _LuaWorld, _gameRoot: _gameRoot, _projectRoot: _projectRoot);
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
        _LuaWorld.LuaScript.Globals[key: "Game_Root"] = _gameRoot;
        _LuaWorld.LuaScript.Globals[key: "Project_Root"] = _projectRoot;

        // script_dir - directory containing the executing script
        string scriptDir = System.IO.Path.GetDirectoryName(path: _scriptPath)?.Replace(oldValue: "\\", newValue: "/") ?? "";
        _LuaWorld.LuaScript.Globals[key: "script_dir"] = scriptDir;

        // script arguments
        Table argvTable = new(owner: _LuaWorld.LuaScript);
        for (int index = 0; index < _args.Length; index++) {
            argvTable[key: index + 1] = DynValue.NewString(str: _args[index]);
        }
        _LuaWorld.LuaScript.Globals[key: "argv"] = argvTable; // array of arguments
        _LuaWorld.LuaScript.Globals[key: "argc"] = _args.Length; // number of arguments

        // UI Mode (cli, gui, tui)
        string mode = "unknown";
        if (EngineNet.Shared.State.IsCli) mode = "cli";
        else if (EngineNet.Shared.State.IsGui) mode = "gui";
        else if (EngineNet.Shared.State.IsTui) mode = "tui";
        _LuaWorld.LuaScript.Globals[key: "UIMode"] = mode;

        // Debug state
#if DEBUG
        _LuaWorld.LuaScript.Globals[key: "DEBUG"] = true;
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
        EngineSdkGlobals(_LuaWorld: _LuaWorld);

        // Global path normalization helpers and path join (soft join, host separator aware)
        _LuaWorld.LuaScript.Globals[key: "normalize"] = (System.Func<ScriptExecutionContext, CallbackArguments, DynValue>)((_, args) => NormalizePath(args: args));
        _LuaWorld.LuaScript.Globals[key: "normalise"] = _LuaWorld.LuaScript.Globals[key: "normalize"];
        _LuaWorld.LuaScript.Globals[key: "Normalize"] = _LuaWorld.LuaScript.Globals[key: "normalize"];

        // Global path join (soft join, host separator aware)
        _LuaWorld.LuaScript.Globals[key: "join"] = (System.Func<ScriptExecutionContext, CallbackArguments, DynValue>)((_, args) => JoinPaths(args: args));

        static DynValue NormalizePath(CallbackArguments args) {
            if (args.Count == 0) {
                return DynValue.Nil;
            }

            return NormalizePathValue(args[index: 0]);
        }

        static DynValue NormalizePathValue(DynValue value) {
            if (value.Type == DataType.Nil || value.Type == DataType.Void) {
                return DynValue.Nil;
            }

            string path = value.Type == DataType.String ? value.String : value.ToPrintString();
            if (string.IsNullOrEmpty(path)) {
                return DynValue.NewString(str: string.Empty);
            }

            return DynValue.NewString(str: NormalizePathString(path: path));
        }

        static string NormalizePathString(string path) {
            char separator = System.IO.Path.DirectorySeparatorChar;
            StringBuilder builder = new(capacity: path.Length);
            bool previousWasSeparator = false;

            foreach (char character in path) {
                if (character == '/' || character == '\\') {
                    if (!previousWasSeparator) {
                        builder.Append(separator);
                        previousWasSeparator = true;
                    }
                } else {
                    builder.Append(character);
                    previousWasSeparator = false;
                }
            }

            return builder.ToString();
        }

        static DynValue JoinPaths(CallbackArguments args) {
            char separator = System.IO.Path.DirectorySeparatorChar;
            List<string> parts = Enumerable.Range(start: 0, count: args.Count)
                .Select(selector: i => args[index: i])
                .Select(selector: NormalizePathValue)
                .Where(predicate: v => v.Type == DataType.String && !string.IsNullOrEmpty(v.String))
                .Select(selector: v => v.String)
                .ToList();

            if (parts.Count == 0) {
                return DynValue.NewString(str: string.Empty);
            }

            StringBuilder sb = new();
            for (int i = 0; i < parts.Count; i++) {
                string part = parts[index: i];
                if (i > 0) {
                    part = part.TrimStart(trimChar: separator);
                    if (part.Length == 0) {
                        continue;
                    }

                    if (sb.Length > 0 && sb[^1] != separator) {
                        sb.Append(separator);
                    }
                }

                sb.Append(part);
            }

            return DynValue.NewString(str: sb.ToString());
        }

        // Resolve external tool path
        _LuaWorld.LuaScript.Globals[key: "ResolveToolPath"] = (string id, string? ver) => _tools.ResolveToolPath(toolId: id, version: ver);
        _LuaWorld.LuaScript.Globals[key: "tool"] = _LuaWorld.LuaScript.Globals[key: "ResolveToolPath"]; // alias for convenience

        // Global 'import' function - loads and executes Lua files relative to current script_dir global
        _LuaWorld.LuaScript.Globals[key: "import"] = (System.Func<ScriptExecutionContext, string, DynValue>)((_, path) => {
            // Re-fetch script_dir from globals at runtime to allow dynamic updates
            string currentScriptDir = _LuaWorld.LuaScript.Globals.Get(key: "script_dir").String ?? "";
            string absolutePath = System.IO.Path.IsPathRooted(path: path) ? path : System.IO.Path.Combine(path1: currentScriptDir, path2: path);

            if (!absolutePath.EndsWith(".lua", comparisonType: StringComparison.OrdinalIgnoreCase)) {
                absolutePath += ".lua";
            }

            if (!Security.TryGetAllowedCanonicalPathWithPrompt(path: absolutePath, canonicalPath: out string safePath)) {
                throw new ScriptRuntimeException($"import error: access denied '{absolutePath}'");
            }

            if (!System.IO.File.Exists(path: safePath)) {
                throw new ScriptRuntimeException($"import error: file not found '{safePath}'");
            }

            string previousScriptDir = currentScriptDir;
            string nextScriptDir = System.IO.Path.GetDirectoryName(path: safePath)?.Replace(oldValue: "\\", newValue: "/") ?? "";

            try {
                // Ensure nested imports resolve relative to the currently imported file.
                _LuaWorld.LuaScript.Globals[key: "script_dir"] = nextScriptDir;
                return _LuaWorld.LuaScript.DoFile(filename: safePath);
            } catch (Exception ex) {
                throw new ScriptRuntimeException($"import error in '{safePath}': {ex.Message}");
            } finally {
                _LuaWorld.LuaScript.Globals[key: "script_dir"] = previousScriptDir;
            }
        });

        // Custom 'require' implementation that matches the 'import' behavior
        _LuaWorld.LuaScript.Globals[key: "require"] = _LuaWorld.LuaScript.Globals[key: "import"];
    }

    /// <summary>
    /// Defines the global modules available to Lua scripts,
    /// including the core SDK module and any additional modules for functionality like file system access, process execution, JSON handling, etc.
    /// </summary>
    /// <param name="_LuaWorld"></param>
    /// <param name="_tools"></param>
    /// <param name="_commandService"></param>
    private static void CreateGlobalModules(LuaWorld _LuaWorld, Core.ExternalTools.JsonToolResolver _tools, Core.Services.CommandService.CommandService _commandService) {
        Global.Sdk.CreateSdkModule(_LuaWorld: _LuaWorld, tools: _tools, commandService: _commandService);
        Global.Sqlite.CreateSqliteModule(_LuaWorld: _LuaWorld);
        Global.Progress.CreateProgressModule(_LuaWorld: _LuaWorld);
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
        _LuaWorld.DiagnosticsMethods[key: "Log"] = (System.Action<string>)Shared.IO.Diagnostics.LuaLogger.LuaLog;
        _LuaWorld.DiagnosticsMethods[key: "Trace"] = (System.Action<string>)Shared.IO.Diagnostics.LuaLogger.LuaTrace;
        _LuaWorld.LuaScript.Globals[key: "Diagnostics"] = _LuaWorld.DiagnosticsMethods;

        // Final startup logs
        Shared.IO.Diagnostics.Log($"Set Game_Root to '{_gameRoot}'");
        Shared.IO.Diagnostics.Log($"Set Project_Root to '{_projectRoot}'");
    }

    /// <summary>
    /// Defines methods for emitting engine SDK events (warn, error, prompt) to allow Lua scripts to interact with the user through the engine's UI.
    /// </summary>
    /// <param name="_LuaWorld"></param>
    private static void EngineSdkGlobals(LuaWorld _LuaWorld) {
        // basic outputs for warning and error events
        _LuaWorld.LuaScript.Globals[key: "warn"] = (System.Action<string>)Shared.IO.UI.EngineSdk.Warn;
        _LuaWorld.LuaScript.Globals[key: "error"] = (System.Action<string>)Shared.IO.UI.EngineSdk.Error;

        // overwrite built in Print method, and direct to sdk print
        _LuaWorld.LuaScript.Globals[key: "print"] = DynValue.NewCallback(callBack: (ctx, args) => {
            List<string> parts = new();
            for (int i = 0; i < args.Count; i++) {
                // ToPrintString() safely converts Lua types (nil, tables, etc.) to strings
                parts.Add(item: args[index: i].ToPrintString());
            }

            // Standard Lua print separates multiple arguments with a tab
            Shared.IO.UI.EngineSdk.PrintLine(string.Join(separator: "\t", values: parts));
            return DynValue.Nil;
        });

        // emits the prompt query to the engine/ui and returns the user input
        _LuaWorld.LuaScript.Globals[key: "prompt"] = (System.Func<DynValue, DynValue, DynValue, string>)((message, id, secret) => {
            string msg = message.Type == DataType.String ? message.String : message.ToPrintString();
            string pid = id.Type == DataType.Nil || id.Type == DataType.Void ? "q1" : id.Type == DataType.String ? id.String : id.ToPrintString();
            bool sec = secret.Type == DataType.Boolean && secret.Boolean;
            return Shared.IO.UI.EngineSdk.Prompt(msg, id: pid, secret: sec);
        });
        _LuaWorld.LuaScript.Globals[key: "color_prompt"] = (System.Func<DynValue, DynValue, DynValue, DynValue, string>)((message, color, id, secret) => {
            string msg = message.Type == DataType.String ? message.String : message.ToPrintString();
            string col = color.Type == DataType.String ? color.String : color.ToPrintString();
            string pid = id.Type == DataType.Nil || id.Type == DataType.Void ? "q1" : id.Type == DataType.String ? id.String : id.ToPrintString();
            bool sec = secret.Type == DataType.Boolean && secret.Boolean;
            return Shared.IO.UI.EngineSdk.color_prompt(msg, color: col, id: pid, secret: sec);
        });
        _LuaWorld.LuaScript.Globals[key: "colour_prompt"] = _LuaWorld.LuaScript.Globals[key: "color_prompt"]; // (Correct) AU spelling
    }


}
