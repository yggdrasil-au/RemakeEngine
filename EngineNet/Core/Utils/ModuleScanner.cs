namespace EngineNet.Core.Utils;

using System.Collections.Generic;

/// <summary>
/// Scans registry and file system to produce a consistent view of modules with status flags.
/// </summary>
internal sealed class ModuleScanner {
    private readonly string _rootPath;
    private readonly Registries _registries;

    internal ModuleScanner(string rootPath, Registries registries) {
        _rootPath = rootPath;
        _registries = registries;
    }

    internal Dictionary<string, GameModuleInfo> ListModules() {
        Dictionary<string, GameModuleInfo> result = new Dictionary<string, GameModuleInfo>(System.StringComparer.OrdinalIgnoreCase);

        System.Collections.Generic.IReadOnlyDictionary<string, object?> registered = _registries.GetRegisteredModules();
        foreach (System.Collections.Generic.KeyValuePair<string, object?> kv in registered) {
            GameModuleInfo info = new GameModuleInfo {
                Name = kv.Key,
                IsRegistered = true
            };
            result[kv.Key] = info;
        }

        string gamesDir = System.IO.Path.Combine(_rootPath, "EngineApps", "Games");
        if (System.IO.Directory.Exists(gamesDir)) {
            foreach (string dir in System.IO.Directory.EnumerateDirectories(gamesDir)) {
                string name = new System.IO.DirectoryInfo(dir).Name;
                GameModuleInfo info;
                if (!result.TryGetValue(name, out info!)) {
                    info = new GameModuleInfo { Name = name };
                    result[name] = info;
                }
                info.GameRoot = System.IO.Path.GetFullPath(dir);
                info.IsInstalled = true;

                // Ops file
                string opsToml = System.IO.Path.Combine(dir, "operations.toml");
                string opsJson = System.IO.Path.Combine(dir, "operations.json");
                if (System.IO.File.Exists(opsToml)) info.OpsFile = System.IO.Path.GetFullPath(opsToml);
                else if (System.IO.File.Exists(opsJson)) info.OpsFile = System.IO.Path.GetFullPath(opsJson);

                // game.toml basic parse for built
                string gameToml = System.IO.Path.Combine(dir, "game.toml");
                if (System.IO.File.Exists(gameToml)) {
                    info.IsBuilt = true;
                    try {
                        foreach (string raw in System.IO.File.ReadAllLines(gameToml)) {
                            string line = raw.Trim();
                            if (line.Length == 0 || line.StartsWith("#")) continue;
                            if (line.StartsWith("[")) continue; // skip tables
                            int eq = line.IndexOf('=');
                            if (eq <= 0) continue;
                            string key = line.Substring(0, eq).Trim();
                            string valRaw = line.Substring(eq + 1).Trim();
                            string? val = valRaw.StartsWith("\"") && valRaw.EndsWith("\"") ? valRaw.Substring(1, valRaw.Length - 2) : valRaw;
                            if (key.Equals("exe", System.StringComparison.OrdinalIgnoreCase) || key.Equals("executable", System.StringComparison.OrdinalIgnoreCase)) {
                                info.ExePath = ResolveUnder(dir, val);
                            } else if (key.Equals("title", System.StringComparison.OrdinalIgnoreCase) || key.Equals("name", System.StringComparison.OrdinalIgnoreCase)) {
                                info.Title = val;
                            }
                        }
                    } catch { /* ignore malformed */ }
                }

                // Unverified if installed but not in registry
                if (info.IsInstalled && !info.IsRegistered) {
                    info.IsUnverified = true;
                }
            }
        }

        // Fill game root/ops for registered-only entries if missing
        foreach (System.Collections.Generic.KeyValuePair<string, GameModuleInfo> kv in result) {
            if (string.IsNullOrWhiteSpace(kv.Value.GameRoot)) {
                string potential = System.IO.Path.Combine(gamesDir, kv.Key);
                if (System.IO.Directory.Exists(potential)) {
                    kv.Value.GameRoot = System.IO.Path.GetFullPath(potential);
                    kv.Value.IsInstalled = true;
                }
            }
        }

        return result;
    }

    private static string? ResolveUnder(string dir, string? path) {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return System.IO.Path.IsPathRooted(path!) ? path : System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, path!));
    }
}

