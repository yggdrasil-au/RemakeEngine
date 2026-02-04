using System.Collections.Generic;

namespace EngineNet.Core.Engine.operations.Built_inActions;
public partial class InternalOperations {

    internal async System.Threading.Tasks.Task<bool> DownloadTools(IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers, string currentGame, Dictionary<string, Core.Utils.GameModuleInfo> games, string RootPath,  EngineConfig EngineConfig) {
        // Expect a 'tools_manifest' value (path), or fallback to first arg
        string? manifest = null;
        if (op.TryGetValue("tools_manifest", out object? tm) && tm is not null) {
            manifest = tm.ToString();
        } else if (op.TryGetValue("args", out object? argsObj) && argsObj is IList<object?> list && list.Count > 0) {
            manifest = list[0]?.ToString();
        }

        if (string.IsNullOrWhiteSpace(manifest)) {
            return false;
        }

        Dictionary<string, object?> ctx = new Dictionary<string, object?>(EngineConfig.Data, System.StringComparer.OrdinalIgnoreCase);
        if (!games.TryGetValue(currentGame, out Core.Utils.GameModuleInfo? gobj)) {
            throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
        }
        // Built-in placeholders
        string gameRoot = gobj.GameRoot;
        ctx["Game_Root"] = gameRoot;
        ctx["Project_Root"] = RootPath;
        ctx["Registry_Root"] = System.IO.Path.Combine(RootPath, "EngineApps");
        ctx["Game"] = new Dictionary<string, object?> {
            ["RootPath"] = gameRoot,
            ["Name"] = currentGame,
        };
        // Ensure RemakeEngine dictionary exists
        if (!ctx.TryGetValue("RemakeEngine", out object? re0) || re0 is not IDictionary<string, object?> reDict0) {
            ctx["RemakeEngine"] = reDict0 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
            Core.Diagnostics.Log("[Engine.private.cs :: Operations()]] Created RemakeEngine dictionary in placeholders context");
        }
        // Ensure Config dictionary exists
        if (!reDict0.TryGetValue("Config", out object? cfg0) || cfg0 is not IDictionary<string, object?> cfgDict0) {
            reDict0["Config"] = cfgDict0 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
            Core.Diagnostics.Log("[Engine.private.cs :: Operations()]] Created RemakeEngine.Config dictionary in placeholders context");
        }
        // Merge module placeholders from config.toml
        try {
            string cfgPath = System.IO.Path.Combine(gameRoot, "config.toml");
            if (!string.IsNullOrWhiteSpace(gameRoot) && System.IO.File.Exists(cfgPath)) {
                Dictionary<string, object?> fromToml = Core.ExternalTools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                foreach (KeyValuePair<string, object?> kv in fromToml) {
                    if (!ctx.ContainsKey(kv.Key)) {
                        ctx[kv.Key] = kv.Value;
                    }
                }
            }
        }  catch (System.Exception ex) {
            Core.Diagnostics.Bug($"[Engine.cs] err reading config.toml: {ex.Message}");
        }
        cfgDict0["module_path"] = gameRoot;
        cfgDict0["project_path"] = RootPath;
        string resolvedManifest = Core.Utils.Placeholders.Resolve(manifest!, ctx)?.ToString() ?? manifest!;
        string central = System.IO.Path.Combine(RootPath, "EngineApps", "Registries", "Tools", "Main.json");
        bool force = false;
        if (promptAnswers.TryGetValue("force download", out object? fd) && fd is bool b1) {
            force = b1;
        }

        if (promptAnswers.TryGetValue("force_download", out object? fd2) && fd2 is bool b2) {
            force = b2;
        }

        ExternalTools.ToolsDownloader dl = new ExternalTools.ToolsDownloader(RootPath, central);
        await dl.ProcessAsync(resolvedManifest, force, ctx);
        return true;
    }
}