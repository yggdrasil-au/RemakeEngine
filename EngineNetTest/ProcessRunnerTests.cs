using EngineNet.Core.Services.CommandService;
using EngineNet.Core.ExternalTools;

namespace EngineNetTest;

/// <summary>Exercises the process boundary, including argument tokens, stream forwarding, and cancellation.</summary>
[TestClass]
public sealed class ProcessRunnerTests {
    private string RootPath = null!;

    /// <summary>Creates an isolated installed-tools lockfile location for each test.</summary>
    [TestInitialize]
    public void Initialize() {
        RootPath = Path.Combine(Path.GetTempPath(), $"remake-process-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootPath);
    }

    /// <summary>Removes the isolated tool tracking data after each test.</summary>
    [TestCleanup]
    public void Cleanup() {
        Directory.Delete(RootPath, recursive: true);
    }

    /// <summary>Missing and untracked commands fail before starting a process or emitting a terminal event.</summary>
    [TestMethod]
    public void Execute_RejectsMissingAndUntrackedExecutables() {
        ProcessRunner Runner = new(new JsonToolResolver(RootPath));
        List<string> Output = new();
        List<Dictionary<string, object?>> Events = new();

        Assert.IsFalse(Runner.Execute([], "Empty", (Line, Stream) => Output.Add($"{Stream}:{Line}"), Events.Add));
        Assert.AreEqual("stderr:Operation 'Empty' has no executable specified. Skipping.", Output[0]);
        Assert.IsFalse(Runner.Execute(["copy.exe"], "Untracked", (Line, Stream) => Output.Add($"{Stream}:{Line}"), Events.Add));
        StringAssert.Contains(Output[1], "not tracked in Tools.installed.json");
        Assert.IsEmpty(Events);
    }

    /// <summary>Names and directory substrings that were previously trusted do not approve untracked commands.</summary>
    [TestMethod]
    [DataRow("python.exe")]
    [DataRow("git")]
    [DataRow("pwsh.exe")]
    [DataRow("Tools/Blender/untracked.exe")]
    [DataRow("NotTools/untracked.exe")]
    [DataRow("Tools/custom-tool.exe")]
    public void Execute_RejectsUntrackedNamesAndPaths(string Executable) {
        List<string> Output = new();
        List<Dictionary<string, object?>> Events = new();
        ProcessRunner Runner = new(new JsonToolResolver(RootPath));

        Assert.IsFalse(Runner.Execute([Executable], "Untracked", (Line, _) => Output.Add(Line), Events.Add));
        Assert.HasCount(1, Output);
        StringAssert.Contains(Output[0], "not tracked in Tools.installed.json");
        Assert.IsEmpty(Events);
    }

    /// <summary>Empty and invalid executable paths are rejected without attempting process startup.</summary>
    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("invalid\0.exe")]
    public void Execute_RejectsInvalidExecutablePaths(string Executable) {
        List<Dictionary<string, object?>> Events = new();
        ProcessRunner Runner = new(new JsonToolResolver(RootPath));

        Assert.IsFalse(Runner.Execute([Executable], "Invalid", onEvent: Events.Add));
        Assert.IsEmpty(Events);
    }

