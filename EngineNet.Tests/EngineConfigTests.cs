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
        using TempFile tmp = new TempFile("{\n  \"Foo\": { \"Bar\": 123 },\n  \"Flag\": true\n}\n");
        var cfg = new RemakeEngine.Sys.EngineConfig(tmp.Path);
        Assert.True(cfg.Data.ContainsKey("foo"));
        Dictionary<String, Object?> foo = Assert.IsType<Dictionary<String, Object?>>(cfg.Data["foo"]!);
        Assert.Equal(123L, foo["bar"]);
        Assert.Equal(true, cfg.Data["flag"]);
    }

    private sealed class TempFile: IDisposable
    {
        public String Path { get; }
        public TempFile(String content)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "enginenet_cfg_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(Path, content);
        }
        public void Dispose(){ try{ File.Delete(Path);} catch { } }
    }
}
