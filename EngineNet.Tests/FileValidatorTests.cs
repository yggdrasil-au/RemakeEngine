using EngineNet.Core.FileHandlers;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace EngineNet.Tests;

public class FileValidatorTests
{
    [Fact]
    public void Run_Succeeds_WhenAnyVariantDirectoryExists()
    {
        using TempDir td = new TempDir();
        String dbPath = Path.Combine(td.Path, "indexes.db");
        File.WriteAllText(dbPath, "placeholder");

        String baseFolder = Path.Combine(td.Path, "base");
        Directory.CreateDirectory(baseFolder);
        Directory.CreateDirectory(Path.Combine(baseFolder, "audio"));
        Directory.CreateDirectory(Path.Combine(baseFolder, "Video"));

        List<String> args = new List<String>
        {
            dbPath,
            baseFolder,
            "--required-dirs",
            "audiostreams||audio,movies||Video"
        };

        Boolean result = FileValidator.Run(args);
        Assert.True(result);
    }

    [Fact]
    public void Run_Fails_WhenAllVariantDirectoriesMissing()
    {
        using TempDir td = new TempDir();
        String dbPath = Path.Combine(td.Path, "indexes.db");
        File.WriteAllText(dbPath, "placeholder");

        String baseFolder = Path.Combine(td.Path, "base");
        Directory.CreateDirectory(baseFolder);

        List<String> args = new List<String>
        {
            dbPath,
            baseFolder,
            "--required-dirs",
            "sound||audio"
        };

        Boolean result = FileValidator.Run(args);
        Assert.False(result);
    }

    private sealed class TempDir : IDisposable
    {
        public String Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "enginenet_filevalidator_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
