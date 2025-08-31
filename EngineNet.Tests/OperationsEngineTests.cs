using System;
using System.Collections.Generic;
using System.IO;
using RemakeEngine.Core;
using RemakeEngine.Tools;
using Xunit;

namespace EngineNet.Tests;

public class OperationsEngineTests
{
    private sealed class TempDir: IDisposable
    {
        public string Path { get; }
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
        using var td = new TempDir();
        var ops = System.IO.Path.Combine(td.Path, "operations.toml");
        File.WriteAllText(ops, "[[copy]]\nName='Copy'\nscript='copy.py'\n\n[[run]]\nName='Run'\nscript='run.py'\n");

        var eng = new OperationsEngine(td.Path, new PassthroughToolResolver(), new EngineConfig(System.IO.Path.Combine(td.Path, "project.json")));
        var list = eng.LoadOperationsList(ops);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, m => string.Equals(m["Name"]?.ToString(), "Copy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(list, m => string.Equals(m["Name"]?.ToString(), "Run", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadOperationsList_Parses_Json_Array()
    {
        using var td = new TempDir();
        var ops = System.IO.Path.Combine(td.Path, "operations.json");
        File.WriteAllText(ops, "[{\"Name\":\"A\",\"script\":\"a.py\"},{\"Name\":\"B\",\"script\":\"b.py\"}]");
        var eng = new OperationsEngine(td.Path, new PassthroughToolResolver(), new EngineConfig(System.IO.Path.Combine(td.Path, "project.json")));
        var list = eng.LoadOperationsList(ops);
        Assert.Equal(2, list.Count);
        Assert.Equal("A", list[0]["Name"]?.ToString());
        Assert.Equal("B", list[1]["Name"]?.ToString());
    }

    [Fact]
    public void LoadOperations_Parses_Json_Grouped()
    {
        using var td = new TempDir();
        var ops = System.IO.Path.Combine(td.Path, "operations.json");
        File.WriteAllText(ops, "{\"group1\":[{\"Name\":\"A\"}],\"group2\":[{\"Name\":\"B\"}]}\n");
        var eng = new OperationsEngine(td.Path, new PassthroughToolResolver(), new EngineConfig(System.IO.Path.Combine(td.Path, "project.json")));
        var groups = eng.LoadOperations(ops);
        Assert.True(groups.ContainsKey("group1"));
        Assert.True(groups.ContainsKey("group2"));
        Assert.Single(groups["group1"]);
        Assert.Single(groups["group2"]);
    }
}

public class OperationsEngineFullCoverageTests
{
    private sealed class TempRoot: IDisposable
    {
        public string Path { get; }
        public TempRoot()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "enginenet_ops_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            // Seed registry file to avoid remote fallback
            var regDir = System.IO.Path.Combine(Path, "RemakeRegistry");
            Directory.CreateDirectory(regDir);
            File.WriteAllText(System.IO.Path.Combine(regDir, "register.json"), "{\n  \"modules\": {}\n}\n");
        }
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }

    private static OperationsEngine CreateEngine(string root)
    {
        var projectJson = System.IO.Path.Combine(root, "project.json");
        File.WriteAllText(projectJson, "{}\n");
        return new OperationsEngine(root, new PassthroughToolResolver(), new EngineConfig(projectJson));
    }

    private static Dictionary<string, object?> MakeGamesMap(string root, string gameName, string opsPath)
    {
        return new Dictionary<string, object?>
        {
            [gameName] = new Dictionary<string, object?>
            {
                ["game_root"] = root,
                ["ops_file"] = opsPath,
            }
        };
    }

