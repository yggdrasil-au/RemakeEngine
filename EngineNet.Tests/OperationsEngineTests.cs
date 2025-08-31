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

