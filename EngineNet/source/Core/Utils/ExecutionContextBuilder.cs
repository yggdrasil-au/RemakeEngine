

using EngineNet.Shared.Serialization.Toml;

namespace EngineNet.Core.Utils;


/// <summary>
/// Builds the placeholder/variable context used by command building and embedded actions,
/// including module placeholders merged from config.toml.
/// </summary>
internal static class ExecutionContextBuilder {

    /// <summary>
    /// Builds the execution context for a given game, merging engine config and module-specific placeholders.
    /// The resulting dictionary is used for resolving placeholders in commands and operations.
    /// </summary>
    /// <param name="currentGame">The canonical game/module id currently selected; must be a key in <paramref name="games"/>.</param>
    /// <param name="games">Map of games with metadata; must contain <paramref name="currentGame"/>.</param>
    /// <param name="engineConfig">Engine configuration dictionary exposed to placeholder resolution; values are copied into the context with case-insensitive keys.</param>
    /// <returns>A dictionary representing the execution context for the specified game, including merged placeholders from config.toml.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="currentGame"/> is empty.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when the game specified by <paramref name="currentGame"/> is not found in <paramref name="games"/>.</exception>
    internal static Dictionary<string, object?> Build(
        string currentGame,
        Dictionary<string, EngineNet.Core.Data.GameModuleInfo> games,
        IDictionary<string, object?> engineConfig
    ) {
        if (string.IsNullOrWhiteSpace(currentGame)) {
            throw new System.ArgumentException(message: "No game has been loaded.", paramName: nameof(currentGame));
        }

        Dictionary<string, object?> ctx = new Dictionary<string, object?>(engineConfig, System.StringComparer.OrdinalIgnoreCase);

        if (!games.TryGetValue(currentGame, out EngineNet.Core.Data.GameModuleInfo? g) || g is not Data.GameModuleInfo gdict) {
            throw new KeyNotFoundException(message: $"Unknown game '{currentGame}'.");
        }

        ctx[key: "Game_Root"] = gdict.GameRoot;
        ctx[key: "Project_Root"] = EngineNet.Core.Main.RootPath;
        ctx[key: "Registry_Root"] = System.IO.Path.Combine(EngineNet.Core.Main.RootPath, "EngineApps");
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
        cfgDict[key: "project_path"] = EngineNet.Core.Main.RootPath;

        try {
            string cfgPath = System.IO.Path.Combine(gdict.GameRoot, "config.toml");
            if (!string.IsNullOrWhiteSpace(gdict.GameRoot) && System.IO.File.Exists(cfgPath)) {
                Dictionary<string, object?> fromToml = TomlHelpers.ReadPlaceholdersFile(cfgPath);
                foreach (KeyValuePair<string, object?> kv in fromToml) {
                    ctx[kv.Key] = kv.Value;
                }
            }
        } catch {
            Shared.IO.Diagnostics.Bug($"[ExecutionContextBuilder] err reading config.toml for game '{currentGame}' at expected path '{System.IO.Path.Combine(gdict.GameRoot, "config.toml")}'.");
            /* ignore bad/missing toml */
        }

        return ctx;
    }
}