    [Fact]
    public void ListGames_and_InstalledGames_and_States()
    {
        using var td = new TempRoot();
        var gamesDir = System.IO.Path.Combine(td.Path, "RemakeRegistry", "Games");
        Directory.CreateDirectory(gamesDir);

        // Game A: installed (has game.toml and valid exe)
        var gameA = System.IO.Path.Combine(gamesDir, "GameA");
        Directory.CreateDirectory(gameA);
        File.WriteAllText(System.IO.Path.Combine(gameA, "operations.json"), "[]");
        var comspec = Environment.GetEnvironmentVariable("ComSpec") ?? "C:\\Windows\\System32\\cmd.exe";
        File.WriteAllText(System.IO.Path.Combine(gameA, "game.toml"), $"title = \"My Game A\"\nexe = \"{comspec.Replace("\\", "\\\\")}\"\n");

        // Game B: downloaded only (has operations.toml but no game.toml)
        var gameB = System.IO.Path.Combine(gamesDir, "GameB");
        Directory.CreateDirectory(gameB);
        File.WriteAllText(System.IO.Path.Combine(gameB, "operations.toml"), "[[group]]\nName='X'\nscript='do.py'\n");

        var eng = CreateEngine(td.Path);

        // ListGames covers base mapping
        var listed = eng.ListGames();
        Assert.True(listed.ContainsKey("GameA"));
        Assert.True(listed.ContainsKey("GameB"));
        Assert.Equal(gameA, ((Dictionary<string, object?>)listed["GameA"]) ["game_root"]?.ToString());

        // Installed-only helpers include exe/title
        var installed = eng.GetInstalledGames();
        Assert.True(installed.ContainsKey("GameA"));
        var infoA = (Dictionary<string, object?>)installed["GameA"];
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
        Assert.False(eng.LaunchGame("GameB")); // no exe
        // Create a fake invalid exe to hit catch path
        var gameC = System.IO.Path.Combine(gamesDir, "GameC");
        Directory.CreateDirectory(gameC);
        var fakeExe = System.IO.Path.Combine(gameC, "fake.exe");
        File.WriteAllText(fakeExe, "not a real exe");
        File.WriteAllText(System.IO.Path.Combine(gameC, "operations.json"), "[]");
        File.WriteAllText(System.IO.Path.Combine(gameC, "game.toml"), $"title=\"C\"\nexe=\"{fakeExe.Replace("\\", "\\\\")}\"\n");
        Assert.False(eng.LaunchGame("GameC"));
        // Best-effort: call launch for installed real exe (may succeed or fail based on environment)
        var _ = eng.LaunchGame("GameA");
    }

    [Fact]
    public void LoadOperationsList_JsonObject_Flattens_Groups()
    {
        using var td = new TempRoot();
        var ops = System.IO.Path.Combine(td.Path, "operations.json");
        // include numbers to exercise number parsing (int and float) and arrays
        File.WriteAllText(ops, "{\n  \"grp\": [ { \"Name\": \"Op\", \"args\": [1, 2.5, \"x\"] } ]\n}\n");
        var eng = CreateEngine(td.Path);
        var list = eng.LoadOperationsList(ops);
        Assert.Single(list);
        Assert.Equal("Op", list[0]["Name"]?.ToString());
    }

    [Fact]
    public void LoadOperationsList_Json_Array_Types_Coverage()
    {
        using var td = new TempRoot();
        var ops = System.IO.Path.Combine(td.Path, "operations.json");
        File.WriteAllText(ops, "[ { \"Name\": \"Types\", \"flagTrue\": true, \"flagFalse\": false, \"numI\": 7, \"numD\": 3.14, \"noval\": null, \"args\": [1, 2.0, \"x\"] } ]\n");
        var eng = CreateEngine(td.Path);
        var list = eng.LoadOperationsList(ops);
        Assert.Single(list);
        Assert.Equal("Types", list[0]["Name"]?.ToString());
    }

