using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Tomlyn.Model;

using EngineNet.Core.Serialization.Json;

namespace EngineNet.Core.Utils;

internal sealed partial class Registries {
    internal void RefreshModules() => _modules = Core.Serialization.Json.JsonHelpers.LoadJsonFile(_modulesRegistryPath);

    internal IReadOnlyDictionary<string, object?> GetRegisteredModules() {
        return _modules.TryGetValue("modules", out object? m) && m is Dictionary<string, object?> dict ? dict : new Dictionary<string, object?>();
    }

    internal Dictionary<string, GameInfo> DiscoverGames() {
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

    internal Dictionary<string, GameInfo> DiscoverBuiltGames() {
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
                Core.Diagnostics.Trace($"[GameRegistry] warning: game '{new System.IO.DirectoryInfo(dir).Name}' is missing game.toml - skipping");
                continue; // not installed - requires a valid game.toml
            }

            // Parse a minimal subset of TOML: top-level key = "value" pairs
            string? exePath = null;
            string? luaPath = null;
            string? godotPath = null;
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
                    } else if (key.Equals("lua", System.StringComparison.OrdinalIgnoreCase) || key.Equals("lua_script", System.StringComparison.OrdinalIgnoreCase) || key.Equals("script", System.StringComparison.OrdinalIgnoreCase)) {
                        luaPath = val;
                    } else if (key.Equals("godot", System.StringComparison.OrdinalIgnoreCase) || key.Equals("godot_project", System.StringComparison.OrdinalIgnoreCase) || key.Equals("project", System.StringComparison.OrdinalIgnoreCase)) {
                        godotPath = val;
                    } else if (key.Equals("title", System.StringComparison.OrdinalIgnoreCase) || key.Equals("name", System.StringComparison.OrdinalIgnoreCase)) {
                        title = val;
                    }
                }
            } catch {
                Core.Diagnostics.Bug($"[GameRegistry] err parsing game.toml for game '{new System.IO.DirectoryInfo(dir).Name}' - skipping");
                // malformed game.toml - reject
                continue;
            }

            // Prepare resolution context
            var ctx = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase) {
                ["Game_Root"] = dir,
                ["Project_Root"] = Program.rootPath
            };

            // Resolve and validate entry points (Priority: EXE > Lua > Godot)
            string? finalEntryPoint = null;
            if (!string.IsNullOrWhiteSpace(exePath)) {
                string resolved = Placeholders.Resolve(exePath, ctx)?.ToString() ?? exePath;
                string full = PathHelper.ResolveRelativePath(dir, resolved);
                if (System.IO.File.Exists(full)) finalEntryPoint = full;
            }
            if (finalEntryPoint == null && !string.IsNullOrWhiteSpace(luaPath)) {
                string resolved = Placeholders.Resolve(luaPath, ctx)?.ToString() ?? luaPath;
                string full = PathHelper.ResolveRelativePath(dir, resolved);
                if (System.IO.File.Exists(full)) finalEntryPoint = full;
            }
            if (finalEntryPoint == null && !string.IsNullOrWhiteSpace(godotPath)) {
                string resolved = Placeholders.Resolve(godotPath, ctx)?.ToString() ?? godotPath;
                string full = PathHelper.ResolveRelativePath(dir, resolved);
                // Godot project can be a file or a directory (if it contains project.godot)
                if (System.IO.File.Exists(full) || System.IO.Directory.Exists(full)) finalEntryPoint = full;
            }

            if (string.IsNullOrWhiteSpace(finalEntryPoint)) {
                continue; // No valid runnable entry point found
            }

            title = Placeholders.Resolve(title, ctx)?.ToString();

            string name = new System.IO.DirectoryInfo(dir).Name;
            games[name] = new GameInfo(
                opsFile: System.IO.Path.GetFullPath(ops),
                gameRoot: System.IO.Path.GetFullPath(dir),
                exePath: System.IO.Path.GetFullPath(finalEntryPoint),
                title: title
            );
        }

        return games;
    }
}
