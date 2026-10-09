namespace EngineNetTest;

using EngineNet.Core.ExternalTools;
using EngineNet.Core.Media;
using EngineNet.Shared.IO;

[TestClass]
public sealed class LongPathIOTests {
    [TestMethod]
    public void GetAbsolutePath_ResolvesAgainstExplicitBaseDirectory() {
        string baseDirectory = Path.Combine(path1: Path.GetTempPath(), path2: $"long-path-base-{Guid.NewGuid():N}");
        string expected = Path.Combine(path1: baseDirectory, path2: "nested", path3: "file.txt");

        string actual = LongPathIO.GetAbsolutePath(path: Path.Combine("nested", "file.txt"), basePath: baseDirectory);
        string expectedAbsolute = Path.GetFullPath(path: expected);

        if (OperatingSystem.IsWindows() && expectedAbsolute.Length > 259) {
            expectedAbsolute = expectedAbsolute.StartsWith(@"\\", comparisonType: StringComparison.Ordinal)
                ? @"\\?\UNC\" + expectedAbsolute.Substring(startIndex: 2)
                : @"\\?\" + expectedAbsolute;
        }

        Assert.AreEqual(expected: expectedAbsolute, actual: actual, ignoreCase: OperatingSystem.IsWindows());
    }

    [TestMethod]
    public void GetAbsolutePath_IsIdempotentForExtendedPaths() {
        if (!OperatingSystem.IsWindows()) {
            Assert.Inconclusive("Windows extended-length prefixes are platform-specific.");
        }

        string extendedPath = @"\\?\C:\very-long-path\file.txt";

        Assert.AreEqual(expected: extendedPath, actual: LongPathIO.GetAbsolutePath(path: extendedPath));
    }

