using MoonSharp.Interpreter;

using EngineNet.ScriptEngines.Global.SdkModule;

namespace EngineNet.ScriptEngines.Lua.Global;

public static partial class Sdk {

    internal static void AddProcessExecution(LuaWorld _LuaWorld, Core.ExternalTools.IToolResolver tools) {

        _LuaWorld.Sdk.Table["exec"] = DynValue.NewCallback((ctx, args) => {
            if (args.Count < 1 || args[0].Type != DataType.Table) {
                throw new ScriptRuntimeException("exec expects first argument to be an array/table of strings (command + args)");
            }

            Table commandArgs = args[0].Table;
            Table? options = args.Count > 1 && args[1].Type == DataType.Table ? args[1].Table : null;

            // Security: Validate command before execution
            List<string> parts = Lua.Globals.Utils.TableToStringList(commandArgs);
            if (parts.Count == 0) {
                throw new ScriptRuntimeException("exec requires at least one argument (executable)");
            }

            if (!EngineNet.ScriptEngines.Security.IsApprovedExecutable(parts[0], tools)) {
                throw new ScriptRuntimeException($"Executable '{parts[0]}' is not in the approved tools list. Use tool() function to resolve approved tools.");
            }

            return ProcessExecution.ExecProcess(_LuaWorld.LuaScript, commandArgs, options, false);
        });

        _LuaWorld.Sdk.Table["execSilent"] = DynValue.NewCallback((ctx, args) => {
            if (args.Count < 1 || args[0].Type != DataType.Table) {
                throw new ScriptRuntimeException("exec expects first argument to be an array/table of strings (command + args)");
            }

            Table commandArgs = args[0].Table;
            Table? options = args.Count > 1 && args[1].Type == DataType.Table ? args[1].Table : null;

            // Security: Validate command before execution
            List<string> parts = Lua.Globals.Utils.TableToStringList(commandArgs);
            if (parts.Count == 0) {
                throw new ScriptRuntimeException("exec requires at least one argument (executable)");
            }

            if (!EngineNet.ScriptEngines.Security.IsApprovedExecutable(parts[0], tools)) {
                throw new ScriptRuntimeException($"Executable '{parts[0]}' is not in the approved tools list. Use tool() function to resolve approved tools.");
            }

            return ProcessExecution.ExecProcess(_LuaWorld.LuaScript, commandArgs, options, true);
        });

        _LuaWorld.Sdk.Table["run_process"] = DynValue.NewCallback((ctx, args) => {
            if (args.Count < 1 || args[0].Type != DataType.Table) {
                throw new ScriptRuntimeException("run_process expects argument table");
            }

            Table commandArgs = args[0].Table;
            Table? options = args.Count > 1 && args[1].Type == DataType.Table ? args[1].Table : null;

            // Security: Validate command before execution
            List<string> parts = Lua.Globals.Utils.TableToStringList(commandArgs);
            if (parts.Count == 0) {
                throw new ScriptRuntimeException("run_process requires at least one argument (executable)");
            }

            if (!EngineNet.ScriptEngines.Security.IsApprovedExecutable(parts[0], tools)) {
                throw new ScriptRuntimeException($"Executable '{parts[0]}' is not in the approved tools list. Use tool() function to resolve approved tools.");
            }

            return ProcessExecution.RunProcess(_LuaWorld.LuaScript, commandArgs, options);
        });

        _LuaWorld.Sdk.Table["spawn_process"] = DynValue.NewCallback((ctx, args) => {
            if (args.Count < 1 || args[0].Type != DataType.Table) {
                throw new ScriptRuntimeException("spawn_process expects a table of command parts");
            }
            Table cmdTable = args[0].Table;
            Table? options = null;
            if (args.Count > 1 && args[1].Type == DataType.Table) {
                options = args[1].Table;
            }
            return ProcessExecution.SpawnProcess(_LuaWorld.LuaScript, cmdTable, options, tools);
        });

        _LuaWorld.Sdk.Table["poll_process"] = DynValue.NewCallback((ctx, args) => {
            if (args.Count < 1 || args[0].Type != DataType.Number) {
                throw new ScriptRuntimeException("poll_process requires a numeric process id");
            }
            int pid = (int)args[0].Number;
            return ProcessExecution.PollProcess(_LuaWorld.LuaScript, pid);
        });

        _LuaWorld.Sdk.Table["wait_process"] = DynValue.NewCallback((ctx, args) => {
            if (args.Count < 1 || args[0].Type != DataType.Number) {
                throw new ScriptRuntimeException("wait_process requires a numeric process id");
            }
            int pid = (int)args[0].Number;
            int? timeoutMs = null;
            if (args.Count > 1 && args[1].Type == DataType.Number) {
                timeoutMs = (int)args[1].Number;
            }
            return ProcessExecution.WaitProcess(_LuaWorld.LuaScript, pid, timeoutMs);
        });

        _LuaWorld.Sdk.Table["close_process"] = DynValue.NewCallback((ctx, args) => {
            if (args.Count < 1 || args[0].Type != DataType.Number) {
                throw new ScriptRuntimeException("close_process requires a numeric process id");
            }
            int pid = (int)args[0].Number;
            return ProcessExecution.CloseProcess(_LuaWorld.LuaScript, pid);
        });

    }
}