    /// <summary>Arguments reach the child as individual values; both output streams remain raw text.</summary>
    [TestMethod]
    public void Execute_ForwardsTokensStreamsAndNonzeroExitCode() {
        List<(string Stream, string Line)> Output = new();
        List<Dictionary<string, object?>> Events = new();
        string ScriptPath = Path.Combine(Path.GetTempPath(), $"remake-process-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(ScriptPath, "param([string]$Value)\n[Console]::Out.WriteLine($Value)\n[Console]::Error.WriteLine('error-line')\nStart-Sleep -Milliseconds 300\nexit 7");
        bool Success;
        try {
            Success = TrackedRunner(PowerShell()).Execute(
                [PowerShell(), "-NoLogo", "-NoProfile", "-NonInteractive", "-File", ScriptPath, "two words"],
                "Stream test", (Line, Stream) => Output.Add((Stream, Line)), Events.Add
            );
        } finally {
            File.Delete(ScriptPath);
        }

        Assert.IsFalse(Success);
        Assert.Contains(("stdout", "two words"), Output);
        Assert.Contains(("stderr", "error-line"), Output);
        Assert.HasCount(1, Events);
        Assert.AreEqual(false, Events[0]["success"]);
        Assert.AreEqual(7, Events[0]["exit_code"]);
    }

    /// <summary>Cancellation terminates the child and emits the existing unsuccessful terminal event.</summary>
    [TestMethod]
    public void Execute_CancellationReportsExitCode130() {
        using CancellationTokenSource Cancellation = new(TimeSpan.FromSeconds(10));
        List<Dictionary<string, object?>> Events = new();
        bool SawReady = false;
        bool Success = TrackedRunner(PowerShell()).Execute(
            [PowerShell(), "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "[Console]::Out.WriteLine('ready'); Start-Sleep -Seconds 30"],
            "Cancellation test",
            (Line, _) => { if (Line == "ready") { SawReady = true; Cancellation.Cancel(); } },
            Events.Add, cancellationToken: Cancellation.Token
        );

        Assert.IsTrue(SawReady);
        Assert.IsFalse(Success);
        Assert.HasCount(1, Events);
        Assert.AreEqual(false, Events[0]["success"]);
        Assert.AreEqual(130, Events[0]["exit_code"]);
    }

    /// <summary>External SDK-looking lines are forwarded unchanged and never request an stdin response.</summary>
    [TestMethod]
    public void Execute_PreservesRawEventLookingOutput() {
        const string Marker = "@@SDK@@{\"event\":\"prompt\",\"message\":\"question\"}";
        List<string> Output = new();
        List<Dictionary<string, object?>> Events = new();
        int PromptCalls = 0;
        bool Success = TrackedRunner(PowerShell()).Execute(
            [PowerShell(), "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", $"[Console]::Out.WriteLine('{Marker}'); Start-Sleep -Milliseconds 300"],
            "Raw output test", (Line, _) => Output.Add(Line), Events.Add,
            () => { PromptCalls++; return "answer"; }
        );

        Assert.IsTrue(Success);
        Assert.Contains(Marker, Output);
        Assert.AreEqual(0, PromptCalls);
        Assert.HasCount(1, Events);
        Assert.AreEqual(0, Events[0]["exit_code"]);
    }

    /// <summary>Tracked tools, including formerly blocked names, reach startup and retain start-failure events.</summary>
    [TestMethod]
    [DataRow("python.exe")]
    [DataRow("copy.exe")]
    [DataRow("7z.exe")]
    [DataRow("custom-tool.exe")]
    public void Execute_MissingTrackedExecutableReportsStartFailure(string FileName) {
        List<Dictionary<string, object?>> Events = new();
        string Missing = Path.Combine(RootPath, "installation", FileName);

        Assert.IsFalse(TrackedRunner(Missing).Execute([Missing], "Missing executable", onEvent: Events.Add));
        Assert.HasCount(1, Events);
        Assert.AreEqual("Win32Exception", Events[0]["kind"]);
    }

    /// <summary>Records the test executable in a real lockfile and gives the runner its resolver.</summary>
    private ProcessRunner TrackedRunner(string Executable) {
        File.WriteAllText(Path.Combine(RootPath, "Tools.installed.json"), System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, object> {
                ["test-tool"] = new Dictionary<string, object> {
                    ["1.0"] = new { version = "1.0", exe = Executable }
                }
            }
        ));
        return new ProcessRunner(new JsonToolResolver(RootPath));
    }

    private static string PowerShell() {
        string Executable = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
        foreach (string DirectoryPath in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
            string Candidate = Path.Combine(DirectoryPath, Executable);
            if (File.Exists(Candidate)) {
                return Candidate;
            }
        }
        Assert.Inconclusive("PowerShell 7 is required for child-process integration tests.");
        return Executable;
    }
}
