using System.Reflection;
using EngineNet.Core.Abstractions;
using EngineNet.ScriptEngines;
using MoonSharp.Interpreter;

namespace EngineNetTest;

/// <summary>Verifies process contracts across the Lua adapter and the language-independent execution utility.</summary>
[TestClass]
public sealed class ScriptProcessExecutionTests {
    private const BindingFlags InternalInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string Executable = Path.Combine(Directory.GetCurrentDirectory(), "process-test.exe");

    /// <summary>A C# consumer can execute with plain options and receive a native process result.</summary>
    [TestMethod]
    public void SharedExecution_AcceptsClrInputsAndReturnsNativeResult() {
        RecordingCommandService Service = new();
        Type Utility = typeof(ScriptActionDispatcher).Assembly.GetType("EngineNet.ScriptEngines.Global.SdkModule.ProcessExecution", throwOnError: true)!;
        object Options = Activator.CreateInstance(Utility.GetNestedType("Options", BindingFlags.NonPublic)!, nonPublic: true)!;
        object? Result = Utility.GetMethod("RunProcess", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(
            null, [Service, new TrackedTools(), new List<string> { Executable, "two words" }, Options]
        );

        Assert.AreSame(Service.Result, Result);
        CollectionAssert.AreEqual(new[] { "two words" }, Service.Arguments);
        Assert.IsTrue(Service.CaptureStdout);
        Assert.IsTrue(Service.CaptureStderr);
    }

    /// <summary>Lua capture defaults, optional output fields, option filtering, and timeout clamping remain stable.</summary>
    [TestMethod]
    public void RunProcess_PreservesLuaOptionsAndResultShape() {
        RecordingCommandService Service = new();
        Script Lua = CreateLua(Service);
        Table Default = Lua.DoString("return sdk.run_process({executable, 'two words'})").Table;
        Assert.IsTrue(Default.Get("success").Boolean);
        Assert.AreEqual("output", Default.Get("stdout").String);
        Assert.AreEqual("errors", Default.Get("stderr").String);
        Assert.IsNull(Service.TimeoutMs);

        Table Result = Lua.DoString("""
            return sdk.run_process({executable, 'two words'}, {
                cwd = working_directory, capture_stdout = false, capture_stderr = 'ignored', timeout_ms = -10,
                env = { TOKEN = 'value', ignored = false, [1] = 'ignored' }
            })
            """).Table;
        Assert.IsTrue(Result.Get("stdout").IsNil());
        Assert.AreEqual("errors", Result.Get("stderr").String);
        Assert.AreEqual(0, Service.TimeoutMs);
        Assert.AreEqual(Directory.GetCurrentDirectory(), Service.WorkingDirectory);
        Assert.IsFalse(Service.CaptureStdout);
        Assert.IsTrue(Service.CaptureStderr);
        Assert.HasCount(1, Service.Environment!);
        Assert.AreEqual("value", Service.Environment!["TOKEN"]);
        CollectionAssert.AreEqual(new[] { "two words" }, Service.Arguments);
    }

    /// <summary>Streaming callbacks preserve the completion-event and missing-event exit-code rules.</summary>
    [TestMethod]
    [DataRow("exec", true, 0, true, 0)]
    [DataRow("execSilent", false, 7, false, 7)]
    [DataRow("execSilent", true, null, true, 0)]
    [DataRow("exec", false, null, false, 1)]
    public void Exec_PreservesCompletionResult(string Function, bool Success, int? EventExitCode, bool ExpectedSuccess, int ExpectedCode) {
        RecordingCommandService Service = new() { ExecuteSuccess = Success, EventExitCode = EventExitCode };
        Script Lua = CreateLua(Service);
        Table Result = Lua.DoString($"return sdk.{Function}({{executable, 'two words'}}, {{cwd = working_directory, env = {{TOKEN = 'value'}}}})").Table;

        Assert.AreEqual(ExpectedSuccess, Result.Get("success").Boolean);
        Assert.AreEqual(ExpectedCode, (int)Result.Get("exit_code").Number);
        Assert.IsTrue(Result.Get("stdout").IsNil());
        Assert.IsTrue(Result.Get("stderr").IsNil());
        Assert.AreEqual("value", Service.ExecutionEnvironment!["TOKEN"]);
        Assert.AreEqual(Directory.GetCurrentDirectory(), Service.ExecutionEnvironment["PWD"]);
        CollectionAssert.AreEqual(new[] { Executable, "two words" }, Service.Arguments);
    }

    /// <summary>New-terminal flags are forwarded without opening a real terminal in the test.</summary>
    [TestMethod]
    public void Exec_ForwardsNewTerminalOptions() {
        if (!OperatingSystem.IsWindows()) {
            Assert.Inconclusive("This check uses the Windows terminal path; Unix-like systems may select the emulator fallback.");
        }
        RecordingCommandService Service = new();
        Script Lua = CreateLua(Service);
        Table Result = Lua.DoString("return sdk.exec({executable}, {new_terminal = true, keep_open = true, wait = false})").Table;

        Assert.IsTrue(Service.UsedNewTerminal);
        Assert.IsTrue(Service.KeepOpen);
        Assert.IsFalse(Service.Wait);
        Assert.IsTrue(Result.Get("success").Boolean);
        Assert.IsTrue(Result.Get("stdout").IsNil());
    }

    /// <summary>Background results retain IDs, status fields, delta output, and the nonblocking wait contract.</summary>
    [TestMethod]
    public void BackgroundProcess_PreservesLifecycleAndStatusShape() {
        RecordingCommandService Service = new();
        Script Lua = CreateLua(Service);
        Assert.AreEqual(42, (int)Lua.DoString("return sdk.spawn_process({executable}, {capture_stderr = false}).pid").Number);
        Assert.IsFalse(Service.CaptureStderr);
        Table Running = Lua.DoString("return sdk.poll_process(42)").Table;
        Assert.IsTrue(Running.Get("running").Boolean);
        Assert.IsTrue(Running.Get("exit_code").IsNil());
        Assert.AreEqual("all-output", Running.Get("stdout").String);
        Assert.AreEqual("all-errors", Running.Get("stderr").String);
        Assert.AreEqual("new-output", Running.Get("stdout_delta").String);
        Assert.AreEqual("new-errors", Running.Get("stderr_delta").String);

        Service.Status = new ProcessPollResult { Running = false, ExitCode = 7 };
        Table Finished = Lua.DoString("return sdk.wait_process(42, 5000)").Table;
        Assert.IsFalse(Finished.Get("running").Boolean);
        Assert.AreEqual(7, (int)Finished.Get("exit_code").Number);
        Assert.AreEqual(2, Service.PollCalls);
        Assert.IsTrue(Lua.DoString("return sdk.close_process(42)").Boolean);
        Assert.AreEqual(42, Service.ClosedPid);
    }

    /// <summary>Lua callers can catch validation, shared security, and command-service failures with pcall.</summary>
    [TestMethod]
    [DataRow("sdk.exec('wrong')", "expects first argument")]
    [DataRow("sdk.run_process({})", "requires at least one argument")]
    [DataRow("sdk.spawn_process({'untracked.exe'})", "not in the approved tools list")]
    [DataRow("sdk.poll_process('wrong')", "requires a numeric process id")]
    [DataRow("sdk.exec({executable, forbidden_path})", "Restricted path detected")]
    [DataRow("sdk.run_process({executable}, {cwd = forbidden_path})", "Restricted path detected")]
    public void ProcessFailures_AreCatchableLuaErrors(string Expression, string ExpectedMessage) {
        RecordingCommandService Service = new();
        Script Lua = CreateLua(Service);
        Lua.Globals["forbidden_path"] = Path.Combine(Path.GetPathRoot(Directory.GetCurrentDirectory())!, "outside-process-workspace");
        DynValue Result = Lua.DoString($"local ok, err = pcall(function() return {Expression} end); return ok, tostring(err)");

        Assert.IsFalse(Result.Tuple[0].Boolean);
        StringAssert.Contains(Result.Tuple[1].String, ExpectedMessage);
        Assert.IsEmpty(Service.Arguments);
    }

    /// <summary>Errors from the plain C# utility are translated at the Lua boundary.</summary>
    [TestMethod]
    public void CommandServiceFailure_IsCatchableLuaError() {
        Script Lua = CreateLua(new RecordingCommandService { FailRun = true });
        DynValue Result = Lua.DoString("local ok, err = pcall(sdk.run_process, {executable}); return ok, tostring(err)");
        Assert.IsFalse(Result.Tuple[0].Boolean);
        StringAssert.Contains(Result.Tuple[1].String, "process service failure");
    }

    /// <summary>Registers only process callbacks through the existing internal Lua registration entry point.</summary>
    private static Script CreateLua(RecordingCommandService Service) {
        Script Lua = new();
        Assembly Scripts = typeof(ScriptActionDispatcher).Assembly;
        Type WorldType = Scripts.GetType("EngineNet.ScriptEngines.Lua.LuaWorld", throwOnError: true)!;
        object World = Activator.CreateInstance(WorldType, InternalInstance, null, [Lua, "process-test.lua"], null)!;
        object Sdk = WorldType.GetProperty("Sdk", InternalInstance)!.GetValue(World)!;
        Lua.Globals["sdk"] = (Table)Sdk.GetType().GetProperty("Table", InternalInstance)!.GetValue(Sdk)!;
        Lua.Globals["executable"] = Executable;
        Lua.Globals["working_directory"] = Directory.GetCurrentDirectory();
        Scripts.GetType("EngineNet.ScriptEngines.Lua.Global.Sdk", throwOnError: true)!
            .GetMethod("AddProcessExecution", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [World, new TrackedTools(), Service]);
        return Lua;
    }

    /// <summary>Approves only the fixture executable, without relying on the developer's installed tools.</summary>
    private sealed class TrackedTools : IJsonToolResolver {
        public bool IsTrackedTool(string executablePath) => executablePath == Executable;
        public string ResolveToolPath(string toolId, string? version = null) => Executable;
        public (string? exe, string? version) ResolveExeAndVersion(string toolId) => (Executable, "test");
    }

    /// <summary>Records process requests and supplies deterministic results without starting child processes.</summary>
    private sealed class RecordingCommandService : ICommandService {
        internal ProcessResult Result { get; } = new() { Success = true, ExitCode = 0, Stdout = "output", Stderr = "errors" };
        internal ProcessPollResult Status { get; set; } = new() {
            Running = true, StdoutFull = "all-output", StderrFull = "all-errors", StdoutDelta = "new-output", StderrDelta = "new-errors"
        };
        internal string[] Arguments { get; private set; } = [];
        internal string? WorkingDirectory { get; private set; }
        internal IDictionary<string, string>? Environment { get; private set; }
        internal IDictionary<string, object?>? ExecutionEnvironment { get; private set; }
        internal int? TimeoutMs { get; private set; }
        internal bool CaptureStdout { get; private set; }
        internal bool CaptureStderr { get; private set; }
        internal bool ExecuteSuccess { get; init; } = true;
        internal int? EventExitCode { get; init; } = 0;
        internal bool UsedNewTerminal { get; private set; }
        internal bool KeepOpen { get; private set; }
        internal bool Wait { get; private set; }
        internal bool FailRun { get; init; }
        internal int PollCalls { get; private set; }
        internal int? ClosedPid { get; private set; }

        public ProcessResult RunProcess(string executable, IEnumerable<string> args, string? cwd, IDictionary<string, string>? env, int? timeoutMs, bool captureStdout, bool captureStderr) {
            if (FailRun) {
                throw new InvalidOperationException("process service failure");
            }
            Arguments = args.ToArray();
            WorkingDirectory = cwd;
            Environment = env;
            TimeoutMs = timeoutMs;
            CaptureStdout = captureStdout;
            CaptureStderr = captureStderr;
            return Result;
        }

        public bool ExecuteCommand(IList<string> commandParts, string title, IProcessRunner.OutputHandler? onOutput = null, IProcessRunner.EventHandler? onEvent = null, IProcessRunner.StdinProvider? stdinProvider = null, IDictionary<string, object?>? envOverrides = null, CancellationToken cancellationToken = default) {
            Arguments = commandParts.ToArray();
            ExecutionEnvironment = envOverrides;
            if (EventExitCode.HasValue) {
                onEvent?.Invoke(new Dictionary<string, object?> { ["event"] = "end", ["exit_code"] = EventExitCode.Value });
            }
            return ExecuteSuccess;
        }

        public int SpawnProcess(string executable, IEnumerable<string> args, string? cwd, IDictionary<string, string>? env, bool captureStdout, bool captureStderr) {
            _ = RunProcess(executable, args, cwd, env, null, captureStdout, captureStderr);
            return 42;
        }

        public ProcessPollResult PollProcess(int pid) {
            Assert.AreEqual(42, pid);
            PollCalls++;
            return Status;
        }

        public bool CloseProcess(int pid) {
            ClosedPid = pid;
            return true;
        }

        public ProcessResult RunInNewTerminal(string executable, IEnumerable<string> args, string? cwd, IDictionary<string, string>? env, bool keepOpen, bool wait) {
            UsedNewTerminal = true;
            KeepOpen = keepOpen;
            Wait = wait;
            return RunProcess(executable, args, cwd, env, null, false, false);
        }

        public ProcessPollResult WaitProcess(int pid, int? timeoutMs) => throw new AssertFailedException("wait_process must retain poll semantics.");
        public List<string> BuildCommand(string currentGame, EngineNet.Core.Data.GameModules games, IDictionary<string, object?> engineData, IDictionary<string, object?> op, EngineNet.Core.Data.PromptAnswers promptAnswers) => throw new NotSupportedException();
        public bool LaunchDetached(string executable, IEnumerable<string> args, string? cwd, DetachedLaunchOptions options) => throw new NotSupportedException();
        public void OpenFolder(string path) => throw new NotSupportedException();
    }
}
