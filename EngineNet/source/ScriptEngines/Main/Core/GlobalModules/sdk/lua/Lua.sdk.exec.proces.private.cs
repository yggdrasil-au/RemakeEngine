using MoonSharp.Interpreter;
using EngineNet.ScriptEngines.Global.SdkModule;

namespace EngineNet.ScriptEngines.Lua.Global;

internal static partial class Sdk {
    /// <summary>Registers Lua process callbacks, decoding Lua inputs and encoding plain process results.</summary>
    /// <param name="_LuaWorld">Lua execution context receiving the SDK callbacks.</param>
    /// <param name="tools">Resolver that approves executable paths in the shared process utility.</param>
    /// <param name="cs">Service used by the shared utility to execute and manage processes.</param>
    internal static void AddProcessExecution(LuaWorld _LuaWorld, IJsonToolResolver tools, ICommandService cs) {
        // Both streaming variants share the same Lua contract and differ only in output forwarding.
        DynValue Exec(CallbackArguments Arguments, bool SilentRun) {
            return InvokeProcess(() => {
                List<string> CommandParts = ReadProcessCommand(Arguments, "exec");
                ProcessExecution.Options Settings = ReadProcessOptions(Arguments);
                ProcessResult Result = ProcessExecution.ExecProcess(cs, tools, CommandParts, Settings, SilentRun);
                return ToProcessResult(_LuaWorld.LuaScript, Result);
            });
        }

        _LuaWorld.Sdk.Table["exec"] = DynValue.NewCallback((Context, Arguments) => Exec(Arguments, SilentRun: false));
        _LuaWorld.Sdk.Table["execSilent"] = DynValue.NewCallback((Context, Arguments) => Exec(Arguments, SilentRun: true));
        _LuaWorld.Sdk.Table["run_process"] = DynValue.NewCallback((Context, Arguments) => InvokeProcess(() => {
            List<string> CommandParts = ReadProcessCommand(Arguments, "run_process");
            ProcessExecution.Options Settings = ReadProcessOptions(Arguments, IncludeTimeout: true);
            ProcessResult Result = ProcessExecution.RunProcess(cs, tools, CommandParts, Settings);
            return ToProcessResult(_LuaWorld.LuaScript, Result, Settings.CaptureStdout, Settings.CaptureStderr);
        }));
        _LuaWorld.Sdk.Table["spawn_process"] = DynValue.NewCallback((Context, Arguments) => InvokeProcess(() => {
            List<string> CommandParts = ReadProcessCommand(Arguments, "spawn_process");
            ProcessExecution.Options Settings = ReadProcessOptions(Arguments);
            int Pid = ProcessExecution.SpawnProcess(cs, tools, CommandParts, Settings);
            return DynValue.NewTable(new Table(_LuaWorld.LuaScript) { ["pid"] = Pid });
        }));
        _LuaWorld.Sdk.Table["poll_process"] = DynValue.NewCallback((Context, Arguments) => InvokeProcess(() => {
            int Pid = ReadProcessId(Arguments, "poll_process");
            return ToProcessStatus(_LuaWorld.LuaScript, ProcessExecution.PollProcess(cs, Pid));
        }));
        _LuaWorld.Sdk.Table["wait_process"] = DynValue.NewCallback((Context, Arguments) => InvokeProcess(() => {
            int Pid = ReadProcessId(Arguments, "wait_process");
            int? TimeoutMs = Arguments.Count > 1 && Arguments[1].Type == DataType.Number ? (int)Arguments[1].Number : null;
            return ToProcessStatus(_LuaWorld.LuaScript, ProcessExecution.WaitProcess(cs, Pid, TimeoutMs));
        }));
        _LuaWorld.Sdk.Table["close_process"] = DynValue.NewCallback((Context, Arguments) => {
            int Pid = ReadProcessId(Arguments, "close_process");
            return DynValue.NewBoolean(ProcessExecution.CloseProcess(cs, Pid));
        });
    }

    /* :: :: Lua Input Conversion :: START :: */

    /// <summary>Checks a Lua command table and converts it to executable and argument tokens.</summary>
    private static List<string> ReadProcessCommand(CallbackArguments Arguments, string Operation) {
        if (Arguments.Count < 1 || Arguments[0].Type != DataType.Table) {
            string Message = Operation switch {
                "exec" => "exec expects first argument to be an array/table of strings (command + args)",
                "run_process" => "run_process expects argument table",
                _ => "spawn_process expects a table of command parts"
            };
            throw new ScriptRuntimeException(Message);
        }
        List<string> CommandParts = Lua.Globals.Utils.TableToStringList(t: Arguments[0].Table);
        if (CommandParts.Count == 0) {
            throw new ScriptRuntimeException($"{Operation} requires at least one argument (executable)");
        }
        return CommandParts;
    }