    [Fact]
    public void LoadOperations_Toml_Groups_With_Nested_Types()
    {
        using var td = new TempRoot();
        var ops = System.IO.Path.Combine(td.Path, "operations.toml");
        File.WriteAllText(ops, "[[copy]]\nName='Copy'\nargs=[1, \"two\"]\nmeta={foo=\"bar\"}\narrtbl=[{a=1},{b=2}]\n[[copy.sub]]\nk='v'\n[[copy.sub]]\nk='w'\n\n[[run]]\nName='Run'\nscript='run.py'\n");
        var eng = CreateEngine(td.Path);
        var groups = eng.LoadOperations(ops);
        Assert.True(groups.ContainsKey("copy"));
        Assert.True(groups.ContainsKey("run"));
        Assert.Single(groups["copy"]);
        Assert.Single(groups["run"]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ReloadProjectConfig_All_Paths()
    {
        using var td = new TempRoot();
        var proj = System.IO.Path.Combine(td.Path, "project.json");
        var cfg = new EngineConfig(proj);
        var eng = new OperationsEngine(td.Path, new PassthroughToolResolver(), cfg);

        var opsPath = System.IO.Path.Combine(td.Path, "ops.json");
        File.WriteAllText(opsPath, "[]");
        var games = MakeGamesMap(td.Path, "G1", opsPath);

        // No project.json -> nothing changes
        File.Delete(proj);
        await eng.RunSingleOperationAsync("G1", games, new Dictionary<string, object?> { ["script_type"] = "foobar", ["script"] = "x.py" }, new Dictionary<string, object?>());
        Assert.Empty(cfg.Data);

        // Non-object JSON -> early return
        File.WriteAllText(proj, "[1,2,3]\n");
        await eng.RunSingleOperationAsync("G1", games, new Dictionary<string, object?> { ["script_type"] = "foobar", ["script"] = "x.py" }, new Dictionary<string, object?>());
        Assert.Empty(cfg.Data);

        // Valid object -> data reloaded
        File.WriteAllText(proj, "{ \"A\": 1, \"B\": true }\n");
        await eng.RunSingleOperationAsync("G1", games, new Dictionary<string, object?> { ["script_type"] = "foobar", ["script"] = "x.py" }, new Dictionary<string, object?>());
        Assert.True(cfg.Data.ContainsKey("A"));
        Assert.True(cfg.Data.ContainsKey("B"));
    }

    [Fact]
    public void GetRegisteredModules_Covers_Method()
    {
        using var td = new TempRoot();
        // registry already seeded in TempRoot
        var eng = CreateEngine(td.Path);
        var modules = eng.GetRegisteredModules();
        Assert.NotNull(modules);
    }

    [Fact]
    public async System.Threading.Tasks.Task InstallModuleAsync_JsonArray_Flattens()
    {
        using var td = new TempRoot();
        var eng = CreateEngine(td.Path);
        var gamesDir = System.IO.Path.Combine(td.Path, "RemakeRegistry", "Games");
        var gameY = System.IO.Path.Combine(gamesDir, "GameY");
        Directory.CreateDirectory(gameY);
        var opsJson = System.IO.Path.Combine(gameY, "operations.json");
        File.WriteAllText(opsJson, "[ { \"Name\": \"One\", \"script\": \"a.py\", \"script_type\": \"foobar\" } ]\n");
        var ok = await eng.InstallModuleAsync("GameY");
        Assert.False(ok);
    }

    [Fact]
    public async System.Threading.Tasks.Task RunSingleOperationAsync_Covers_All_Types()
    {
        using var td = new TempRoot();
        var eng = CreateEngine(td.Path);

        var opsPath = System.IO.Path.Combine(td.Path, "ops.json");
        File.WriteAllText(opsPath, "[]");
        var games = MakeGamesMap(td.Path, "G1", opsPath);
        var answers = new Dictionary<string, object?>();

        // lua: simple script
        var luaPath = System.IO.Path.Combine(td.Path, "test.lua");
        File.WriteAllText(luaPath, "print('hi')\n");
        var opLua = new Dictionary<string, object?> { ["script_type"] = "lua", ["script"] = luaPath };
        var okLua = await eng.RunSingleOperationAsync("G1", games, opLua, answers);
        Assert.True(okLua);

        // js: simple script
        var jsPath = System.IO.Path.Combine(td.Path, "test.js");
        File.WriteAllText(jsPath, "// noop\nvar x=1;\n");
        var opJs = new Dictionary<string, object?> { ["script_type"] = "js", ["script"] = jsPath };
        var okJs = await eng.RunSingleOperationAsync("G1", games, opJs, answers);
        Assert.True(okJs);

        // engine: unknown action -> false
        var opEngUnknown = new Dictionary<string, object?> { ["script_type"] = "engine", ["script"] = "unknown_action" };
        var okUnknown = await eng.RunSingleOperationAsync("G1", games, opEngUnknown, answers);
        Assert.False(okUnknown);

        // default/unknown script_type -> triggers external process runner false
        // also ensure project.json reload path tolerates invalid JSON
        File.WriteAllText(System.IO.Path.Combine(td.Path, "project.json"), "not json");
        var dummy = System.IO.Path.Combine(td.Path, "noop.py");
        File.WriteAllText(dummy, "print('x')\n");
        var opDefault = new Dictionary<string, object?> { ["script_type"] = "foobar", ["script"] = dummy };
        var okDefault = await eng.RunSingleOperationAsync("G1", games, opDefault, answers);
        Assert.False(okDefault);
        // parts.Count < 2 -> false (no script specified)
        var opEmpty = new Dictionary<string, object?> { ["script_type"] = "python" };
        var okEmpty = await eng.RunSingleOperationAsync("G1", games, opEmpty, answers);
        Assert.False(okEmpty);
    }

    [Fact]
    public async System.Threading.Tasks.Task RunSingleOperationAsync_Engine_ExceptionHandled()
    {
        using var td = new TempRoot();
        var eng = CreateEngine(td.Path);
        var opsPath = System.IO.Path.Combine(td.Path, "ops.json");
        File.WriteAllText(opsPath, "[]");
        var games = MakeGamesMap(td.Path, "G1", opsPath);
        var answers = new Dictionary<string, object?>();
        var op = new Dictionary<string, object?> { ["script_type"] = "engine", ["script"] = "download_tools" };
        var ok = await eng.RunSingleOperationAsync("G1", games, op, answers);
        Assert.False(ok);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteEngineOperationAsync_All_Cases()
    {
        using var td = new TempRoot();
        var eng = CreateEngine(td.Path);
        var opsPath = System.IO.Path.Combine(td.Path, "ops.json");
        File.WriteAllText(opsPath, "[]");
        var games = MakeGamesMap(td.Path, "G1", opsPath);
        var answers = new Dictionary<string, object?> { ["force_download"] = true };

        // Prepare central tools registry and empty manifest to avoid network
        var central = System.IO.Path.Combine(td.Path, "RemakeRegistry", "Tools.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(central)!);
        File.WriteAllText(central, "{ }\n");
        var manifest = System.IO.Path.Combine(td.Path, "tools.toml");
        File.WriteAllText(manifest, "# empty manifest\n");

        var opTools = new Dictionary<string, object?>
        {
            ["script"] = "download_tools",
            ["args"] = new List<object?> { manifest }
        };
        Assert.True(await eng.ExecuteEngineOperationAsync("G1", games, opTools, answers));

        // format-extract txd (likely fails quickly but covers branch)
        var opFmtTxd = new Dictionary<string, object?>
        {
            ["script"] = "format-extract",
            ["format"] = "txd",
            ["args"] = new List<object?> { "-i", "input.txd" }
        };
        Assert.False(await eng.ExecuteEngineOperationAsync("G1", games, opFmtTxd, answers));

        // format-extract str
        var opFmtStr = new Dictionary<string, object?>
        {
            ["script"] = "format-extract",
            ["format"] = "str",
            ["args"] = new List<object?> { "-i", "input.str" }
        };
        Assert.False(await eng.ExecuteEngineOperationAsync("G1", games, opFmtStr, answers));

        // format-convert ffmpeg
        var opConvFfmpeg = new Dictionary<string, object?>
        {
            ["script"] = "format-convert",
            ["tool"] = "ffmpeg",
            ["args"] = new List<object?> { "-m", "ffmpeg", "--type", "audio", "-s", System.IO.Path.Combine(td.Path, "src_missing"), "-t", System.IO.Path.Combine(td.Path, "out"), "-i", ".wav", "-o", ".ogg" }
        };
        Assert.False(await eng.ExecuteEngineOperationAsync("G1", games, opConvFfmpeg, answers));

        // format-convert unknown tool -> false
        var opConvUnknown = new Dictionary<string, object?> { ["script"] = "format_convert", ["tool"] = "unknown", ["args"] = new List<object?>() };
        Assert.False(await eng.ExecuteEngineOperationAsync("G1", games, opConvUnknown, answers));

        // format-convert vgmstream branch
        var opConvVgm = new Dictionary<string, object?>
        {
            ["script"] = "format_convert",
            ["tool"] = "vgmstream",
            ["args"] = new List<object?> { "-m", "vgmstream", "--type", "audio", "-s", System.IO.Path.Combine(td.Path, "src_missing2"), "-t", System.IO.Path.Combine(td.Path, "out2"), "-i", ".snu", "-o", ".wav" }
        };
        Assert.False(await eng.ExecuteEngineOperationAsync("G1", games, opConvVgm, answers));
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteEngineOperationAsync_DownloadTools_MissingManifest_ReturnsFalse()
    {
        using var td = new TempRoot();
        var eng = CreateEngine(td.Path);
        var opsPath = System.IO.Path.Combine(td.Path, "ops.json");
        File.WriteAllText(opsPath, "[]");
        var games = MakeGamesMap(td.Path, "G1", opsPath);
        var op = new Dictionary<string, object?> { ["script"] = "download_tools" /* no args/tools_manifest */ };
        Assert.False(await eng.ExecuteEngineOperationAsync("G1", games, op, new Dictionary<string, object?>()));
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteEngineOperationAsync_Throws_On_Unknown_Game()
    {
        using var td = new TempRoot();
        var eng = CreateEngine(td.Path);
        var manifest = System.IO.Path.Combine(td.Path, "tools.toml");
        File.WriteAllText(manifest, "# empty\n");
        Directory.CreateDirectory(System.IO.Path.Combine(td.Path, "RemakeRegistry"));
        File.WriteAllText(System.IO.Path.Combine(td.Path, "RemakeRegistry", "Tools.json"), "{}\n");
        var op = new Dictionary<string, object?> { ["script"] = "download_tools", ["args"] = new List<object?> { manifest } };
        await Assert.ThrowsAsync<KeyNotFoundException>(async () => await eng.ExecuteEngineOperationAsync("MissingGame", new Dictionary<string, object?>(), op, new Dictionary<string, object?>()));
    }

    [Fact]
    public async System.Threading.Tasks.Task RunOperationGroupAsync_Aggregates_Success()
    {
        using var td = new TempRoot();
        var eng = CreateEngine(td.Path);
        var opsPath = System.IO.Path.Combine(td.Path, "ops.json");
        File.WriteAllText(opsPath, "[]");
        var games = MakeGamesMap(td.Path, "G1", opsPath);
        var answers = new Dictionary<string, object?>();

        var luaPath = System.IO.Path.Combine(td.Path, "ok.lua");
        File.WriteAllText(luaPath, "print('ok')\n");
        var dummy = System.IO.Path.Combine(td.Path, "noop.py");
        File.WriteAllText(dummy, "print('x')\n");

        var ops = new List<Dictionary<string, object?>>()
        {
            new() { ["script_type"] = "lua", ["script"] = luaPath },
            new() { ["script_type"] = "foobar", ["script"] = dummy } // will fail
        };

        var ok = await eng.RunOperationGroupAsync("G1", games, "group", ops, answers);
        Assert.False(ok);
    }

    [Fact]
    public void BuildCommand_and_ExecuteCommand_Wrappers()
    {
        using var td = new TempRoot();
        var eng = CreateEngine(td.Path);
        var opsPath = System.IO.Path.Combine(td.Path, "ops.json");
        File.WriteAllText(opsPath, "[]");
        var games = MakeGamesMap(td.Path, "G1", opsPath);
        var answers = new Dictionary<string, object?> { ["flag"] = true, ["textval"] = "abc" };

        var op = new Dictionary<string, object?>
        {
            ["script_type"] = "python",
            ["script"] = "script.py",
            ["prompts"] = new List<object?>
            {
                new Dictionary<string, object?> { ["Name"] = "flag", ["type"] = "confirm", ["cli_arg"] = "--flag" },
                new Dictionary<string, object?> { ["Name"] = "list", ["type"] = "checkbox", ["cli_prefix"] = "--list" },
                new Dictionary<string, object?> { ["Name"] = "textval", ["type"] = "text", ["cli_arg_prefix"] = "--name" },
            }
        };

        var parts = eng.BuildCommand("G1", games, op, answers);
        Assert.True(parts.Count >= 2);
        var ok = eng.ExecuteCommand(new List<string> { "definitely-not-a-real-exe", "arg" }, "title");
        Assert.False(ok);
    }

    [Fact]
    public async System.Threading.Tasks.Task InstallModuleAsync_Paths()
    {
        using var td = new TempRoot();
        var eng = CreateEngine(td.Path);

        // No operations -> false
        var okNone = await eng.InstallModuleAsync("GameX");
        Assert.False(okNone);

        // Prepare a module with grouped operations
        var gamesDir = System.IO.Path.Combine(td.Path, "RemakeRegistry", "Games");
        var gameX = System.IO.Path.Combine(gamesDir, "GameX");
        Directory.CreateDirectory(gameX);
        var opsJson = System.IO.Path.Combine(gameX, "operations.json");
        File.WriteAllText(opsJson, "{\n  \"run-all\": [ { \"Name\": \"One\", \"script\": \"a.py\", \"script_type\": \"foobar\" } ]\n}\n");

        // Execute; will attempt to run unknown exe and fail (ok=false) but cover path
        var ok = await eng.InstallModuleAsync("GameX");
        Assert.False(ok);

        // Add an op lacking a script to hit parts.Count==0 path
        var opsJson2 = System.IO.Path.Combine(gameX, "operations_extra.json");
        File.WriteAllText(opsJson2, "{ \"run\": [ { \"Name\": \"NoScript\" } ] }\n");
        File.Move(opsJson2, opsJson, overwrite: true);
        var ok2 = await eng.InstallModuleAsync("GameX");
        Assert.True(ok2); // no failing ops executed; continues
    }

    [Fact]
    public async System.Threading.Tasks.Task InstallModuleAsync_Groups_No_RunAll_Chooses_First()
    {
        using var td = new TempRoot();
        var eng = CreateEngine(td.Path);
        var gamesDir = System.IO.Path.Combine(td.Path, "RemakeRegistry", "Games");
        var gameZ = System.IO.Path.Combine(gamesDir, "GameZ");
        Directory.CreateDirectory(gameZ);
        var opsJson = System.IO.Path.Combine(gameZ, "operations.json");
        File.WriteAllText(opsJson, "{\n  \"grp1\": [ { \"Name\": \"One\", \"script\": \"a.py\", \"script_type\": \"foobar\" } ],\n  \"grp2\": [ { \"Name\": \"Two\", \"script\": \"b.py\", \"script_type\": \"foobar\" } ]\n}\n");
        var ok = await eng.InstallModuleAsync("GameZ");
        Assert.False(ok);
    }

    [Fact]
    public async System.Threading.Tasks.Task RunSingleOperationAsync_Python_Case_Label()
    {
        using var td = new TempRoot();
        var eng = CreateEngine(td.Path);
        var opsPath = System.IO.Path.Combine(td.Path, "ops.json");
        File.WriteAllText(opsPath, "[]");
        var games = MakeGamesMap(td.Path, "G1", opsPath);
        var py = System.IO.Path.Combine(td.Path, "p.py");
        File.WriteAllText(py, "import sys\nsys.exit(1)\n");
        var op = new Dictionary<string, object?> { ["script_type"] = "python", ["script"] = py };
        var ok = await eng.RunSingleOperationAsync("G1", games, op, new Dictionary<string, object?>());
        Assert.False(ok);
    }

    [Fact]
    public void DownloadModule_Invokes_Git()
    {
        using var td = new TempRoot();
        var eng = CreateEngine(td.Path);
        // We do not assert true/false (depends on git availability); calling it covers the line
        var _ = eng.DownloadModule("https://example.com/repo.git");
    }
}
