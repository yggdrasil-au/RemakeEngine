using System;
using System.Collections.Generic;
using System.IO;
using EngineNet.Tools;

namespace EngineNet.Core.Sys;

internal sealed class Registries {
    private readonly string _rootPath;
    private readonly string _gamesRegistryPath;
    private readonly string _modulesRegistryPath;

    private Dictionary<string, object?> _modules = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);

    public Registries(string rootPath) {
        _rootPath = rootPath;

        // Preferred locations (relative to working root)
        string gamesRel = System.IO.Path.Combine(rootPath, "RemakeRegistry", "Games");
        string modulesRel = System.IO.Path.Combine(rootPath, "RemakeRegistry", "register.json");

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(modulesRel) ?? rootPath);

        // If modules registry JSON is missing, try to download from GitHub repo
        if (!System.IO.File.Exists(modulesRel)) {
            RemoteFallbacks.EnsureRepoFile(System.IO.Path.Combine("RemakeRegistry", "register.json"), modulesRel);
        }

        _gamesRegistryPath = gamesRel;
        _modulesRegistryPath = modulesRel;
        _modules = EngineConfig.LoadJsonFile(_modulesRegistryPath);
    }

    public void RefreshModules() => _modules = EngineConfig.LoadJsonFile(_modulesRegistryPath);

    public IReadOnlyDictionary<string, object?> GetRegisteredModules() {
        return _modules.TryGetValue("modules", out object? m) && m is Dictionary<string, object?> dict ? dict : new Dictionary<string, object?>();
    }

    public Dictionary<string, GameInfo> DiscoverGames() {
        Dictionary<string, GameInfo> games = new Dictionary<string, GameInfo>(System.StringComparer.OrdinalIgnoreCase);
        if (!System.IO.Directory.Exists(_gamesRegistryPath)) {
            return games;
        }

        foreach (string dir in System.IO.Directory.EnumerateDirectories(_gamesRegistryPath)) {
            string opsToml = System.IO.Path.Combine(dir, "operations.toml");
            string opsJson = System.IO.Path.Combine(dir, "operations.json");
            string? ops = null;
            if (System.IO.File.Exists(opsToml)) {
                ops = opsToml;
            } else if (System.IO.File.Exists(opsJson)) {
                ops = opsJson;
            }

            if (ops is null) {
                continue;
            }

            string name = new System.IO.DirectoryInfo(dir).Name;
            games[name] = new GameInfo(
                opsFile: System.IO.Path.GetFullPath(ops),
                gameRoot: System.IO.Path.GetFullPath(dir)
            );
        }
        return games;
    }


    public Dictionary<string, GameInfo> DiscoverBuiltGames() {
        Dictionary<string, GameInfo> games = new Dictionary<string, GameInfo>(System.StringComparer.OrdinalIgnoreCase);
        if (!System.IO.Directory.Exists(_gamesRegistryPath)) {
            return games;
        }

        foreach (string dir in System.IO.Directory.EnumerateDirectories(_gamesRegistryPath)) {
            string opsToml = System.IO.Path.Combine(dir, "operations.toml");
            string opsJson = System.IO.Path.Combine(dir, "operations.json");
            string? ops = null;
            if (System.IO.File.Exists(opsToml)) {
                ops = opsToml;
            } else if (System.IO.File.Exists(opsJson)) {
                ops = opsJson;
            }

            if (ops is null) {
                continue;
            }

            string gameToml = System.IO.Path.Combine(dir, "game.toml");
            if (!System.IO.File.Exists(gameToml)) {
                continue; // not installed - requires a valid game.toml
            }

            // Parse a minimal subset of TOML: top-level key = "value" pairs
            string? exePath = null;
            string? title = null;
            try {
                foreach (string raw in System.IO.File.ReadAllLines(gameToml)) {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) {
                        continue;
                    }
                    // ignore tables/arrays
                    if (line.StartsWith("[") && line.EndsWith("]")) {
                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (eq <= 0) {
                        continue;
                    }

                    string key = line.Substring(0, eq).Trim();
                    string valRaw = line.Substring(eq + 1).Trim();
                    string? val = valRaw.StartsWith("\"") && valRaw.EndsWith("\"") ? valRaw.Substring(1, valRaw.Length - 2) : valRaw;

                    if (key.Equals("exe", System.StringComparison.OrdinalIgnoreCase) || key.Equals("executable", System.StringComparison.OrdinalIgnoreCase)) {
                        exePath = val;
                    } else if (key.Equals("title", System.StringComparison.OrdinalIgnoreCase) || key.Equals("name", System.StringComparison.OrdinalIgnoreCase)) {
                        title = val;
                    }
                }
            } catch {
                // malformed game.toml - reject
                continue;
            }

            if (string.IsNullOrWhiteSpace(exePath)) {
                continue;
            }

            // Resolve and validate executable
            string exeFull = System.IO.Path.IsPathRooted(exePath!) ? exePath! : System.IO.Path.Combine(dir, exePath!);
            if (!System.IO.File.Exists(exeFull)) {
                continue; // exe missing - not installed
            }

            string name = new System.IO.DirectoryInfo(dir).Name;
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
