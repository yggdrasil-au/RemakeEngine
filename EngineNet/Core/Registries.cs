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
            var opsToml = Path.Combine(dir, "operations.toml");
            var opsJson = Path.Combine(dir, "operations.json");
            string? ops = null;
            if (File.Exists(opsToml))
                ops = opsToml;
            else if (File.Exists(opsJson))
                ops = opsJson;
            if (ops is null)
                continue;

            var name = new DirectoryInfo(dir).Name;
            games[name] = new GameInfo(
                opsFile: Path.GetFullPath(ops),
                gameRoot: Path.GetFullPath(dir)
            );
        }
        return games;
    }


    public Dictionary<string, GameInfo> DiscoverInstalledGames()
	{
		var games = new Dictionary<string, GameInfo>(StringComparer.OrdinalIgnoreCase);
		if (!Directory.Exists(_gamesRegistryPath))
			return games;

                foreach (var dir in Directory.EnumerateDirectories(_gamesRegistryPath))
                {
                        var opsToml = Path.Combine(dir, "operations.toml");
                        var opsJson = Path.Combine(dir, "operations.json");
                        string? ops = null;
                        if (File.Exists(opsToml))
                                ops = opsToml;
                        else if (File.Exists(opsJson))
                                ops = opsJson;
                        if (ops is null)
                                continue;

			var gameToml = System.IO.Path.Combine(dir, "game.toml");
			if (!File.Exists(gameToml))
				continue; // not installed – requires a valid game.toml

			// Parse a minimal subset of TOML: top-level key = "value" pairs
			string? exePath = null;
			string? title = null;
			try
			{
				foreach (var raw in File.ReadAllLines(gameToml))
				{
					var line = raw.Trim();
					if (line.Length == 0 || line.StartsWith("#"))
						continue;
					// ignore tables/arrays
					if (line.StartsWith("[") && line.EndsWith("]"))
						continue;
					var eq = line.IndexOf('=');
					if (eq <= 0)
						continue;
					var key = line.Substring(0, eq).Trim();
					var valRaw = line.Substring(eq + 1).Trim();
					string? val;
					if (valRaw.StartsWith("\"") && valRaw.EndsWith("\""))
						val = valRaw.Substring(1, valRaw.Length - 2);
					else
						val = valRaw;

					if (key.Equals("exe", StringComparison.OrdinalIgnoreCase) || key.Equals("executable", StringComparison.OrdinalIgnoreCase))
						exePath = val;
					else if (key.Equals("title", StringComparison.OrdinalIgnoreCase) || key.Equals("name", StringComparison.OrdinalIgnoreCase))
						title = val;
				}
			}
			catch
			{
				// malformed game.toml – reject
				continue;
			}

			if (string.IsNullOrWhiteSpace(exePath))
				continue;

			// Resolve and validate executable
			var exeFull = System.IO.Path.IsPathRooted(exePath!) ? exePath! : System.IO.Path.Combine(dir, exePath!);
			if (!File.Exists(exeFull))
				continue; // exe missing – not installed

			var name = new DirectoryInfo(dir).Name;
			games[name] = new GameInfo(
				opsFile: System.IO.Path.GetFullPath(ops),
				gameRoot: System.IO.Path.GetFullPath(dir),
				exePath: System.IO.Path.GetFullPath(exeFull),
				title: title
			);
		}

		return games;
	}
}
