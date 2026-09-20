using MoonSharp.Interpreter;

using EngineNet.ScriptEngines.Global.SdkModule;

namespace EngineNet.ScriptEngines.Lua.Global;

internal static partial class Sdk {

    internal static void AddProcessExecution(LuaWorld _LuaWorld, Core.ExternalTools.JsonToolResolver tools, Core.Services.CommandService.CommandService cs) {

        _LuaWorld.Sdk.Table[key: "exec"] = DynValue.NewCallback(callBack: (ctx, args) => {
            if (args.Count < 1 || args[index: 0].Type != DataType.Table) {
                throw new ScriptRuntimeException("exec expects first argument to be an array/table of strings (command + args)");
            }

            Table commandArgs = args[index: 0].Table;
            Table? options = args.Count > 1 && args[index: 1].Type == DataType.Table ? args[index: 1].Table : null;

            // Security: Validate command before execution
            List<string> parts = Lua.Globals.Utils.TableToStringList(t: commandArgs);
            if (parts.Count == 0) {
                throw new ScriptRuntimeException("exec requires at least one argument (executable)");
            }

            if (!EngineNet.ScriptEngines.Security.IsApprovedExecutable(executable: parts[index: 0], tools: tools)) {
                throw new ScriptRuntimeException($"Executable '{parts[index: 0]}' is not in the approved tools list. Use tool() function to resolve approved tools.");
            }

            return ProcessExecution.ExecProcess(lua: _LuaWorld.LuaScript, cs: cs, commandArgs: commandArgs, options: options, silentRun: false);
        });

        _LuaWorld.Sdk.Table[key: "execSilent"] = DynValue.NewCallback(callBack: (ctx, args) => {
            if (args.Count < 1 || args[index: 0].Type != DataType.Table) {
                throw new ScriptRuntimeException("exec expects first argument to be an array/table of strings (command + args)");
            }

            Table commandArgs = args[index: 0].Table;
            Table? options = args.Count > 1 && args[index: 1].Type == DataType.Table ? args[index: 1].Table : null;

            // Security: Validate command before execution
            List<string> parts = Lua.Globals.Utils.TableToStringList(t: commandArgs);
            if (parts.Count == 0) {
                throw new ScriptRuntimeException("exec requires at least one argument (executable)");
            }

            if (!EngineNet.ScriptEngines.Security.IsApprovedExecutable(executable: parts[index: 0], tools: tools)) {
                throw new ScriptRuntimeException($"Executable '{parts[index: 0]}' is not in the approved tools list. Use tool() function to resolve approved tools.");
            }

            return ProcessExecution.ExecProcess(lua: _LuaWorld.LuaScript, cs: cs, commandArgs: commandArgs, options: options, silentRun: true);
        });

        _LuaWorld.Sdk.Table[key: "run_process"] = DynValue.NewCallback(callBack: (ctx, args) => {
            if (args.Count < 1 || args[index: 0].Type != DataType.Table) {
                throw new ScriptRuntimeException("run_process expects argument table");
            }

            Table commandArgs = args[index: 0].Table;
            Table? options = args.Count > 1 && args[index: 1].Type == DataType.Table ? args[index: 1].Table : null;

            // Security: Validate command before execution
            List<string> parts = Lua.Globals.Utils.TableToStringList(t: commandArgs);
            if (parts.Count == 0) {
                throw new ScriptRuntimeException("run_process requires at least one argument (executable)");
            }

            if (!EngineNet.ScriptEngines.Security.IsApprovedExecutable(executable: parts[index: 0], tools: tools)) {
                throw new ScriptRuntimeException($"Executable '{parts[index: 0]}' is not in the approved tools list. Use tool() function to resolve approved tools.");
            }

            return ProcessExecution.RunProcess(lua: _LuaWorld.LuaScript, cs: cs, commandArgs: commandArgs, options: options);
        });

        _LuaWorld.Sdk.Table[key: "spawn_process"] = DynValue.NewCallback(callBack: (ctx, args) => {
            if (args.Count < 1 || args[index: 0].Type != DataType.Table) {
                throw new ScriptRuntimeException("spawn_process expects a table of command parts");
            }
            Table cmdTable = args[index: 0].Table;
            Table? options = null;
            if (args.Count > 1 && args[index: 1].Type == DataType.Table) {
                options = args[index: 1].Table;
            }

            // Security: Validate command before execution
            List<string> parts = Lua.Globals.Utils.TableToStringList(t: cmdTable);
            if (parts.Count == 0) {
                throw new ScriptRuntimeException("spawn_process requires at least one argument (executable)");
            }

            if (!EngineNet.ScriptEngines.Security.IsApprovedExecutable(executable: parts[index: 0], tools: tools)) {
                throw new ScriptRuntimeException($"Executable '{parts[index: 0]}' is not in the approved tools list. Use tool() function to resolve approved tools.");
            }

            return ProcessExecution.SpawnProcess(lua: _LuaWorld.LuaScript, cs: cs, commandArgs: cmdTable, options: options, tools: tools);
        });

        _LuaWorld.Sdk.Table[key: "poll_process"] = DynValue.NewCallback(callBack: (ctx, args) => {
            if (args.Count < 1 || args[index: 0].Type != DataType.Number) {
                throw new ScriptRuntimeException("poll_process requires a numeric process id");
            }
            int pid = (int)args[index: 0].Number;
            return ProcessExecution.PollProcess(lua: _LuaWorld.LuaScript, cs: cs, pid: pid);
        });

        _LuaWorld.Sdk.Table[key: "wait_process"] = DynValue.NewCallback(callBack: (ctx, args) => {
            if (args.Count < 1 || args[index: 0].Type != DataType.Number) {
                throw new ScriptRuntimeException("wait_process requires a numeric process id");
            }
            int pid = (int)args[index: 0].Number;
            int? timeoutMs = null;
            if (args.Count > 1 && args[index: 1].Type == DataType.Number) {
                timeoutMs = (int)args[index: 1].Number;
            }
            return ProcessExecution.WaitProcess(lua: _LuaWorld.LuaScript, cs: cs, pid: pid, timeoutMs: timeoutMs);
        });

        _LuaWorld.Sdk.Table[key: "close_process"] = DynValue.NewCallback(callBack: (ctx, args) => {
            if (args.Count < 1 || args[index: 0].Type != DataType.Number) {
                throw new ScriptRuntimeException("close_process requires a numeric process id");
            }
            int pid = (int)args[index: 0].Number;
            return ProcessExecution.CloseProcess(lua: _LuaWorld.LuaScript, cs: cs, pid: pid);
        });

    }
}
