
using System;
using System.IO;
using System.Collections.Generic;


namespace EngineNet.Core;

public sealed partial class OperationsEngine {
    public Dictionary<String, Object?> ListGames() {
        Dictionary<String, Object?> games = new Dictionary<String, Object?>();
        // Also look up installed games to enrich entries with exe/title when available
        Dictionary<String, Sys.GameInfo> installed = _registries.DiscoverInstalledGames();
        foreach (KeyValuePair<String, Sys.GameInfo> kv in _registries.DiscoverGames()) {
            Dictionary<String, Object?> info = new Dictionary<String, Object?> {
                ["game_root"] = kv.Value.GameRoot,
                ["ops_file"] = kv.Value.OpsFile
            };
            if (installed.TryGetValue(kv.Key, out Sys.GameInfo? gi)) {
                if (!String.IsNullOrWhiteSpace(gi.ExePath))
                    info["exe"] = gi.ExePath;
                if (!String.IsNullOrWhiteSpace(gi.Title))
                    info["title"] = gi.Title;
            }
            games[kv.Key] = info;
        }
        return games;
    }

    public Dictionary<String, Object?> GetInstalledGames() {
        Dictionary<String, Object?> games = new Dictionary<String, Object?>();
        foreach (KeyValuePair<String, Sys.GameInfo> kv in _registries.DiscoverInstalledGames()) {
            Dictionary<String, Object?> info = new Dictionary<String, Object?> {
                ["game_root"] = kv.Value.GameRoot,
                ["ops_file"] = kv.Value.OpsFile
            };
            if (!String.IsNullOrWhiteSpace(kv.Value.ExePath))
                info["exe"] = kv.Value.ExePath;
            if (!String.IsNullOrWhiteSpace(kv.Value.Title))
                info["title"] = kv.Value.Title;
            games[kv.Key] = info;
        }
        return games;
    }

    public IReadOnlyDictionary<String, Object?> GetRegisteredModules() {
        return _registries.GetRegisteredModules();
    }

    public Boolean IsModuleInstalled(String name)
    {
        Dictionary<String, Sys.GameInfo> games = _registries.DiscoverInstalledGames();
        return games.ContainsKey(name);
    }

    public String? GetGameExecutable(String name) {
        Dictionary<String, Sys.GameInfo> games = _registries.DiscoverInstalledGames();
        return games.TryGetValue(name, out Sys.GameInfo? gi) ? gi.ExePath : null;
    }

    public String? GetGamePath(String name) {
        // Prefer installed location first, then fall back to downloaded location
        Dictionary<String, Sys.GameInfo> games = _registries.DiscoverInstalledGames();
        if (games.TryGetValue(name, out Sys.GameInfo? gi))
            return gi.GameRoot;
        String dir = Path.Combine(_rootPath, "RemakeRegistry", "Games", name);
        return Directory.Exists(dir) ? dir : null;
    }

    public Boolean LaunchGame(String name) {
        String? exe = GetGameExecutable(name);
        String root = GetGamePath(name) ?? _rootPath;
        if (String.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            return false;

        String? launchOverride = Environment.GetEnvironmentVariable("ENGINE_NET_TEST_LAUNCH_OVERRIDE");
        if (!String.IsNullOrEmpty(launchOverride))
            return String.Equals(launchOverride, "success", StringComparison.OrdinalIgnoreCase);

        try {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
                FileName = exe!,
                WorkingDirectory = root!,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            return true;
        } catch {
            return false;
        }
    }

    public String GetModuleState(String name) {
        String dir = Path.Combine(_rootPath, "RemakeRegistry", "Games", name);
        return !Directory.Exists(dir) ? "not_downloaded" : IsModuleInstalled(name) ? "installed" : "downloaded";
    }

}
