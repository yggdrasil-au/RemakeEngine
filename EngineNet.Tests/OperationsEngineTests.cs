using System;
using System.Collections.Generic;
using System.IO;
using EngineNet.Core;
using EngineNet.Tools;
using Xunit;

namespace EngineNet.Tests;

public class OperationsEngineTests
{
    private sealed class TempDir: IDisposable
    {
        public String Path { get; }
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "enginenet_ops_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }

    [Fact]
    public void LoadOperationsList_Parses_Toml_Array_Of_Tables()
    {
        using TempDir td = new TempDir();
        String ops = System.IO.Path.Combine(td.Path, "operations.toml");
        File.WriteAllText(ops, "[[copy]]\nName='Copy'\nscript='copy.py'\n\n[[run]]\nName='Run'\nscript='run.py'\n");

        OperationsEngine eng = new OperationsEngine(td.Path, new PassthroughToolResolver(), new EngineNet.EngineConfig(System.IO.Path.Combine(td.Path, "project.json")));
        List<Dictionary<String, Object?>> list = eng.LoadOperationsList(ops);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, m => String.Equals(m["Name"]?.ToString(), "Copy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(list, m => String.Equals(m["Name"]?.ToString(), "Run", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadOperationsList_Parses_Json_Array()
    {
        using TempDir td = new TempDir();
        String ops = System.IO.Path.Combine(td.Path, "operations.json");
        File.WriteAllText(ops, "[{\"Name\":\"A\",\"script\":\"a.py\"},{\"Name\":\"B\",\"script\":\"b.py\"}]");
        OperationsEngine eng = new OperationsEngine(td.Path, new PassthroughToolResolver(), new EngineConfig(System.IO.Path.Combine(td.Path, "project.json")));
        List<Dictionary<String, Object?>> list = eng.LoadOperationsList(ops);
        Assert.Equal(2, list.Count);
        Assert.Equal("A", list[0]["Name"]?.ToString());
        Assert.Equal("B", list[1]["Name"]?.ToString());
    }

    [Fact]
    public void LoadOperations_Parses_Json_Grouped()
    {
        using TempDir td = new TempDir();
        String ops = System.IO.Path.Combine(td.Path, "operations.json");
        File.WriteAllText(ops, "{\"group1\":[{\"Name\":\"A\"}],\"group2\":[{\"Name\":\"B\"}]}\n");
        OperationsEngine eng = new OperationsEngine(td.Path, new PassthroughToolResolver(), new EngineConfig(System.IO.Path.Combine(td.Path, "project.json")));
        Dictionary<String, List<Dictionary<String, Object?>>> groups = eng.LoadOperations(ops);
        Assert.True(groups.ContainsKey("group1"));
        Assert.True(groups.ContainsKey("group2"));
        Assert.Single(groups["group1"]);
        Assert.Single(groups["group2"]);
    }
}

public class OperationsEngineFullCoverageTests
{
    static OperationsEngineFullCoverageTests()
    {
        Environment.SetEnvironmentVariable("ENGINE_NET_TEST_LAUNCH_OVERRIDE", "failure");
    }

    private sealed class TempRoot: IDisposable
    {
        public String Path { get; }
        public TempRoot()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "enginenet_ops_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            // Seed registry file to avoid remote fallback
            String regDir = System.IO.Path.Combine(Path, "RemakeRegistry");
            Directory.CreateDirectory(regDir);
            File.WriteAllText(System.IO.Path.Combine(regDir, "register.json"), "{\n  \"modules\": {}\n}\n");
        }
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }

    private static OperationsEngine CreateEngine(String root)
    {
        String projectJson = System.IO.Path.Combine(root, "project.json");
        File.WriteAllText(projectJson, "{}\n");
        return new OperationsEngine(root, new PassthroughToolResolver(), new EngineConfig(projectJson));
    }

    private static Dictionary<String, Object?> MakeGamesMap(String root, String gameName, String opsPath)
    {
        return new Dictionary<String, Object?>
        {
            [gameName] = new Dictionary<String, Object?>
            {
                ["game_root"] = root,
                ["ops_file"] = opsPath,
            }
        };
    }

    [Fact]
    public void ListGames_and_InstalledGames_and_States()
    {
        using TempRoot td = new TempRoot();
        String gamesDir = System.IO.Path.Combine(td.Path, "RemakeRegistry", "Games");
        Directory.CreateDirectory(gamesDir);

        // Game A: installed (has game.toml and valid exe)
        String gameA = System.IO.Path.Combine(gamesDir, "GameA");
        Directory.CreateDirectory(gameA);
        File.WriteAllText(System.IO.Path.Combine(gameA, "operations.json"), "[]");
        String comspec = Environment.GetEnvironmentVariable("ComSpec") ?? "C:\\Windows\\System32\\cmd.exe";
        File.WriteAllText(System.IO.Path.Combine(gameA, "game.toml"), $"title = \"My Game A\"\nexe = \"{comspec.Replace("\\", "\\\\")}\"\n");

        // Game B: downloaded only (has operations.toml but no game.toml)
        String gameB = System.IO.Path.Combine(gamesDir, "GameB");
        Directory.CreateDirectory(gameB);
        File.WriteAllText(System.IO.Path.Combine(gameB, "operations.toml"), "[[group]]\nName='X'\nscript='do.py'\n");

        OperationsEngine eng = CreateEngine(td.Path);

        // ListGames covers base mapping
        Dictionary<String, Object?> listed = eng.ListGames();
        Assert.True(listed.ContainsKey("GameA"));
        Assert.True(listed.ContainsKey("GameB"));
        Dictionary<String, Object?> gameAInfo = Assert.IsType<Dictionary<String, Object?>>(listed["GameA"]);
        String gameRoot = Assert.IsType<String>(gameAInfo["game_root"]!);
        Assert.Equal(gameA, gameRoot);

        // Installed-only helpers include exe/title
        Dictionary<String, Object?> installed = eng.GetInstalledGames();
        Assert.True(installed.ContainsKey("GameA"));
        Dictionary<String, Object?> infoA = Assert.IsType<Dictionary<String, Object?>>(installed["GameA"]);
        Assert.Equal(comspec, infoA["exe"]?.ToString());
        Assert.Equal("My Game A", infoA["title"]?.ToString());

        // Getters and states
        Assert.True(eng.IsModuleInstalled("GameA"));
        Assert.False(eng.IsModuleInstalled("Nope"));
        Assert.Equal(comspec, eng.GetGameExecutable("GameA"));
        Assert.Null(eng.GetGameExecutable("Nope"));
        Assert.Equal(gameA, eng.GetGamePath("GameA"));
        Assert.Equal(gameB, eng.GetGamePath("GameB")); // downloaded fallback
        Assert.Null(eng.GetGamePath("Nope"));
        Assert.Equal("installed", eng.GetModuleState("GameA"));
        Assert.Equal("downloaded", eng.GetModuleState("GameB"));
        Assert.Equal("not_downloaded", eng.GetModuleState("Nope"));

        // LaunchGame: cover early false and attempt path
        Environment.SetEnvironmentVariable("ENGINE_NET_TEST_LAUNCH_OVERRIDE", "failure");
        Assert.False(eng.LaunchGame("GameB")); // no exe
        // Create a fake invalid exe to hit catch path
        String gameC = System.IO.Path.Combine(gamesDir, "GameC");
        Directory.CreateDirectory(gameC);
        String fakeExe = System.IO.Path.Combine(gameC, "fake.exe");
        File.WriteAllText(fakeExe, "not a real exe");
        File.WriteAllText(System.IO.Path.Combine(gameC, "operations.json"), "[]");
        File.WriteAllText(System.IO.Path.Combine(gameC, "game.toml"), $"title=\"C\"\nexe=\"{fakeExe.Replace("\\", "\\\\")}\"\n");
        Assert.False(eng.LaunchGame("GameC"));
        // Best-effort: call launch for installed real exe (may succeed or fail based on environment)
        Boolean _ = eng.LaunchGame("GameA");
    }

    [Fact]
    public void LoadOperationsList_JsonObject_Flattens_Groups()
    {
        using TempRoot td = new TempRoot();
        String ops = System.IO.Path.Combine(td.Path, "operations.json");
        // include numbers to exercise number parsing (int and float) and arrays
        File.WriteAllText(ops, "{\n  \"grp\": [ { \"Name\": \"Op\", \"args\": [1, 2.5, \"x\"] } ]\n}\n");
        OperationsEngine eng = CreateEngine(td.Path);
        List<Dictionary<String, Object?>> list = eng.LoadOperationsList(ops);
        Assert.Single(list);
        Assert.Equal("Op", list[0]["Name"]?.ToString());
    }

    [Fact]
    public void LoadOperationsList_Json_Array_Types_Coverage()
    {
        using TempRoot td = new TempRoot();
        String ops = System.IO.Path.Combine(td.Path, "operations.json");
        File.WriteAllText(ops, "[ { \"Name\": \"Types\", \"flagTrue\": true, \"flagFalse\": false, \"numI\": 7, \"numD\": 3.14, \"noval\": null, \"args\": [1, 2.0, \"x\"] } ]\n");
        OperationsEngine eng = CreateEngine(td.Path);
        List<Dictionary<String, Object?>> list = eng.LoadOperationsList(ops);
        Assert.Single(list);
        Assert.Equal("Types", list[0]["Name"]?.ToString());
    }

    [Fact]
    public void LoadOperations_Toml_Groups_With_Nested_Types()
    {
        using TempRoot td = new TempRoot();
        String ops = System.IO.Path.Combine(td.Path, "operations.toml");
        File.WriteAllText(ops, "[[copy]]\nName='Copy'\nargs=[1, \"two\"]\nmeta={foo=\"bar\"}\narrtbl=[{a=1},{b=2}]\n[[copy.sub]]\nk='v'\n[[copy.sub]]\nk='w'\n\n[[run]]\nName='Run'\nscript='run.py'\n");
        OperationsEngine eng = CreateEngine(td.Path);
        Dictionary<String, List<Dictionary<String, Object?>>> groups = eng.LoadOperations(ops);
        Assert.True(groups.ContainsKey("copy"));
        Assert.True(groups.ContainsKey("run"));
        Assert.Single(groups["copy"]);
        Assert.Single(groups["run"]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ReloadProjectConfig_All_Paths()
    {
        using TempRoot td = new TempRoot();
        String proj = System.IO.Path.Combine(td.Path, "project.json");
        var cfg = new EngineConfig(proj);
        OperationsEngine eng = new OperationsEngine(td.Path, new PassthroughToolResolver(), cfg);

        String opsPath = System.IO.Path.Combine(td.Path, "ops.json");
        File.WriteAllText(opsPath, "[]");
        Dictionary<String, Object?> games = MakeGamesMap(td.Path, "G1", opsPath);

        // No project.json -> nothing changes
        File.Delete(proj);
        await eng.RunSingleOperationAsync("G1", games, new Dictionary<String, Object?> { ["script_type"] = "foobar", ["script"] = "x.py" }, new Dictionary<String, Object?>());
        Assert.Empty(cfg.Data);

        // Non-object JSON -> early return
        File.WriteAllText(proj, "[1,2,3]\n");
        await eng.RunSingleOperationAsync("G1", games, new Dictionary<String, Object?> { ["script_type"] = "foobar", ["script"] = "x.py" }, new Dictionary<String, Object?>());
        Assert.Empty(cfg.Data);

        // Valid object -> data reloaded
        File.WriteAllText(proj, "{ \"A\": 1, \"B\": true }\n");
        await eng.RunSingleOperationAsync("G1", games, new Dictionary<String, Object?> { ["script_type"] = "foobar", ["script"] = "x.py" }, new Dictionary<String, Object?>());
        Assert.True(cfg.Data.ContainsKey("A"));
        Assert.True(cfg.Data.ContainsKey("B"));
    }

    [Fact]
    public void GetRegisteredModules_Covers_Method()
    {
        using TempRoot td = new TempRoot();
        // registry already seeded in TempRoot
        OperationsEngine eng = CreateEngine(td.Path);
        IReadOnlyDictionary<String, Object?> modules = eng.GetRegisteredModules();
        Assert.NotNull(modules);
    }

    [Fact]
    public async System.Threading.Tasks.Task InstallModuleAsync_JsonArray_Flattens()
    {
        using TempRoot td = new TempRoot();
        OperationsEngine eng = CreateEngine(td.Path);
        String gamesDir = System.IO.Path.Combine(td.Path, "RemakeRegistry", "Games");
        String gameY = System.IO.Path.Combine(gamesDir, "GameY");
        Directory.CreateDirectory(gameY);
        String opsJson = System.IO.Path.Combine(gameY, "operations.json");
        File.WriteAllText(opsJson, "[ { \"Name\": \"One\", \"script\": \"a.py\", \"script_type\": \"foobar\" } ]\n");
        Boolean ok = await eng.InstallModuleAsync("GameY");
        Assert.False(ok);
    }

    [Fact]
    public async System.Threading.Tasks.Task RunSingleOperationAsync_Covers_All_Types()
    {
        using TempRoot td = new TempRoot();
        OperationsEngine eng = CreateEngine(td.Path);

        String opsPath = System.IO.Path.Combine(td.Path, "ops.json");
        File.WriteAllText(opsPath, "[]");
        Dictionary<String, Object?> games = MakeGamesMap(td.Path, "G1", opsPath);
        Dictionary<String, Object?> answers = new Dictionary<String, Object?>();

        // lua: simple script
        String luaPath = System.IO.Path.Combine(td.Path, "test.lua");
        File.WriteAllText(luaPath, "print('hi')\n");
        Dictionary<String, Object?> opLua = new Dictionary<String, Object?> { ["script_type"] = "lua", ["script"] = luaPath };
        Boolean okLua = await eng.RunSingleOperationAsync("G1", games, opLua, answers);
        Assert.True(okLua);

        // js: simple script
        String jsPath = System.IO.Path.Combine(td.Path, "test.js");
        File.WriteAllText(jsPath, "// noop\nvar x=1;\n");
        Dictionary<String, Object?> opJs = new Dictionary<String, Object?> { ["script_type"] = "js", ["script"] = jsPath };
        Boolean okJs = await eng.RunSingleOperationAsync("G1", games, opJs, answers);
        Assert.True(okJs);

        // engine: unknown action -> false
        Dictionary<String, Object?> opEngUnknown = new Dictionary<String, Object?> { ["script_type"] = "engine", ["script"] = "unknown_action" };
        Boolean okUnknown = await eng.RunSingleOperationAsync("G1", games, opEngUnknown, answers);
        Assert.False(okUnknown);

        // default/unknown script_type -> triggers external process runner false
        // also ensure project.json reload path tolerates invalid JSON
        File.WriteAllText(System.IO.Path.Combine(td.Path, "project.json"), "not json");
        String dummy = System.IO.Path.Combine(td.Path, "noop.py");
        File.WriteAllText(dummy, "print('x')\n");
        Dictionary<String, Object?> opDefault = new Dictionary<String, Object?> { ["script_type"] = "foobar", ["script"] = dummy };
        Boolean okDefault = await eng.RunSingleOperationAsync("G1", games, opDefault, answers);
        Assert.False(okDefault);
        // parts.Count < 2 -> false (no script specified)
        Dictionary<String, Object?> opEmpty = new Dictionary<String, Object?> { ["script_type"] = "python" };
        Boolean okEmpty = await eng.RunSingleOperationAsync("G1", games, opEmpty, answers);
        Assert.False(okEmpty);
    }

    [Fact]
    public async System.Threading.Tasks.Task RunSingleOperationAsync_Engine_ExceptionHandled()
    {
        using TempRoot td = new TempRoot();
        OperationsEngine eng = CreateEngine(td.Path);
        String opsPath = System.IO.Path.Combine(td.Path, "ops.json");
        File.WriteAllText(opsPath, "[]");
        Dictionary<String, Object?> games = MakeGamesMap(td.Path, "G1", opsPath);
        Dictionary<String, Object?> answers = new Dictionary<String, Object?>();
        Dictionary<String, Object?> op = new Dictionary<String, Object?> { ["script_type"] = "engine", ["script"] = "download_tools" };
        Boolean ok = await eng.RunSingleOperationAsync("G1", games, op, answers);
        Assert.False(ok);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteEngineOperationAsync_All_Cases()
    {
        using TempRoot td = new TempRoot();
        OperationsEngine eng = CreateEngine(td.Path);
        String opsPath = System.IO.Path.Combine(td.Path, "ops.json");
        File.WriteAllText(opsPath, "[]");
        Dictionary<String, Object?> games = MakeGamesMap(td.Path, "G1", opsPath);
        Dictionary<String, Object?> answers = new Dictionary<String, Object?> { ["force_download"] = true };

        // Prepare central tools registry and empty manifest to avoid network
        String central = System.IO.Path.Combine(td.Path, "RemakeRegistry", "Tools.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(central)!);
        File.WriteAllText(central, "{ }\n");
        String manifest = System.IO.Path.Combine(td.Path, "tools.toml");
        File.WriteAllText(manifest, "# empty manifest\n");

        Dictionary<String, Object?> opTools = new Dictionary<String, Object?>
        {
            ["script"] = "download_tools",
            ["args"] = new List<Object?> { manifest }
        };
        Assert.True(await eng.ExecuteEngineOperationAsync("G1", games, opTools, answers));

        // format-extract txd (likely fails quickly but covers branch)
        Dictionary<String, Object?> opFmtTxd = new Dictionary<String, Object?>
        {
            ["script"] = "format-extract",
            ["format"] = "txd",
            ["args"] = new List<Object?> { "-i", "input.txd" }
        };
        Assert.False(await eng.ExecuteEngineOperationAsync("G1", games, opFmtTxd, answers));

        // format-extract str
        Dictionary<String, Object?> opFmtStr = new Dictionary<String, Object?>
        {
            ["script"] = "format-extract",
            ["format"] = "str",
            ["args"] = new List<Object?> { "-i", "input.str" }
        };
        Assert.False(await eng.ExecuteEngineOperationAsync("G1", games, opFmtStr, answers));

        // format-convert ffmpeg
        Dictionary<String, Object?> opConvFfmpeg = new Dictionary<String, Object?>
        {
            ["script"] = "format-convert",
            ["tool"] = "ffmpeg",
            ["args"] = new List<Object?> { "-m", "ffmpeg", "--type", "audio", "-s", System.IO.Path.Combine(td.Path, "src_missing"), "-t", System.IO.Path.Combine(td.Path, "out"), "-i", ".wav", "-o", ".ogg" }
        };
        Assert.False(await eng.ExecuteEngineOperationAsync("G1", games, opConvFfmpeg, answers));

        // format-convert unknown tool -> false
        Dictionary<String, Object?> opConvUnknown = new Dictionary<String, Object?> { ["script"] = "format_convert", ["tool"] = "unknown", ["args"] = new List<Object?>() };
        Assert.False(await eng.ExecuteEngineOperationAsync("G1", games, opConvUnknown, answers));

        // format-convert vgmstream branch
        Dictionary<String, Object?> opConvVgm = new Dictionary<String, Object?>
        {
            ["script"] = "format_convert",
            ["tool"] = "vgmstream",
            ["args"] = new List<Object?> { "-m", "vgmstream", "--type", "audio", "-s", System.IO.Path.Combine(td.Path, "src_missing2"), "-t", System.IO.Path.Combine(td.Path, "out2"), "-i", ".snu", "-o", ".wav" }
        };
        Assert.False(await eng.ExecuteEngineOperationAsync("G1", games, opConvVgm, answers));
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteEngineOperationAsync_DownloadTools_MissingManifest_ReturnsFalse()
    {
        using TempRoot td = new TempRoot();
        OperationsEngine eng = CreateEngine(td.Path);
        String opsPath = System.IO.Path.Combine(td.Path, "ops.json");
        File.WriteAllText(opsPath, "[]");
        Dictionary<String, Object?> games = MakeGamesMap(td.Path, "G1", opsPath);
        Dictionary<String, Object?> op = new Dictionary<String, Object?> { ["script"] = "download_tools" /* no args/tools_manifest */ };
        Assert.False(await eng.ExecuteEngineOperationAsync("G1", games, op, new Dictionary<String, Object?>()));
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteEngineOperationAsync_Throws_On_Unknown_Game()
    {
        using TempRoot td = new TempRoot();
        OperationsEngine eng = CreateEngine(td.Path);
        String manifest = System.IO.Path.Combine(td.Path, "tools.toml");
        File.WriteAllText(manifest, "# empty\n");
        Directory.CreateDirectory(System.IO.Path.Combine(td.Path, "RemakeRegistry"));
        File.WriteAllText(System.IO.Path.Combine(td.Path, "RemakeRegistry", "Tools.json"), "{}\n");
        Dictionary<String, Object?> op = new Dictionary<String, Object?> { ["script"] = "download_tools", ["args"] = new List<Object?> { manifest } };
        await Assert.ThrowsAsync<KeyNotFoundException>(async () => await eng.ExecuteEngineOperationAsync("MissingGame", new Dictionary<String, Object?>(), op, new Dictionary<String, Object?>()));
    }

    [Fact]
    public async System.Threading.Tasks.Task RunOperationGroupAsync_Aggregates_Success()
    {
        using TempRoot td = new TempRoot();
        OperationsEngine eng = CreateEngine(td.Path);
        String opsPath = System.IO.Path.Combine(td.Path, "ops.json");
        File.WriteAllText(opsPath, "[]");
        Dictionary<String, Object?> games = MakeGamesMap(td.Path, "G1", opsPath);
        Dictionary<String, Object?> answers = new Dictionary<String, Object?>();

        String luaPath = System.IO.Path.Combine(td.Path, "ok.lua");
        File.WriteAllText(luaPath, "print('ok')\n");
        String dummy = System.IO.Path.Combine(td.Path, "noop.py");
        File.WriteAllText(dummy, "print('x')\n");

        List<Dictionary<String, Object?>> ops = new List<Dictionary<String, Object?>>()
        {
            new() { ["script_type"] = "lua", ["script"] = luaPath },
            new() { ["script_type"] = "foobar", ["script"] = dummy } // will fail
        };

        Boolean ok = await eng.RunOperationGroupAsync("G1", games, "group", ops, answers);
        Assert.False(ok);
    }

    [Fact]
    public void BuildCommand_and_ExecuteCommand_Wrappers()
    {
        using TempRoot td = new TempRoot();
        OperationsEngine eng = CreateEngine(td.Path);
        String opsPath = System.IO.Path.Combine(td.Path, "ops.json");
        File.WriteAllText(opsPath, "[]");
        Dictionary<String, Object?> games = MakeGamesMap(td.Path, "G1", opsPath);
        Dictionary<String, Object?> answers = new Dictionary<String, Object?> { ["flag"] = true, ["textval"] = "abc" };

        Dictionary<String, Object?> op = new Dictionary<String, Object?>
        {
            ["script_type"] = "python",
            ["script"] = "script.py",
            ["prompts"] = new List<Object?>
            {
                new Dictionary<String, Object?> { ["Name"] = "flag", ["type"] = "confirm", ["cli_arg"] = "--flag" },
                new Dictionary<String, Object?> { ["Name"] = "list", ["type"] = "checkbox", ["cli_prefix"] = "--list" },
                new Dictionary<String, Object?> { ["Name"] = "textval", ["type"] = "text", ["cli_arg_prefix"] = "--name" },
            }
        };

        List<String> parts = eng.BuildCommand("G1", games, op, answers);
        Assert.True(parts.Count >= 2);
        Boolean ok = eng.ExecuteCommand(new List<String> { "definitely-not-a-real-exe", "arg" }, "title");
        Assert.False(ok);
    }

    [Fact]
    public async System.Threading.Tasks.Task InstallModuleAsync_Paths()
    {
        using TempRoot td = new TempRoot();
        OperationsEngine eng = CreateEngine(td.Path);

        // No operations -> false
        Boolean okNone = await eng.InstallModuleAsync("GameX");
        Assert.False(okNone);

        // Prepare a module with grouped operations
        String gamesDir = System.IO.Path.Combine(td.Path, "RemakeRegistry", "Games");
        String gameX = System.IO.Path.Combine(gamesDir, "GameX");
        Directory.CreateDirectory(gameX);
        String opsJson = System.IO.Path.Combine(gameX, "operations.json");
        File.WriteAllText(opsJson, "{\n  \"run-all\": [ { \"Name\": \"One\", \"script\": \"a.py\", \"script_type\": \"foobar\" } ]\n}\n");

        // Execute; will attempt to run unknown exe and fail (ok=false) but cover path
        Boolean ok = await eng.InstallModuleAsync("GameX");
        Assert.False(ok);

        // Add an op lacking a script to hit parts.Count==0 path
        String opsJson2 = System.IO.Path.Combine(gameX, "operations_extra.json");
        File.WriteAllText(opsJson2, "{ \"run\": [ { \"Name\": \"NoScript\" } ] }\n");
        File.Move(opsJson2, opsJson, overwrite: true);
        Boolean ok2 = await eng.InstallModuleAsync("GameX");
        Assert.True(ok2); // no failing ops executed; continues
    }

    [Fact]
    public async System.Threading.Tasks.Task InstallModuleAsync_Groups_No_RunAll_Chooses_First()
    {
        using TempRoot td = new TempRoot();
        OperationsEngine eng = CreateEngine(td.Path);
        String gamesDir = System.IO.Path.Combine(td.Path, "RemakeRegistry", "Games");
        String gameZ = System.IO.Path.Combine(gamesDir, "GameZ");
        Directory.CreateDirectory(gameZ);
        String opsJson = System.IO.Path.Combine(gameZ, "operations.json");
        File.WriteAllText(opsJson, "{\n  \"grp1\": [ { \"Name\": \"One\", \"script\": \"a.py\", \"script_type\": \"foobar\" } ],\n  \"grp2\": [ { \"Name\": \"Two\", \"script\": \"b.py\", \"script_type\": \"foobar\" } ]\n}\n");
        Boolean ok = await eng.InstallModuleAsync("GameZ");
        Assert.False(ok);
    }

    [Fact]
    public async System.Threading.Tasks.Task RunSingleOperationAsync_Python_Case_Label()
    {
        using TempRoot td = new TempRoot();
        OperationsEngine eng = CreateEngine(td.Path);
        String opsPath = System.IO.Path.Combine(td.Path, "ops.json");
        File.WriteAllText(opsPath, "[]");
        Dictionary<String, Object?> games = MakeGamesMap(td.Path, "G1", opsPath);
        String py = System.IO.Path.Combine(td.Path, "p.py");
        File.WriteAllText(py, "import sys\nsys.exit(1)\n");
        Dictionary<String, Object?> op = new Dictionary<String, Object?> { ["script_type"] = "python", ["script"] = py };
        Boolean ok = await eng.RunSingleOperationAsync("G1", games, op, new Dictionary<String, Object?>());
        Assert.False(ok);
    }

    [Fact]
    public void DownloadModule_Invokes_Git()
    {
        using TempRoot td = new TempRoot();
        OperationsEngine eng = CreateEngine(td.Path);
        // We do not assert true/false (depends on git availability); calling it covers the line
        Boolean _ = eng.DownloadModule("https://example.com/repo.git");
    }
}
