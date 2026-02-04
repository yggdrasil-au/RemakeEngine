namespace EngineNet.Core.Utils;

using System.Collections.Generic;

/// <summary>
/// Builds the placeholder/variable context used by command building and embedded actions,
/// including module placeholders merged from config.toml.
/// </summary>
internal sealed class ExecutionContextBuilder {


    internal ExecutionContextBuilder() {
        //
    }

    internal Dictionary<string, object?> Build(
        string currentGame,
        Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games,
        IDictionary<string, object?> engineConfig) {
        if (string.IsNullOrWhiteSpace(currentGame)) {
            throw new System.ArgumentException(message: "No game has been loaded.", paramName: nameof(currentGame));
        }

        Dictionary<string, object?> ctx = new Dictionary<string, object?>(engineConfig, System.StringComparer.OrdinalIgnoreCase);

        if (!games.TryGetValue(currentGame, out EngineNet.Core.Utils.GameModuleInfo? g) || g is not GameModuleInfo gdict) {
            throw new KeyNotFoundException(message: $"Unknown game '{currentGame}'.");
        }

        ctx[key: "Game_Root"] = gdict.GameRoot;
        ctx[key: "Project_Root"] = Program.rootPath;
        ctx[key: "Registry_Root"] = System.IO.Path.Combine(Program.rootPath, "EngineApps");
        ctx[key: "Game"] = new Dictionary<string, object?> {
            [key: "RootPath"] = gdict.GameRoot,
            [key: "Name"] = currentGame,
        };

        if (!ctx.TryGetValue(key: "RemakeEngine", out object? re) || re is not IDictionary<string, object?> reDict) {
            ctx[key: "RemakeEngine"] = reDict = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        }
        if (!reDict.TryGetValue(key: "Config", out object? cfg) || cfg is not IDictionary<string, object?> cfgDict) {
            reDict[key: "Config"] = cfgDict = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        }
        cfgDict[key: "module_path"] = gdict.GameRoot;
        cfgDict[key: "project_path"] = Program.rootPath;

        try {
            string cfgPath = System.IO.Path.Combine(gdict.GameRoot, "config.toml");
            if (!string.IsNullOrWhiteSpace(gdict.GameRoot) && System.IO.File.Exists(cfgPath)) {
                Dictionary<string, object?> fromToml = ExternalTools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                foreach (KeyValuePair<string, object?> kv in fromToml) {
                    if (!ctx.ContainsKey(kv.Key)) {
                        ctx[kv.Key] = kv.Value;
                    }
                }
            }
        } catch { /* ignore bad/missing toml */ }

        return ctx;
    }
}