    /// <summary>Decodes correctly typed Lua options, preserving defaults and ignoring unrelated table entries.</summary>
    /// <param name="Arguments">Callback arguments whose optional second value is the options table.</param>
    /// <param name="IncludeTimeout">Enables timeout decoding only for captured execution.</param>
    /// <returns>Language-independent process settings.</returns>
    private static ProcessExecution.Options ReadProcessOptions(CallbackArguments Arguments, bool IncludeTimeout = false) {
        if (Arguments.Count < 2 || Arguments[1].Type != DataType.Table) {
            return new ProcessExecution.Options();
        }
        Table Options = Arguments[1].Table;
        Dictionary<string, string> Environment = new();
        DynValue EnvironmentValue = Options.Get("env");
        if (EnvironmentValue.Type == DataType.Table) {
            foreach (TablePair Entry in EnvironmentValue.Table.Pairs) {
                if (Entry.Key.Type == DataType.String && Entry.Value.Type == DataType.String) {
                    Environment[Entry.Key.String] = Entry.Value.String;
                }
            }
        }
        DynValue WorkingDirectory = Options.Get("cwd");
        DynValue Timeout = Options.Get("timeout_ms");
        return new ProcessExecution.Options {
            WorkingDirectory = WorkingDirectory.Type == DataType.String ? WorkingDirectory.String : null,
            Environment = Environment,
            CaptureStdout = ReadProcessBoolean(Options, "capture_stdout", DefaultValue: true),
            CaptureStderr = ReadProcessBoolean(Options, "capture_stderr", DefaultValue: true),
            NewTerminal = ReadProcessBoolean(Options, "new_terminal", DefaultValue: false),
            KeepOpen = ReadProcessBoolean(Options, "keep_open", DefaultValue: false),
            Wait = ReadProcessBoolean(Options, "wait", DefaultValue: true),
            TimeoutMs = IncludeTimeout && Timeout.Type == DataType.Number ? (int)Math.Max(0, Timeout.Number) : null
        };
    }

    /// <summary>Reads a boolean option without treating other Lua value types as booleans.</summary>
    private static bool ReadProcessBoolean(Table Options, string Key, bool DefaultValue) {
        DynValue Value = Options.Get(Key);
        return Value.Type == DataType.Boolean ? Value.Boolean : DefaultValue;
    }

    /// <summary>Validates and converts a numeric Lua process ID for poll, wait, and close callbacks.</summary>
    private static int ReadProcessId(CallbackArguments Arguments, string Operation) {
        if (Arguments.Count < 1 || Arguments[0].Type != DataType.Number) {
            throw new ScriptRuntimeException($"{Operation} requires a numeric process id");
        }
        return (int)Arguments[0].Number;
    }

    /* :: :: Lua Input Conversion :: END :: */
    // //
    /* :: :: Lua Result Conversion :: START :: */

    /// <summary>Builds the existing Lua execution result, omitting output keys when capture was not requested.</summary>
    private static DynValue ToProcessResult(Script Script, ProcessResult Result, bool CaptureStdout = false, bool CaptureStderr = false) {
        Table Value = new(Script) {
            ["exit_code"] = Result.ExitCode,
            ["success"] = Result.Success
        };
        if (CaptureStdout) {
            Value["stdout"] = Result.Stdout;
        }
        if (CaptureStderr) {
            Value["stderr"] = Result.Stderr;
        }
        return DynValue.NewTable(Value);
    }

    /// <summary>Builds a Lua status snapshot; running processes omit the exit-code key.</summary>
    private static DynValue ToProcessStatus(Script Script, ProcessPollResult Result) {
        Table Value = new(Script) {
            ["running"] = Result.Running,
            ["stdout"] = Result.StdoutFull,
            ["stderr"] = Result.StderrFull,
            ["stdout_delta"] = Result.StdoutDelta,
            ["stderr_delta"] = Result.StderrDelta
        };
        if (!Result.Running) {
            Value["exit_code"] = Result.ExitCode;
        }
        return DynValue.NewTable(Value);
    }

    /// <summary>Translates shared utility and command-service failures into Lua runtime errors.</summary>
    private static DynValue InvokeProcess(Func<DynValue> Action) {
        try {
            return Action();
        } catch (Exception Error) when (Error is not ScriptRuntimeException) {
            Shared.IO.Diagnostics.Bug($"[Lua.sdk.exec.proces.private.cs::InvokeProcess()]:: catch: {Error.Message}");
            throw new ScriptRuntimeException(Error.Message);
        }
    }

    /* :: :: Lua Result Conversion :: END :: */
}
