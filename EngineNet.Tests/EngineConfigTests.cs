
namespace EngineNet.Tests;

public class EngineConfigTests {
    [Fact]
    public void LoadJsonFile_ParsesNested() {
        using TempFile tmp = new TempFile("{\n  \"Foo\": { \"Bar\": 123 },\n  \"Flag\": true\n}\n");
        EngineNet.EngineConfig cfg = new EngineNet.EngineConfig(tmp.Path);
        Assert.True(cfg.Data.ContainsKey("foo"));
        Dictionary<String, Object?> foo = Assert.IsType<Dictionary<String, Object?>>(cfg.Data["foo"]!);
        Assert.Equal(123L, foo["bar"]);
        Assert.Equal(true, cfg.Data["flag"]);
    }

    [Fact]
    public void Reload_ReflectsLatestFileContents() {
        using TempFile tmp = new TempFile("{\n  \"Foo\": \"Alpha\",\n  \"Flag\": false\n}\n");
        EngineNet.EngineConfig cfg = new EngineNet.EngineConfig(tmp.Path);
        Assert.Equal("Alpha", cfg.Data["foo"]);

        File.WriteAllText(tmp.Path, "{\n  \"Foo\": \"Beta\",\n  \"Flag\": true,\n  \"Extra\": 5\n}\n");
        cfg.Reload();

        Assert.Equal("Beta", cfg.Data["FOO"]);
        Assert.Equal(true, cfg.Data["flag"]);
        Assert.Equal(5L, cfg.Data["extra"]);
    }

    [Fact]
    public void LoadJsonFile_ReturnsEmpty_WhenMissingOrInvalid() {
        String missingPath = Path.Combine(Path.GetTempPath(), "engcfg_missing_" + Guid.NewGuid().ToString("N") + ".json");
        Dictionary<String, Object?> missing = EngineNet.EngineConfig.LoadJsonFile(missingPath);
        Assert.Empty(missing);

        using TempFile tmp = new TempFile("{ invalid json");
        Dictionary<String, Object?> invalid = EngineNet.EngineConfig.LoadJsonFile(tmp.Path);
        Assert.Empty(invalid);
    }

    private sealed class TempFile:IDisposable {
        public String Path {
            get;
        }
        public TempFile(String content) {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "enginenet_cfg_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(Path, content);
        }
        public void Dispose() {
            try {
                File.Delete(Path);
            } catch { }
        }
    }
}
