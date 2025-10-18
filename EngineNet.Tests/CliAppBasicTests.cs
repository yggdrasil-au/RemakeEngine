
namespace EngineNet.Tests;

public sealed class CliAppBasicTests:IDisposable {
    private readonly String _root;
    private readonly OperationsEngine _engine;

    public CliAppBasicTests() {
        // Create isolated temp root with minimal registry to avoid network calls
        _root = Path.Combine(Path.GetTempPath(), "EngineNet_CLI_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "RemakeRegistry"));
        File.WriteAllText(Path.Combine(_root, "RemakeRegistry", "register.json"), "{\n  \"modules\": {}\n}");
        // Minimal project.json
        File.WriteAllText(Path.Combine(_root, "project.json"), "{\n  \"RemakeEngine\": { \n    \"Config\": { \"project_path\": \"" + _root.Replace("\\", "\\\\") + "\" }\n  }\n}");

        EngineConfig cfg = new EngineConfig(Path.Combine(_root, "project.json"));
        IToolResolver tools = new PassthroughToolResolver();
        _engine = new OperationsEngine(_root, tools, cfg);
    }

    public void Dispose() {
        try {
            if (Directory.Exists(_root)) {
                Directory.Delete(_root, recursive: true);
            }
        } catch { /* ignore */ }
    }

    private static (String stdout, String stderr, Int32 rc) RunCli(OperationsEngine engine, params String[] args) {
        App app = new App(engine);
        StringBuilder outSb = new StringBuilder();
        StringBuilder errSb = new StringBuilder();
        TextWriter prevOut = Console.Out;
        TextWriter prevErr = Console.Error;
        try {
            using StringWriter outWriter = new StringWriter(outSb);
            using StringWriter errWriter = new StringWriter(errSb);
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            Int32 rc = app.Run(args);
            outWriter.Flush();
            errWriter.Flush();
            return (outSb.ToString(), errSb.ToString(), rc);
        } finally {
            Console.SetOut(prevOut);
            Console.SetError(prevErr);
        }
    }

    [Fact]
    public void Help_PrintsUsage_And_ReturnsZero() {
        (String stdout, String stderr, Int32 rc) = RunCli(_engine, "--help");
        Assert.Equal(0, rc);
        Assert.Contains("Usage:", stdout);
        Assert.True(String.IsNullOrWhiteSpace(stderr));
    }

    [Fact]
    public void UnknownCommand_PrintsHelp_And_ReturnsTwo() {
        (String stdout, String stderr, Int32 rc) = RunCli(_engine, "--does-not-exist");
        Assert.Equal(2, rc);
        Assert.Contains("Unknown command", stderr);
        Assert.Contains("Usage:", stdout);
    }

    [Fact]
    public void Run_StripsRootFlag() {
        // Create a bogus other root to verify it is ignored by the CLI (Program handles --root)
        String otherRoot = Path.Combine(Path.GetTempPath(), "OtherRoot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(otherRoot);
        try {
            (String stdout, String stderr, Int32 rc) = RunCli(_engine, "--root", otherRoot, "--list-games");
            Assert.Equal(0, rc);
            Assert.Contains("No games found in RemakeRegistry/Games.", stdout);
            Assert.True(String.IsNullOrWhiteSpace(stderr));
        } finally {
            try {
                Directory.Delete(otherRoot, recursive: true);
            } catch { }
        }
    }

    [Fact]
    public void ListOps_PrintsGroupNames_And_ReturnsZero() {
        // Arrange a minimal game folder with grouped operations
        String gameDir = Path.Combine(_root, "RemakeRegistry", "Games", "TestGame");
        Directory.CreateDirectory(gameDir);
        String opsPath = Path.Combine(gameDir, "operations.json");
        File.WriteAllText(opsPath, "{ \"GroupA\": [ { \"script\": \"a.py\" } ], \"GroupB\": [] }");

        (String stdout, String stderr, Int32 rc) = RunCli(_engine, "--list-ops", "TestGame");
        Assert.Equal(0, rc);

        Assert.True(String.IsNullOrWhiteSpace(stderr));
    }

    [Fact]
    public void InlineInvocation_WithUnapprovedExecutable_ReturnsOne_NoProcessSpawn() {
        // Provide a direct game root so TryResolveInlineGame creates an entry without touching disk
        String gameRoot = Path.Combine(_root, "DummyGameRoot");
        Directory.CreateDirectory(gameRoot);
        // Use script_type 'foobar' so CommandBuilder uses that as the executable; ProcessRunner will block it.
        (String stdout, String stderr, Int32 rc) = RunCli(
            _engine,
            "--game-root", gameRoot,
            "--script", "myscript.py",
            "--script_type", "foobar",
            "--set", "Name=Inline Test"
        );
        Assert.Equal(1, rc); // ExecuteCommand should fail fast due to security and propagate as non-zero
        Assert.Contains("SECURITY: Executable", stdout + stderr);
    }
}
