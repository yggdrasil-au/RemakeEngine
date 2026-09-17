// lua interpreter
using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua;

using MoonSharp.Interpreter.Debugging;

/// <summary>
/// entry point for executing a Lua script, called from EngineNet.ScriptEngines.Helpers.EmbeddedActionDispatcher
/// </summary>
internal sealed class Main : IScriptAction {

    private readonly string _scriptPath;
    private readonly string[] _args;
    private readonly string _gameRoot;
    private readonly string _projectRoot;

    internal Main(string scriptPath, IEnumerable<string>? args, string gameRoot, string projectRoot) {
        this._scriptPath = scriptPath;
        this._args = args is null ? System.Array.Empty<string>() : args as string[] ?? new List<string>(collection: args).ToArray();
        this._gameRoot = gameRoot;
        this._projectRoot = projectRoot;
    }

    /// <summary>
    /// Returns true when the exception chain contains the known MoonSharp iterator prep NullReferenceException.
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <returns>True when the stack indicates the known ExecIterPrep failure path.</returns>
    private static bool IsMoonSharpIteratorPrepNullReference(Exception exception) {
        Exception? current = exception;
        while (current is not null) {
            if (current is NullReferenceException && current.StackTrace?.Contains("MoonSharp.Interpreter.Execution.VM.Processor.ExecIterPrep", comparisonType: StringComparison.Ordinal) == true) {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }

    //
    public async Task ExecuteAsync(Core.ExternalTools.JsonToolResolver tools, Core.Services.CommandService commandService, CancellationToken cancellationToken = default(CancellationToken)) {
        bool ok = false;
        int exitCode = 0;
        System.Exception? executionError = null;
        LuaWorld? LuaWorld = null;
        try {
            if (!System.IO.File.Exists(path: this._scriptPath)) {
                throw new System.IO.FileNotFoundException("Lua script not found", fileName: this._scriptPath);
            }

            // ::
            // ::

            // read script code
            string code = await System.IO.File.ReadAllTextAsync(path: this._scriptPath, cancellationToken: cancellationToken);
            // create new Lua script environment with default modules, all sandboxing is done manually
            Script LuaScript = new(coreModules: CoreModules.Preset_Default);
            // object to hold all exposed tables
            LuaWorld = new LuaWorld(_luaScript: LuaScript, _scriptPath: this._scriptPath);



            // ::
            // ::

            // Setup safer environment
            SetupEnvironment.LuaEnvironment(_LuaWorld: LuaWorld);

            // Load versions from current game module context
            Dictionary<string,string> moduleVersions = Helper.LoadModuleToolVersions(_gameRoot: _gameRoot);
            ContextualToolResolver contextualTools = new(baseResolver: tools, contextVersions: moduleVersions);

            // Expose core functions, SDK and modules
            LuaAction.CreateGlobals(_LuaWorld: LuaWorld, _tools: contextualTools, _commandService: commandService, _args: this._args, _gameRoot: this._gameRoot, _projectRoot: this._projectRoot, _scriptPath: this._scriptPath);

            // Register UserData types
            UserData.RegisterType<Shared.IO.UI.EngineSdk.PanelProgress>();
            UserData.RegisterType<Shared.IO.UI.EngineSdk.ScriptProgress>();
            UserData.RegisterType<EngineNet.ScriptEngines.Lua.Global.LuaConsoleProgress>();
            UserData.RegisterType<Global.SqliteHandle>();

            // Signal GUI that a script is active so the bottom panel can reflect activity even without progress events
            Shared.IO.UI.EngineSdk.ScriptActiveStart(scriptPath: this._scriptPath);

#if DEBUG
            Shared.IO.UI.EngineSdk.PrintLine($"Running lua script '{this._scriptPath}' with {this._args.Length} args...");
            Shared.IO.UI.EngineSdk.PrintLine($"input args: {string.Join(separator: ", ", this._args)}");
#endif

            // ::
            // ::

            // Create a fresh object array specifically for this call
            object[] argsForLua = new object[this._args.Length];
            Array.Copy(sourceArray: this._args, destinationArray: argsForLua, length: this._args.Length);

            await System.Threading.Tasks.Task.Run(action: () => {
                LuaWorld.LuaScript.Call(function: LuaWorld.LuaScript.LoadString(code: code, codeFriendlyName: this._scriptPath), args: argsForLua);
            }, cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            ok = true;
            exitCode = 0;

        } catch (ScriptExitException exitEx) {
            // Script called os.exit, treat as normal exit without error
            exitCode = exitEx.ExitCode;
            ok = exitEx.ExitCode == 0;
            if (!ok) {
                executionError = new System.InvalidOperationException($"Lua script exited with non-zero code {exitCode}.");
            }
        } catch (MoonSharp.Interpreter.SyntaxErrorException syntaxEx) {
            // Catches parse errors (e.g. missing 'end', unexpected symbols) before the script even runs
            Shared.IO.UI.EngineSdk.PrintLine($"Lua Syntax Error: {syntaxEx.DecoratedMessage}", color: System.ConsoleColor.Red);
            Shared.IO.UI.EngineSdk.PrintLine($"Error: {syntaxEx.Message}", color: System.ConsoleColor.Yellow);

            exitCode = 1;
            executionError = syntaxEx;
        } catch (MoonSharp.Interpreter.ScriptRuntimeException luaEx) {
            // luaEx.DecoratedMessage contains the file path and line number
            string luaErrorMessage = luaEx.DecoratedMessage;

            Shared.IO.UI.EngineSdk.PrintLine($"Lua Runtime Error: {luaErrorMessage}", color: System.ConsoleColor.Red);
            Shared.IO.UI.EngineSdk.PrintLine($"Error: {luaEx.Message}", color: System.ConsoleColor.Yellow);

            // Print the detailed Lua Call Stack cleanly
            if (luaEx.CallStack != null && luaEx.CallStack.Count > 0) {
                Shared.IO.UI.EngineSdk.PrintLine("Lua Stack Trace:", color: System.ConsoleColor.DarkRed);
                foreach (WatchItem frame in luaEx.CallStack) {
                    string functionName = string.IsNullOrEmpty(frame.Name) ? "main chunk" : frame.Name;
                    string location = "[C# / native code]";

                    if (frame.Location != null) {
                        string fileName = this._scriptPath;

                        // Try to get the specific file/chunk name from MoonSharp using SourceIdx
                        if (LuaWorld?.LuaScript != null) {
                            try {
                                SourceCode? source = LuaWorld.LuaScript.GetSourceCode(sourceCodeID: frame.Location.SourceIdx);
                                if (source != null && !string.IsNullOrEmpty(source.Name)) {
                                    fileName = source.Name;
                                }
                            } catch {
                                // Fallback to _scriptPath if lookup fails
                            }
                        }

                        location = $"{fileName}:line {frame.Location.FromLine}";
                    }

                    Shared.IO.UI.EngineSdk.PrintLine($"  at {functionName} in {location}", color: System.ConsoleColor.DarkRed);
                }
            }

            // Mark as failed so the engine actually reports it as a failure
            exitCode = 1;
            executionError = luaEx;
        } catch (Exception ex) {
            Shared.IO.Diagnostics.Bug("Lua script catch triggered: " + ex);

            // 1. Check for the specific VM IndexOutOfRange crash
            if (ex is IndexOutOfRangeException &&
                ex.StackTrace?.Contains("MoonSharp.Interpreter.Execution.VM.Processor.Processing_Loop") == true) {
                string vmErrorMessage =
                    $"CRITICAL: MoonSharp VM Internal Crash (IndexOutOfRangeException) while executing '{this._scriptPath}'. " +
                    "This usually indicates a stack overflow or an infinite metamethod loop.";

                Shared.IO.UI.EngineSdk.PrintLine(vmErrorMessage, color: System.ConsoleColor.Red);
                Shared.IO.Diagnostics.LuaInternalCatch(ex: vmErrorMessage);

                executionError = new InvalidOperationException(vmErrorMessage, innerException: ex);
            } else if (IsMoonSharpIteratorPrepNullReference(exception: ex)) {
                string compatibilityMessage =
                    "Lua compatibility limitation: MoonSharp 'for ... in' iteration failure. Use pairs()/ipairs() instead.";
                executionError = new InvalidOperationException(compatibilityMessage, innerException: ex);
                Shared.IO.UI.EngineSdk.PrintLine(compatibilityMessage, color: System.ConsoleColor.Yellow);
            } else {
                Shared.IO.Diagnostics.LuaInternalCatch(ex: "Lua script threw an unhandled exception: " + ex);
                Shared.IO.UI.EngineSdk.PrintLine($"Unhandled Exception in Lua Provider: {ex.Message}",
                    color: System.ConsoleColor.Red);
                executionError = ex;
            }

            exitCode = 1;
        } finally {
            LuaWorld?.DisposeOpenDisposables();

            Shared.IO.UI.EngineSdk.ScriptActiveEnd(success: ok, exitCode: exitCode);
        }

        if (!ok || exitCode != 0) {
            throw new System.InvalidOperationException(
                $"Lua script failed with exit code {exitCode}: '{this._scriptPath}'",
                innerException: executionError
            );
        }

        if (!ok) {
            throw new System.InvalidOperationException(
                $"Lua script failed with exit code {exitCode}: '{this._scriptPath}'",
                innerException: executionError
            );
        }
    }

}
