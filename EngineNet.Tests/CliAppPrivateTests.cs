using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EngineNet.Core;
using EngineNet.Interface.CLI;
using EngineNet.Tools;
using Xunit;

namespace EngineNet.Tests;

public sealed class CliAppPrivateTests : IDisposable {
    private readonly string _root;
    private readonly OperationsEngine _engine;

    public CliAppPrivateTests() {
        _root = Path.Combine(Path.GetTempPath(), "EngineNet_CLI_Priv_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "RemakeRegistry"));
        File.WriteAllText(Path.Combine(_root, "RemakeRegistry", "register.json"), "{\n  \"modules\": {}\n}");
        File.WriteAllText(Path.Combine(_root, "project.json"), "{\n  \"RemakeEngine\": { \n    \"Config\": { \"project_path\": \"" + _root.Replace("\\", "\\\\") + "\" }\n  }\n}");
        EngineConfig cfg = new EngineConfig(Path.Combine(_root, "project.json"));
        _engine = new OperationsEngine(_root, new PassthroughToolResolver(), cfg);
    }

    public void Dispose() {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }

    private static MethodInfo GetPrivateStatic(string name, Type[]? parameters = null) {
        var t = typeof(CliApp);
        var mi = t.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic, Type.DefaultBinder, parameters ?? Type.EmptyTypes, null);
        if (mi == null) throw new InvalidOperationException($"Method {name} not found");
        return mi;
    }

    [Fact]
    public void IsInlineOperationInvocation_DetectsProperly() {
        var meth = GetPrivateStatic("IsInlineOperationInvocation", new[] { typeof(string[]) });
        bool yes = (bool)meth.Invoke(null, new object?[] { new[] { "--game", "Foo", "--script", "a.lua" } })!;
        bool no = (bool)meth.Invoke(null, new object?[] { new[] { "--list-games" } })!;
        Assert.True(yes);
        Assert.False(no);
    }

    [Fact]
    public void TryResolveInlineGame_ByExistingName_And_ByRoot() {
        // Arrange existing game in registry
        string gamesDir = Path.Combine(_root, "RemakeRegistry", "Games", "MyGame");
        Directory.CreateDirectory(gamesDir);
        File.WriteAllText(Path.Combine(gamesDir, "operations.json"), "[]");

        var optionsByName = CliApp.InlineOperationOptions.Parse(new[] { "--game", "MyGame", "--script", "a.py" });
        var optionsByRoot = CliApp.InlineOperationOptions.Parse(new[] { "--game-root", gamesDir, "--script", "a.py" });

        var games = _engine.ListGames();
        string? resolved;
        var method = GetPrivateStatic("TryResolveInlineGame", new[] { typeof(CliApp.InlineOperationOptions), typeof(Dictionary<string, object?>), typeof(string).MakeByRefType() });

        object?[] pars1 = new object?[] { optionsByName, games, null };
        bool ok1 = (bool)method.Invoke(null, pars1)!;
        resolved = (string?)pars1[2];
        Assert.True(ok1);
        Assert.Equal("MyGame", resolved);

        games = _engine.ListGames(); // refresh view
        object?[] pars2 = new object?[] { optionsByRoot, games, null };
        bool ok2 = (bool)method.Invoke(null, pars2)!;
        resolved = (string?)pars2[2];
        Assert.True(ok2);
        Assert.Equal(new DirectoryInfo(gamesDir).Name, resolved);
    }

    [Fact]
    public void CollectAnswersForOperation_DefaultsOnly_With_Conditions() {
        // Access private static CollectAnswersForOperation
        var method = GetPrivateStatic("CollectAnswersForOperation", new[] { typeof(Dictionary<string, object?>), typeof(Dictionary<string, object?>), typeof(bool) });

        var op = new Dictionary<string, object?> {
            ["prompts"] = new List<object?> {
                new Dictionary<string, object?> { ["Name"] = "enable", ["type"] = "confirm", ["default"] = true },
                new Dictionary<string, object?> { ["Name"] = "choice", ["type"] = "checkbox", ["choices"] = new List<object?> { "a", "b" }, ["default"] = new List<object?> { "b" }, ["condition"] = "enable" },
                new Dictionary<string, object?> { ["Name"] = "note", ["type"] = "text", ["default"] = "hello", ["condition"] = "enable" },
                new Dictionary<string, object?> { ["Name"] = "skipped", ["type"] = "text", ["condition"] = "nonexistent" },
            }
        };

        var answers = new Dictionary<string, object?>();
        method.Invoke(null, new object?[] { op, answers, true });

        Assert.True(answers.TryGetValue("enable", out var en) && en is bool b && b);
        Assert.True(answers.TryGetValue("choice", out var ch) && ch is List<object?> list && list.SequenceEqual(new object?[] { "b" }));
        Assert.Equal("hello", answers["note"]);
        Assert.True(answers.TryGetValue("skipped", out var sk) && sk is null);
    }
}
