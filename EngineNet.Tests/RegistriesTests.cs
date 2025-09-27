using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace EngineNet.Tests;

public class RegistriesTests
{
    private sealed class TempDir: IDisposable
    {
        public String Path { get; }
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "enginenet_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    [Fact]
    public void DiscoverGames_Finds_Toml_And_Json()
    {
        using TempDir td = new TempDir();
        String gamesRoot = System.IO.Path.Combine(td.Path, "RemakeRegistry", "Games");
        Directory.CreateDirectory(gamesRoot);

        // Ensure register.json exists to avoid network fallback
        String regPath = System.IO.Path.Combine(td.Path, "RemakeRegistry", "register.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(regPath)!);
        File.WriteAllText(regPath, "{\n  \"modules\": {}\n}\n");

        String g1 = System.IO.Path.Combine(gamesRoot, "GameA");
        String g2 = System.IO.Path.Combine(gamesRoot, "GameB");
        String g3 = System.IO.Path.Combine(gamesRoot, "GameC");
        Directory.CreateDirectory(g1);
        Directory.CreateDirectory(g2);
        Directory.CreateDirectory(g3);
        // TOML operations
        File.WriteAllText(System.IO.Path.Combine(g1, "operations.toml"), "[[copy]]\nName='Copy'\nscript='do.py'\n");
        // JSON operations
        File.WriteAllText(System.IO.Path.Combine(g3, "operations.json"), "[ { \"Name\": \"Do\", \"script\": \"do.py\" } ]");

        EngineNet.Core.Sys.Registries reg = new EngineNet.Core.Sys.Registries(td.Path);
        Dictionary<String, EngineNet.Core.Sys.GameInfo> games = reg.DiscoverGames();
        Assert.Contains("GameA", games.Keys);
        Assert.Contains("GameC", games.Keys);
        Assert.DoesNotContain("GameB", games.Keys);
    }

    [Fact]
    public void DiscoverInstalledGames_Requires_Valid_GameToml_And_Exe()
    {
        using TempDir td = new TempDir();
        String gamesRoot = System.IO.Path.Combine(td.Path, "RemakeRegistry", "Games");
        Directory.CreateDirectory(gamesRoot);

        // Ensure register.json exists to avoid network fallback
        String regPath = System.IO.Path.Combine(td.Path, "RemakeRegistry", "register.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(regPath)!);
        File.WriteAllText(regPath, "{\n  \"modules\": {}\n}\n");

        String g1 = System.IO.Path.Combine(gamesRoot, "GameA");
        Directory.CreateDirectory(g1);
        File.WriteAllText(System.IO.Path.Combine(g1, "operations.json"), "[]");

        // Create a fake exe and game.toml
        String binDir = System.IO.Path.Combine(g1, "bin");
        Directory.CreateDirectory(binDir);
        String exePathRel = System.IO.Path.Combine("bin", "game.exe");
        String exePath = System.IO.Path.Combine(g1, exePathRel);
        File.WriteAllText(exePath, "fake");

        File.WriteAllText(System.IO.Path.Combine(g1, "game.toml"), "title = \"My Game\"\nexe = \"" + exePathRel.Replace("\\", "\\\\") + "\"\n");

        EngineNet.Core.Sys.Registries reg = new EngineNet.Core.Sys.Registries(td.Path);
        Dictionary<String, EngineNet.Core.Sys.GameInfo> games = reg.DiscoverInstalledGames();
        Assert.Contains("GameA", games.Keys);
        EngineNet.Core.Sys.GameInfo info = games["GameA"];
        Assert.NotNull(info.ExePath);
        Assert.True(System.IO.Path.IsPathRooted(info.ExePath!));
        Assert.Equal("My Game", info.Title);
    }
}

