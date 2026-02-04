
using System.Collections.Generic;

namespace EngineNet.Core.Engine.operations.Built_inActions;
public partial class InternalOperations {

    internal bool rename_folders(IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers, string currentGame, Dictionary<string, Core.Utils.GameModuleInfo> games, string RootPath,  EngineConfig EngineConfig) {
        Dictionary<string, object?> ctx = new Dictionary<string, object?>(EngineConfig.Data, System.StringComparer.OrdinalIgnoreCase);
        if (!games.TryGetValue(currentGame, out Core.Utils.GameModuleInfo? gobj3)) {
            throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
        }
        // Built-in placeholders
        string gameRoot5 = gobj3.GameRoot;
        ctx["Game_Root"] = gameRoot5;
        ctx["Project_Root"] = RootPath;
        ctx["Registry_Root"] = System.IO.Path.Combine(RootPath, "EngineApps");
        ctx["Game"] = new Dictionary<string, object?> {
            ["RootPath"] = gameRoot5,
            ["Name"] = currentGame,
        };
        if (!ctx.TryGetValue("RemakeEngine", out object? re4) || re4 is not IDictionary<string, object?> reDict4) {
            ctx["RemakeEngine"] = reDict4 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        }

        if (!reDict4.TryGetValue("Config", out object? cfg4) || cfg4 is not IDictionary<string, object?> cfgDict4) {
            reDict4["Config"] = cfgDict4 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        }
        // Merge module placeholders from config.toml
        try {
            string cfgPath = System.IO.Path.Combine(gameRoot5, "config.toml");
            if (!string.IsNullOrWhiteSpace(gameRoot5) && System.IO.File.Exists(cfgPath)) {
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
        cfgDict4["module_path"] = gameRoot5;
        cfgDict4["project_path"] = RootPath;

        List<string> args = new List<string>();
        if (op.TryGetValue("args", out object? aobjRename) && aobjRename is System.Collections.IList aListRename) {
            object? resolvedObj = Core.Utils.Placeholders.Resolve(aobjRename, ctx);
            System.Collections.IList resolved = resolvedObj as System.Collections.IList ?? aListRename;
            foreach (object? a in resolved) {
                if (a is not null) {
                    args.Add(a.ToString()!);
                }
            }
        }

        Core.UI.EngineSdk.PrintLine("\n>>> Built-in folder rename");
        Core.UI.EngineSdk.PrintLine($"with args: {string.Join(' ', args)}");
        bool okRename = FileHandlers.FolderRenamer.Run(args);
        return okRename;
    }
}
