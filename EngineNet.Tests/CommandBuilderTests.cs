using System;
using System.Collections.Generic;
using RemakeEngine.Core;
using Xunit;

namespace EngineNet.Tests;

public class CommandBuilderTests
{
    private static Dictionary<string, object?> MakeGames(string root)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["TestGame"] = new Dictionary<string, object?>
            {
                ["game_root"] = root,
                ["ops_file"] = System.IO.Path.Combine(root, "operations.toml")
            }
        };

    [Fact]
    public void Build_Throws_When_NoGameLoaded()
    {
        var b = new CommandBuilder(".");
        var ex = Assert.Throws<ArgumentException>(() => b.Build("", new Dictionary<string, object?>(), new Dictionary<string, object?>(), new Dictionary<string, object?>(), new Dictionary<string, object?>()));
        Assert.Contains("No game has been loaded", ex.Message);
    }

    [Fact]
    public void Build_Throws_When_UnknownGame()
    {
        var b = new CommandBuilder(".");
        var games = new Dictionary<string, object?>();
        var ex = Assert.Throws<KeyNotFoundException>(() => b.Build("Missing", games, new Dictionary<string, object?>(), new Dictionary<string, object?>(), new Dictionary<string, object?>()));
        Assert.Contains("Unknown game", ex.Message);
    }

    [Fact]
    public void Build_ReturnsEmpty_When_NoScript()
    {
        var b = new CommandBuilder(".");
        var root = System.IO.Path.GetTempPath();
        var games = MakeGames(root);
        var parts = b.Build("TestGame", games, new Dictionary<string, object?>(), new Dictionary<string, object?>(), new Dictionary<string, object?>());
        Assert.Empty(parts);
    }

    [Fact]
    public void Build_Defaults_To_Python_And_Resolves_Placeholders()
    {
        var b = new CommandBuilder(".");
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "enginenet_tests_root");
        var games = MakeGames(root);

        var engineConfig = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["OutputBase"] = "C:/Out"
        };

        var op = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["script"] = "{{Game.RootPath}}/run.py",
            ["args"] = new List<object?> { "-o", "{{OutputBase}}/file.bin" }
        };

        var parts = b.Build("TestGame", games, engineConfig, op, new Dictionary<string, object?>());

        Assert.True(parts.Count >= 2);
        Assert.Equal("python", parts[0]); // On Windows this should be python
        Assert.Equal(System.IO.Path.Combine(root, "run.py").Replace('\\','/'), parts[1].Replace('\\','/'));
        Assert.Equal(4, parts.Count);
        Assert.Equal("-o", parts[2]);
        Assert.Equal("C:/Out/file.bin", parts[3]);
    }

    [Fact]
    public void Build_Maps_Prompts_With_Conditions()
    {
        var b = new CommandBuilder(".");
        var root = System.IO.Path.GetTempPath();
        var games = MakeGames(root);

        var op = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["script"] = "do.py",
            ["prompts"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["Name"] = "DoIt",
                    ["type"] = "confirm",
                    ["cli_arg"] = "--go"
                },
                new Dictionary<string, object?>
                {
                    ["Name"] = "Items",
                    ["type"] = "checkbox",
                    ["cli_prefix"] = "--mods"
                },
                new Dictionary<string, object?>
                {
                    ["Name"] = "Path",
                    ["type"] = "text",
                    ["cli_arg_prefix"] = "--path",
                    ["default"] = "C:/default"
                },
                new Dictionary<string, object?>
                {
                    ["Name"] = "Sub",
                    ["type"] = "text",
                    ["cli_arg"] = "--sub",
                    ["condition"] = "DoIt"
                }
            }
        };

        var answers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DoIt"] = true,
            ["Items"] = new List<object?> { "a", "b" },
            // Path is not set; default should be used
            ["Sub"] = "fine"
        };

        var parts = b.Build("TestGame", games, new Dictionary<string, object?>(), op, answers);

        // Expect: python do.py --go --mods a b --path C:/default --sub fine
        Assert.Equal("python", parts[0]);
        Assert.Equal("do.py", parts[1]);
        Assert.Contains("--go", parts);
        var modsIdx = parts.IndexOf("--mods");
        Assert.InRange(modsIdx, 0, parts.Count - 3);
        Assert.Equal("a", parts[modsIdx + 1]);
        Assert.Equal("b", parts[modsIdx + 2]);
        var pathIdx = parts.IndexOf("--path");
        Assert.InRange(pathIdx, 0, parts.Count - 2);
        Assert.Equal("C:/default", parts[pathIdx + 1]);
        var subIdx = parts.IndexOf("--sub");
        Assert.InRange(subIdx, 0, parts.Count - 2);
        Assert.Equal("fine", parts[subIdx + 1]);
    }
}
