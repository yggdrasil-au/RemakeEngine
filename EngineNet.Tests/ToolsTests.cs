

namespace EngineNet.Tests;

public class SimpleTomlTests {
    [Fact]
    public void ReadTools_ParsesMultipleToolTables() {
        using TempDir dir = new TempDir();
        String path = System.IO.Path.Combine(dir.Path, "tools.toml");
        File.WriteAllText(path, "[[tool]]\nname = \"Foo\"\nversion = \"1.0\"\ndestination = \"./foo\"\nunpack = true\n\n[[tool]]\nName = \"Bar\"\nversion = \"2.0\"\nunknown = 123\n\n[[other]]\nName = \"Ignored\"\n");

        List<Dictionary<String, Object?>> tools = SimpleToml.ReadTools(path);
        Assert.Equal(2, tools.Count);

        Dictionary<String, Object?> first = tools[0];
        Assert.Equal("Foo", first["name"]);
        Assert.True(first.TryGetValue("unpack", out Object? unpack) && unpack is Boolean b && b);
        Assert.Equal("./foo", first["destination"]);

        Dictionary<String, Object?> second = tools[1];
        Assert.Equal("Bar", second["name"]);
        Assert.Equal("123", second["unknown"]);
    }

    private sealed class TempDir:IDisposable {
        public String Path {
            get;
        }
        public TempDir() {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "enginetest_simpletoml_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose() {
            try {
                Directory.Delete(Path, recursive: true);
            } catch { }
        }
    }
}

public class JsonToolResolverTests {
    [Fact]
    public void ResolveToolPath_UsesMappingsAndFallsBack() {
        using TempDir dir = new TempDir();
        String jsonPath = System.IO.Path.Combine(dir.Path, "tools.json");
        Directory.CreateDirectory(System.IO.Path.Combine(dir.Path, "bin"));
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "bin", "ffmpeg.exe"), String.Empty);
        Directory.CreateDirectory(System.IO.Path.Combine(dir.Path, "alt"));
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "alt", "tool.cmd"), String.Empty);
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "texconv.exe"), String.Empty);
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "cmd.exe"), String.Empty);

        String json = "{\n" +
                       "  \"ffmpeg\": \"./bin/ffmpeg.exe\",\n" +
                       "  \"texconv\": { \"exe\": \"texconv.exe\" },\n" +
                       "  \"alt\": { \"path\": \"./alt/tool.cmd\" },\n" +
                       "  \"cmd\": { \"command\": \"cmd.exe\" },\n" +
                       "  \"ignored\": 5\n" +
                       "}\n";
        File.WriteAllText(jsonPath, json);

        JsonToolResolver resolver = new JsonToolResolver(jsonPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "bin", "ffmpeg.exe")), resolver.ResolveToolPath("ffmpeg"));
        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "texconv.exe")), resolver.ResolveToolPath("texconv"));
        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "alt", "tool.cmd")), resolver.ResolveToolPath("alt"));
        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "cmd.exe")), resolver.ResolveToolPath("cmd"));
        Assert.Equal("missing", resolver.ResolveToolPath("missing"));
    }

    [Fact]
    public void PassthroughResolver_ReturnsToolId() {
        PassthroughToolResolver resolver = new PassthroughToolResolver();
        Assert.Equal("example", resolver.ResolveToolPath("example"));
    }

    private sealed class TempDir:IDisposable {
        public String Path {
            get;
        }
        public TempDir() {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "enginetest_jsontools_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose() {
            try {
                Directory.Delete(Path, recursive: true);
            } catch { }
        }
    }
}

