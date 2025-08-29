using System;
using System.Collections.Generic;
using System.IO;

namespace RemakeEngine.Core;

public sealed class Registries {
    private readonly string _rootPath;
    private readonly string _gamesRegistryPath;
    private readonly string _modulesRegistryPath;

    private Dictionary<string, object?> _modules = new(StringComparer.OrdinalIgnoreCase);

    public Registries(string rootPath) {
        _rootPath = rootPath;

        // Preferred locations (relative to working root)
        var gamesRel = System.IO.Path.Combine(rootPath, "RemakeRegistry", "Games");
        var modulesRel = System.IO.Path.Combine(rootPath, "RemakeRegistry", "register.json");

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(modulesRel) ?? rootPath);

        // If modules registry JSON is missing, try to download from GitHub repo
        if (!File.Exists(modulesRel)) {
            RemoteFallbacks.EnsureRepoFile(System.IO.Path.Combine("RemakeRegistry", "register.json"), modulesRel);
        }

        _gamesRegistryPath = gamesRel;
        _modulesRegistryPath = modulesRel;
        _modules = EngineConfig.LoadJsonFile(_modulesRegistryPath);
    }

    public void RefreshModules() => _modules = EngineConfig.LoadJsonFile(_modulesRegistryPath);

    public IReadOnlyDictionary<string, object?> GetRegisteredModules() {
        if (_modules.TryGetValue("modules", out var m) && m is Dictionary<string, object?> dict)
            return dict;
        return new Dictionary<string, object?>();
    }

    public Dictionary<string, GameInfo> DiscoverGames() {
        var games = new Dictionary<string, GameInfo>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_gamesRegistryPath))
            return games;

        foreach (var dir in Directory.EnumerateDirectories(_gamesRegistryPath)) {
            var ops = System.IO.Path.Combine(dir, "operations.json");
            if (File.Exists(ops)) {
                var name = new DirectoryInfo(dir).Name;
                games[name] = new GameInfo(
                    opsFile: System.IO.Path.GetFullPath(ops),
                    gameRoot: System.IO.Path.GetFullPath(dir)
                );
            }
        }
        return games;
    }
}
