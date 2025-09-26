using System;
using System.Collections.Generic;
using RemakeEngine.Core;
using Xunit;

namespace EngineNet.Tests;

public class CommandBuilderTests
{
    private static Dictionary<String, Object?> MakeGames(String root)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["TestGame"] = new Dictionary<String, Object?>
            {
                ["game_root"] = root,
                ["ops_file"] = System.IO.Path.Combine(root, "operations.toml")
            }
        };

    [Fact]
    public void Build_Throws_When_NoGameLoaded()
    {
        RemakeEngine.Sys.CommandBuilder b = new RemakeEngine.Sys.CommandBuilder(".");
        ArgumentException ex = Assert.Throws<ArgumentException>(() => b.Build("", new Dictionary<String, Object?>(), new Dictionary<String, Object?>(), new Dictionary<String, Object?>(), new Dictionary<String, Object?>()));
        Assert.Contains("No game has been loaded", ex.Message);
    }

    [Fact]
    public void Build_Throws_When_UnknownGame()
    {
        RemakeEngine.Sys.CommandBuilder b = new RemakeEngine.Sys.CommandBuilder(".");
        Dictionary<String, Object?> games = new Dictionary<String, Object?>();
        KeyNotFoundException ex = Assert.Throws<KeyNotFoundException>(() => b.Build("Missing", games, new Dictionary<String, Object?>(), new Dictionary<String, Object?>(), new Dictionary<String, Object?>()));
        Assert.Contains("Unknown game", ex.Message);
    }

    [Fact]
    public void Build_ReturnsEmpty_When_NoScript()
    {
        RemakeEngine.Sys.CommandBuilder b = new RemakeEngine.Sys.CommandBuilder(".");
        String root = System.IO.Path.GetTempPath();
        Dictionary<String, Object?> games = MakeGames(root);
        List<String> parts = b.Build("TestGame", games, new Dictionary<String, Object?>(), new Dictionary<String, Object?>(), new Dictionary<String, Object?>());
        Assert.Empty(parts);
    }

    [Fact]
    public void Build_Defaults_To_Python_And_Resolves_Placeholders()
    {
        RemakeEngine.Sys.CommandBuilder b = new RemakeEngine.Sys.CommandBuilder(".");
        String root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "enginenet_tests_root");
        Dictionary<String, Object?> games = MakeGames(root);

        Dictionary<String, Object?> engineConfig = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["OutputBase"] = "C:/Out"
        };

        Dictionary<String, Object?> op = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["script"] = "{{Game.RootPath}}/run.py",
            ["args"] = new List<Object?> { "-o", "{{OutputBase}}/file.bin" }
        };

        List<String> parts = b.Build("TestGame", games, engineConfig, op, new Dictionary<String, Object?>());

    Assert.True(parts.Count >= 2);
        String expectedPython = OperatingSystem.IsWindows() ? "python" : "python3";
    Assert.Equal(expectedPython, parts[0]); // On Windows this should be python
        Assert.Equal(System.IO.Path.Combine(root, "run.py").Replace('\\','/'), parts[1].Replace('\\','/'));
        Assert.Equal(4, parts.Count);
        Assert.Equal("-o", parts[2]);
        Assert.Equal("C:/Out/file.bin", parts[3]);
    }

    [Fact]
    public void Build_Maps_Prompts_With_Conditions()
    {
        RemakeEngine.Sys.CommandBuilder b = new RemakeEngine.Sys.CommandBuilder(".");
        String root = System.IO.Path.GetTempPath();
        Dictionary<String, Object?> games = MakeGames(root);

        Dictionary<String, Object?> op = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["script"] = "do.py",
            ["prompts"] = new List<Object?>
            {
                new Dictionary<String, Object?>
                {
                    ["Name"] = "DoIt",
                    ["type"] = "confirm",
                    ["cli_arg"] = "--go"
                },
                new Dictionary<String, Object?>
                {
                    ["Name"] = "Items",
                    ["type"] = "checkbox",
                    ["cli_prefix"] = "--mods"
                },
                new Dictionary<String, Object?>
                {
                    ["Name"] = "Path",
                    ["type"] = "text",
                    ["cli_arg_prefix"] = "--path",
                    ["default"] = "C:/default"
                },
                new Dictionary<String, Object?>
                {
                    ["Name"] = "Sub",
                    ["type"] = "text",
                    ["cli_arg"] = "--sub",
                    ["condition"] = "DoIt"
                }
            }
        };

        Dictionary<String, Object?> answers = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DoIt"] = true,
            ["Items"] = new List<Object?> { "a", "b" },
            // Path is not set; default should be used
            ["Sub"] = "fine"
        };

        List<String> parts = b.Build("TestGame", games, new Dictionary<String, Object?>(), op, answers);

        // Expect: python[3] do.py --go --mods a b --path C:/default --sub fine
        String expectedPython = OperatingSystem.IsWindows() ? "python" : "python3";
    Assert.Equal(expectedPython, parts[0]);
        Assert.Equal("do.py", parts[1]);
        Assert.Contains("--go", parts);
        Int32 modsIdx = parts.IndexOf("--mods");
        Assert.InRange(modsIdx, 0, parts.Count - 3);
        Assert.Equal("a", parts[modsIdx + 1]);
        Assert.Equal("b", parts[modsIdx + 2]);
        Int32 pathIdx = parts.IndexOf("--path");
        Assert.InRange(pathIdx, 0, parts.Count - 2);
        Assert.Equal("C:/default", parts[pathIdx + 1]);
        Int32 subIdx = parts.IndexOf("--sub");
        Assert.InRange(subIdx, 0, parts.Count - 2);
        Assert.Equal("fine", parts[subIdx + 1]);
    }
}