public class ToolsDownloaderTests {
    [Fact]
    public async Task ProcessAsync_ThrowsWhenManifestMissing() {
        using TempDir dir = new TempDir();
        ToolsDownloader downloader = new ToolsDownloader(dir.Path, System.IO.Path.Combine(dir.Path, "central.json"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => downloader.ProcessAsync(System.IO.Path.Combine(dir.Path, "missing.toml"), force: false));
    }

    [Fact]
    public async Task ProcessAsync_SkipsDownloadAndUpdatesLockFile() {
        using TempDir dir = new TempDir();
        String centralJson = System.IO.Path.Combine(dir.Path, "Tools.json");
        String manifestPath = System.IO.Path.Combine(dir.Path, "module.toml");
        String destination = "./TMP/Downloads";
        String url = "https://example.com/testtool.zip";
        String downloadDir = Path.GetFullPath(Path.Combine(dir.Path, destination));
        Directory.CreateDirectory(downloadDir);
        String archivePath = Path.Combine(downloadDir, "testtool.zip");
        File.WriteAllText(archivePath, "pretend zip contents");
        String sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archivePath))).ToLowerInvariant();

        File.WriteAllText(centralJson, $"{{\n  \"TestTool\": {{\n    \"1.0\": {{\n      \"{GetPlatform()}\": {{ \"url\": \"{url}\", \"sha256\": \"{sha256}\" }}\n    }}\n  }}\n}}\n".Replace("    ", "  "));
        File.WriteAllText(manifestPath, "[[tool]]\nname = \"TestTool\"\nversion = \"1.0\"\ndestination = \"" + destination + "\"\n");

        ToolsDownloader downloader = new ToolsDownloader(dir.Path, centralJson);
        Boolean result = await downloader.ProcessAsync(manifestPath, force: false);
        Assert.True(result);

        String lockFile = Path.Combine(dir.Path, "Tools.local.json");
        Assert.True(File.Exists(lockFile));
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(lockFile));
        JsonElement root = doc.RootElement;
        Assert.True(root.TryGetProperty("TestTool", out JsonElement entry));
        Assert.Equal("1.0", entry.GetProperty("version").GetString());
        Assert.Equal(GetPlatform(), entry.GetProperty("platform").GetString());
        Assert.Equal(url, entry.GetProperty("source_url").GetString());
        Assert.Equal(sha256, entry.GetProperty("sha256").GetString());
        Assert.Equal(Path.GetFullPath(downloadDir), entry.GetProperty("install_path").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("exe").ValueKind);
    }

    [Fact]
    public async Task ProcessAsync_UnpacksZipAndCapturesExecutable() {
        using TempDir dir = new TempDir();
        String centralJson = System.IO.Path.Combine(dir.Path, "Tools.json");
        String manifestPath = System.IO.Path.Combine(dir.Path, "module.toml");
        String destination = "./Downloads";
        String unpackDest = "./Tools/TestTool";
        String downloadDir = Path.GetFullPath(Path.Combine(dir.Path, destination));
        Directory.CreateDirectory(downloadDir);
        String archivePath = Path.Combine(downloadDir, "testtool.zip");

        String staging = Path.Combine(dir.Path, "staging");
        Directory.CreateDirectory(staging);
        String exeName = "TestToolDriver.exe";
        File.WriteAllText(Path.Combine(staging, exeName), "fake exe");
        if (File.Exists(archivePath))
            File.Delete(archivePath);
        ZipFile.CreateFromDirectory(staging, archivePath, CompressionLevel.SmallestSize, includeBaseDirectory: false);
        Directory.Delete(staging, true);

        String sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archivePath))).ToLowerInvariant();
        File.WriteAllText(centralJson, $"{{\n  \"TestTool\": {{\n    \"1.0\": {{\n      \"{GetPlatform()}\": {{ \"url\": \"https://example.com/testtool.zip\", \"sha256\": \"{sha256}\" }}\n    }}\n  }}\n}}\n".Replace("    ", "  "));
        File.WriteAllText(manifestPath, "[[tool]]\nname = \"TestTool\"\nversion = \"1.0\"\ndestination = \"" + destination + "\"\nunpack = true\nunpack_destination = \"" + unpackDest + "\"\n");

        ToolsDownloader downloader = new ToolsDownloader(dir.Path, centralJson);
        Boolean result = await downloader.ProcessAsync(manifestPath, force: false);
        Assert.True(result);

        String expectedExtractDir = Path.GetFullPath(Path.Combine(dir.Path, unpackDest));
        String expectedExePath = Path.Combine(expectedExtractDir, exeName);
        Assert.True(File.Exists(expectedExePath));

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir.Path, "Tools.local.json")));
        JsonElement entry = doc.RootElement.GetProperty("TestTool");
        Assert.Equal(Path.GetFullPath(expectedExtractDir), entry.GetProperty("install_path").GetString());
        String? exe = entry.GetProperty("exe").GetString();
        Assert.NotNull(exe);
        Assert.Equal(expectedExePath, exe, StringComparer.OrdinalIgnoreCase);
    }

    private static String GetPlatform() {
        MethodInfo method = typeof(ToolsDownloader).GetMethod("GetPlatformIdentifier", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (String)method.Invoke(null, Array.Empty<Object>())!;
    }

    private sealed class TempDir:IDisposable {
        public String Path {
            get;
        }
        public TempDir() {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "enginetest_toolsdown_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose() {
            try {
                Directory.Delete(Path, recursive: true);
            } catch { }
        }
    }
}

