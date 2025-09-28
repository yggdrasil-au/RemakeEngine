using System;
using System.Collections.Generic;
using System.IO;
using EngineNet.Tools;

namespace EngineNet.Core.Sys;

public sealed class Registries {
    private readonly String _rootPath;
    private readonly String _gamesRegistryPath;
    private readonly String _modulesRegistryPath;

    private Dictionary<String, Object?> _modules = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);

    public Registries(String rootPath) {
        _rootPath = rootPath;

        // Preferred locations (relative to working root)
        String gamesRel = Path.Combine(rootPath, "RemakeRegistry", "Games");
        String modulesRel = Path.Combine(rootPath, "RemakeRegistry", "register.json");

        Directory.CreateDirectory(Path.GetDirectoryName(modulesRel) ?? rootPath);

        // If modules registry JSON is missing, try to download from GitHub repo
        if (!File.Exists(modulesRel)) {
            RemoteFallbacks.EnsureRepoFile(Path.Combine("RemakeRegistry", "register.json"), modulesRel);
        }

        _gamesRegistryPath = gamesRel;
        _modulesRegistryPath = modulesRel;
        _modules = EngineConfig.LoadJsonFile(_modulesRegistryPath);
    }

    public void RefreshModules() => _modules = EngineConfig.LoadJsonFile(_modulesRegistryPath);

    public IReadOnlyDictionary<String, Object?> GetRegisteredModules() {
        return _modules.TryGetValue("modules", out Object? m) && m is Dictionary<String, Object?> dict ? dict : new Dictionary<String, Object?>();
    }

    public Dictionary<String, GameInfo> DiscoverGames() {
        Dictionary<String, GameInfo> games = new Dictionary<String, GameInfo>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_gamesRegistryPath)) {
            return games;
        }

        foreach (String dir in Directory.EnumerateDirectories(_gamesRegistryPath)) {
            String opsToml = Path.Combine(dir, "operations.toml");
            String opsJson = Path.Combine(dir, "operations.json");
            String? ops = null;
            if (File.Exists(opsToml)) {
                ops = opsToml;
            } else if (File.Exists(opsJson)) {
                ops = opsJson;
            }

            if (ops is null) {
                continue;
            }

            String name = new DirectoryInfo(dir).Name;
            games[name] = new GameInfo(
                opsFile: Path.GetFullPath(ops),
                gameRoot: Path.GetFullPath(dir)
            );
        }
        return games;
    }


    public Dictionary<String, GameInfo> DiscoverInstalledGames()
	{
        Dictionary<String, GameInfo> games = new Dictionary<String, GameInfo>(StringComparer.OrdinalIgnoreCase);
		if (!Directory.Exists(_gamesRegistryPath)) {
            return games;
        }

        foreach (String dir in Directory.EnumerateDirectories(_gamesRegistryPath))
                {
            String opsToml = Path.Combine(dir, "operations.toml");
            String opsJson = Path.Combine(dir, "operations.json");
            String? ops = null;
                        if (File.Exists(opsToml)) {
                ops = opsToml;
            } else if (File.Exists(opsJson)) {
                ops = opsJson;
            }

            if (ops is null) {
                continue;
            }

            String gameToml = Path.Combine(dir, "game.toml");
			if (!File.Exists(gameToml)) {
                continue; // not installed – requires a valid game.toml
            }

            // Parse a minimal subset of TOML: top-level key = "value" pairs
            String? exePath = null;
            String? title = null;
			try
			{
				foreach (String raw in File.ReadAllLines(gameToml))
				{
                    String line = raw.Trim();
					if (line.Length == 0 || line.StartsWith("#")) {
                        continue;
                    }
                    // ignore tables/arrays
                    if (line.StartsWith("[") && line.EndsWith("]")) {
                        continue;
                    }

                    Int32 eq = line.IndexOf('=');
					if (eq <= 0) {
                        continue;
                    }

                    String key = line.Substring(0, eq).Trim();
                    String valRaw = line.Substring(eq + 1).Trim();
                    String? val = valRaw.StartsWith("\"") && valRaw.EndsWith("\"") ? valRaw.Substring(1, valRaw.Length - 2) : valRaw;

                    if (key.Equals("exe", StringComparison.OrdinalIgnoreCase) || key.Equals("executable", StringComparison.OrdinalIgnoreCase)) {
                        exePath = val;
                    } else if (key.Equals("title", StringComparison.OrdinalIgnoreCase) || key.Equals("name", StringComparison.OrdinalIgnoreCase)) {
                        title = val;
                    }
                }
			}
			catch
			{
				// malformed game.toml – reject
				continue;
			}

			if (String.IsNullOrWhiteSpace(exePath)) {
                continue;
            }

            // Resolve and validate executable
            String exeFull = Path.IsPathRooted(exePath!) ? exePath! : Path.Combine(dir, exePath!);
			if (!File.Exists(exeFull)) {
                continue; // exe missing – not installed
            }

            String name = new DirectoryInfo(dir).Name;
			games[name] = new GameInfo(
				opsFile: Path.GetFullPath(ops),
				gameRoot: Path.GetFullPath(dir),
				exePath: Path.GetFullPath(exeFull),
				title: title
			);
		}

		return games;
	}
}
