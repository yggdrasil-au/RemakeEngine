using EngineNet.Core.ExternalTools;

namespace EngineNetTest;

[TestClass]
public sealed class JsonToolResolverTests {
    /// <summary>
    /// Verifies that the resolver uses the lockfile path as the executable trust boundary.
    /// </summary>
    [TestMethod]
    public void TrackedToolResolution_UsesExactNormalizedLockfilePaths() {
        string rootPath = Path.Combine(path1: Path.GetTempPath(), path2: $"remake-engine-tools-{Guid.NewGuid():N}");
        string toolsDirectory = Path.Combine(path1: rootPath, path2: "tools");
        string executablePath = Path.Combine(path1: toolsDirectory, path2: "ffmpeg.exe");
        string untrackedPath = Path.Combine(path1: rootPath, path2: "Malware", path3: "Tools", path4: "ffmpeg.exe");

        Directory.CreateDirectory(path: toolsDirectory);
        File.WriteAllText(
            path: Path.Combine(path1: rootPath, path2: "Tools.installed.json"),
            contents: """
                      {
                        "ffmpeg": {
                          "7.1": {
                            "version": "7.1",
                            "platform": "win-x64",
                            "install_path": "tools",
                            "exe": "tools/ffmpeg.exe",
                            "sha256": "",
                            "source_url": "https://example.invalid/ffmpeg.zip"
                          }
                        }
                      }
                      """
        );

        try {
            JsonToolResolver resolver = new(rootPath: rootPath);

            (string? executable, string? version) = resolver.ResolveExeAndVersion(toolId: "ffmpeg");

            Assert.AreEqual(expected: executablePath, actual: executable);
            Assert.AreEqual(expected: "7.1", actual: version);
            Assert.IsTrue(condition: resolver.IsTrackedTool(executablePath: Path.Combine(path1: toolsDirectory, path2: ".", path3: "ffmpeg.exe")));
            Assert.IsFalse(condition: resolver.IsTrackedTool(executablePath: untrackedPath));
        } finally {
            Directory.Delete(path: rootPath, recursive: true);
        }
    }
}
