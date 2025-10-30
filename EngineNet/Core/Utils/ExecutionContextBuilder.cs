namespace EngineNet.Core.Utils;

using System.Collections.Generic;

/// <summary>
/// Builds the placeholder/variable context used by command building and embedded actions,
/// including module placeholders merged from config.toml.
/// </summary>
internal sealed class ExecutionContextBuilder {
    private readonly string _rootPath;

    internal ExecutionContextBuilder(string rootPath) {
        _rootPath = rootPath;
    }

    internal Dictionary<string, object?> Build(
        string currentGame,
        IDictionary<string, object?> games,
        IDictionary<string, object?> engineConfig) {
        if (string.IsNullOrWhiteSpace(currentGame)) {
            throw new System.ArgumentException(message: "No game has been loaded.", paramName: nameof(currentGame));
        }

        Dictionary<string, object?> ctx = new Dictionary<string, object?>(engineConfig, System.StringComparer.OrdinalIgnoreCase);

        if (!games.TryGetValue(currentGame, out object? g) || g is not IDictionary<string, object?> gdict) {
            throw new System.Collections.Generic.KeyNotFoundException(message: $"Unknown game '{currentGame}'.");
        }

        string gameRoot = gdict.TryGetValue(key: "game_root", out object? gr) ? gr?.ToString() ?? string.Empty : string.Empty;
        ctx[key: "Game_Root"] = gameRoot;
        ctx[key: "Project_Root"] = _rootPath;
        ctx[key: "Registry_Root"] = System.IO.Path.Combine(_rootPath, "EngineApps");
        ctx[key: "Game"] = new Dictionary<string, object?> {
            [key: "RootPath"] = gameRoot,
            [key: "Name"] = currentGame,
        };

        if (!ctx.TryGetValue(key: "RemakeEngine", out object? re) || re is not IDictionary<string, object?> reDict) {
            ctx[key: "RemakeEngine"] = reDict = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        }
        if (!reDict.TryGetValue(key: "Config", out object? cfg) || cfg is not IDictionary<string, object?> cfgDict) {
            reDict[key: "Config"] = cfgDict = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        }
        cfgDict[key: "module_path"] = gameRoot;
        cfgDict[key: "project_path"] = _rootPath;

        try {
            string cfgPath = System.IO.Path.Combine(gameRoot, "config.toml");
            if (!string.IsNullOrWhiteSpace(gameRoot) && System.IO.File.Exists(cfgPath)) {
                Dictionary<string, object?> fromToml = Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                foreach (System.Collections.Generic.KeyValuePair<string, object?> kv in fromToml) {
                    if (!ctx.ContainsKey(kv.Key)) {
                        ctx[kv.Key] = kv.Value;
                    }
                }
            }
        } catch { /* ignore bad/missing toml */ }

        return ctx;
    }
}