    [TestMethod]
    public void GetAbsolutePath_AddsExtendedPrefixToLongDriveAndUncPaths() {
        if (!OperatingSystem.IsWindows()) {
            Assert.Inconclusive("Windows extended-length prefixes are platform-specific.");
        }

        string drivePath = Path.GetFullPath(path: Path.Combine(Path.GetTempPath(), new string('d', 60), new string('e', 60), new string('f', 60), new string('g', 60), "file.txt"));
        string uncPath = @"\\server\share\" + new string('a', 60) + @"\" + new string('b', 60) + @"\" + new string('c', 60) + @"\" + new string('d', 60) + @"\file.txt";

        Assert.IsTrue(condition: LongPathIO.GetAbsolutePath(path: drivePath).StartsWith(@"\\?\", comparisonType: StringComparison.Ordinal));
        Assert.IsTrue(condition: LongPathIO.GetAbsolutePath(path: uncPath).StartsWith(@"\\?\UNC\", comparisonType: StringComparison.Ordinal));
    }

    [TestMethod]
    public void FileOperations_CreateAndReadNestedLongPath() {
        string rootDirectory = Path.Combine(path1: Path.GetTempPath(), path2: $"long-path-io-{Guid.NewGuid():N}");
        string nestedDirectory = rootDirectory;
        for (int index = 0; index < 6; index++) {
            nestedDirectory = Path.Combine(path1: nestedDirectory, path2: $"segment-{index}-{new string('x', 45)}");
        }

        string filePath = Path.Combine(path1: nestedDirectory, path2: "file.txt");

        try {
            LongPathIO.CreateDirectory(path: nestedDirectory);
            LongPathIO.WriteAllText(path: filePath, contents: "long path works");

            Assert.IsTrue(condition: LongPathIO.DirectoryExists(path: nestedDirectory));
            Assert.IsTrue(condition: LongPathIO.FileExists(path: filePath));
            Assert.AreEqual(expected: "long path works", actual: LongPathIO.ReadAllText(path: filePath));

            string enumeratedFile = LongPathIO.EnumerateFiles(path: rootDirectory, searchOption: SearchOption.AllDirectories).Single();
            string relativePath = LongPathIO.GetRelativePath(basePath: rootDirectory, targetPath: enumeratedFile);
            Assert.AreEqual(expected: Path.GetRelativePath(relativeTo: rootDirectory, path: filePath), actual: relativePath);
        } finally {
            if (LongPathIO.DirectoryExists(path: rootDirectory)) {
                LongPathIO.DeleteDirectory(path: rootDirectory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void ShortPathScope_CreatesAliasesUnderEngineRootAndPreservesTargetsOnDispose() {
        string testRoot = Path.Combine(path1: Path.GetTempPath(), path2: $"short-path-scope-{Guid.NewGuid():N}");
        string engineRoot = Path.Combine(path1: testRoot, path2: "engine");
        string targetDirectory = Path.Combine(path1: testRoot, path2: "target");
        string targetFile = Path.Combine(path1: targetDirectory, path2: "preserved.txt");
        Directory.CreateDirectory(path: engineRoot);
        Directory.CreateDirectory(path: targetDirectory);
        File.WriteAllText(path: targetFile, contents: "preserve target");

        string? aliasPath = null;
        try {
            using (ShortPathScope scope = new(engineRootPath: engineRoot)) {
                aliasPath = scope.GetDirectoryPath(directoryPath: targetDirectory);
            string aliasFile = scope.GetFilePath(filePath: targetFile);

                Assert.IsTrue(condition: Directory.Exists(path: aliasPath));
                Assert.IsTrue(condition: aliasPath.StartsWith(Path.Combine(engineRoot, "TMP", "LongPaths"), comparisonType: StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(condition: scope.ScratchDirectory.StartsWith(Path.Combine(engineRoot, "TMP", "LongPaths"), comparisonType: StringComparison.OrdinalIgnoreCase));
                Assert.AreEqual(expected: "preserve target", actual: File.ReadAllText(path: aliasFile));
                if (OperatingSystem.IsWindows()) {
                    string oversizedNamePath = Path.Combine(path1: targetDirectory, path2: new string('n', 230));
                    bool rejectedOversizedPath = false;
                    try {
                        scope.GetFilePath(filePath: oversizedNamePath);
                    } catch (PathTooLongException) {
                        rejectedOversizedPath = true;
                    }
                    Assert.IsTrue(condition: rejectedOversizedPath);
                }
            }

            Assert.IsFalse(condition: Directory.Exists(path: aliasPath));
            Assert.IsTrue(condition: File.Exists(path: targetFile));
            Assert.AreEqual(expected: "preserve target", actual: File.ReadAllText(path: targetFile));
        } finally {
            Directory.Delete(path: testRoot, recursive: true);
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public void AvTools_Run_ConvertsDeepInputAndOutputPaths() {
        if (!OperatingSystem.IsWindows()) {
            Assert.Inconclusive("The installed ffmpeg lockfile and directory alias test are Windows-specific.");
        }

        string? engineRoot = FindEngineRoot();
        if (engineRoot is null) {
            Assert.Inconclusive("The engine root and installed-tools lockfile were not found.");
        }

        JsonToolResolver resolver = new(rootPath: engineRoot);
        if (!LongPathIO.FileExists(path: resolver.ResolveToolPath(toolId: "ffmpeg"))) {
            Assert.Inconclusive("ffmpeg is not installed in the engine tools lockfile.");
        }

        string previousRoot = EngineNet.Shared.State.RootPath;
        bool previousGui = EngineNet.Shared.State.IsGui;
        bool previousTui = EngineNet.Shared.State.IsTui;
        bool previousCli = EngineNet.Shared.State.IsCli;
        string testRoot = Path.Combine(path1: engineRoot, path2: "TMP", path3: "LongPaths", path4: $"avtools-test-{Guid.NewGuid():N}");

        try {
            EngineNet.Shared.State.ConfigureRuntime(rootPath: engineRoot, isGui: false, isTui: false, isCli: true);

            string nestedPath = Path.Combine(paths: Enumerable.Range(start: 0, count: 6)
                .Select(selector: index => $"segment-{index}-{new string('x', 40)}")
                .ToArray());
            string sourceDirectory = Path.Combine(path1: testRoot, path2: "source", path3: nestedPath);
            string targetDirectory = Path.Combine(path1: testRoot, path2: "target", path3: nestedPath);
            string inputPath = Path.Combine(path1: sourceDirectory, path2: "input.wav");
            string outputPath = Path.Combine(path1: targetDirectory, path2: "input.wav");
            LongPathIO.CreateDirectory(path: sourceDirectory);
            WriteSilentWave(path: inputPath);
            Assert.IsTrue(condition: inputPath.Length > 259);

            bool converted = AvTools.Run(
                toolResolver: resolver,
                args: new List<string> {
                    "--mode", "ffmpeg",
                    "--type", "audio",
                    "--source", Path.Combine(path1: testRoot, path2: "source"),
                    "--target", Path.Combine(path1: testRoot, path2: "target"),
                    "--input-ext", ".wav",
                    "--output-ext", ".wav",
                    "--overwrite",
                    "--workers", "1"
                }
            );

            Assert.IsTrue(condition: converted);
            Assert.IsTrue(condition: LongPathIO.FileExists(path: outputPath));
        } finally {
            if (LongPathIO.DirectoryExists(path: testRoot)) {
                LongPathIO.DeleteDirectory(path: testRoot, recursive: true);
            }

            EngineNet.Shared.State.ConfigureRuntime(rootPath: previousRoot, isGui: previousGui, isTui: previousTui, isCli: previousCli);
        }
    }

    private static string? FindEngineRoot() {
        foreach (string startPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }) {
            DirectoryInfo? directory = new(path: startPath);
            while (directory is not null) {
                string installedToolsPath = Path.Combine(path1: directory.FullName, path2: "Tools.installed.json");
                string engineAppsPath = Path.Combine(path1: directory.FullName, path2: "EngineApps");
                if (File.Exists(path: installedToolsPath) && Directory.Exists(path: engineAppsPath)) {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    private static void WriteSilentWave(string path) {
        using BinaryWriter writer = new(
            output: LongPathIO.OpenFile(path: path, mode: FileMode.Create, access: FileAccess.Write),
            encoding: System.Text.Encoding.ASCII
        );
        writer.Write(System.Text.Encoding.ASCII.GetBytes(s: "RIFF"));
        writer.Write(value: 38U);
        writer.Write(System.Text.Encoding.ASCII.GetBytes(s: "WAVEfmt "));
        writer.Write(value: 16U);
        writer.Write(value: (ushort)1);
        writer.Write(value: (ushort)1);
        writer.Write(value: 8000U);
        writer.Write(value: 16000U);
        writer.Write(value: (ushort)2);
        writer.Write(value: (ushort)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes(s: "data"));
        writer.Write(value: 2U);
        writer.Write(value: (short)0);
    }
}