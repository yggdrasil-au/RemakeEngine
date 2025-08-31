using System;
using System.Collections.Generic;
using System.IO;
using RemakeEngine.Core;
using Xunit;

namespace EngineNet.Tests;

public class EngineConfigTests
{
    [Fact]
    public void LoadJsonFile_ParsesNested()
    {
        using var tmp = new TempFile("{\n  \"Foo\": { \"Bar\": 123 },\n  \"Flag\": true\n}\n");
        var cfg = new EngineConfig(tmp.Path);
        Assert.True(cfg.Data.ContainsKey("foo"));
        var foo = Assert.IsType<Dictionary<string, object?>>(cfg.Data["foo"]!);
        Assert.Equal(123L, foo["bar"]);
        Assert.Equal(true, cfg.Data["flag"]);
    }

    private sealed class TempFile: IDisposable
    {
        public string Path { get; }
        public TempFile(string content)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "enginenet_cfg_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(Path, content);
        }
        public void Dispose(){ try{ File.Delete(Path);} catch { } }
    }
}
