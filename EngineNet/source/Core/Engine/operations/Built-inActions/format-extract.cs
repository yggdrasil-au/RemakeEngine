
using System.Collections.Generic;

namespace EngineNet.Core.Engine.operations.Built_inActions;
public partial class InternalOperations {

    internal bool format_extract(IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers, string currentGame, Dictionary<string, Core.Utils.GameModuleInfo> games, string RootPath,  EngineConfig EngineConfig) {
        // Determine input file format
        string? format = op.TryGetValue("format", out object? ft) ? ft?.ToString()?.ToLowerInvariant() : null;

        // Resolve args (used for both TXD and media conversions)
        Dictionary<string, object?> ctx = new Dictionary<string, object?>(EngineConfig.Data, System.StringComparer.OrdinalIgnoreCase);
        if (!games.TryGetValue(currentGame, out Core.Utils.GameModuleInfo? gobj)) {
            throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
        }
        // Built-in placeholders
        string gameRoot2 = gobj.GameRoot;
        ctx["Game_Root"] = gameRoot2;
        ctx["Project_Root"] = RootPath;
        ctx["Registry_Root"] = System.IO.Path.Combine(RootPath, "EngineApps");
        ctx["Game"] = new Dictionary<string, object?> {
            ["RootPath"] = gameRoot2,
            ["Name"] = currentGame,
        };
        if (!ctx.TryGetValue("RemakeEngine", out object? re1) || re1 is not IDictionary<string, object?> reDict1) {
            ctx["RemakeEngine"] = reDict1 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        }

        if (!reDict1.TryGetValue("Config", out object? cfg1) || cfg1 is not IDictionary<string, object?> cfgDict1) {
            reDict1["Config"] = cfgDict1 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        }
        // Merge module placeholders from config.toml
        try {
            string cfgPath = System.IO.Path.Combine(gameRoot2, "config.toml");
            if (!string.IsNullOrWhiteSpace(gameRoot2) && System.IO.File.Exists(cfgPath)) {
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
        cfgDict1["module_path"] = gameRoot2;
        cfgDict1["project_path"] = RootPath;

        List<string> args = new List<string>();
        if (op.TryGetValue("args", out object? aobj) && aobj is System.Collections.IList aList) {
            object? resolvedObj = Core.Utils.Placeholders.Resolve(aobj, ctx);
            System.Collections.IList resolved = resolvedObj as System.Collections.IList ?? aList;
            foreach (object? a in resolved) {
                if (a is not null) {
                    args.Add(a.ToString()!);
                }
            }
        }

        // If format is TXD, use built-in extractor

        if (string.Equals(format, "txd", System.StringComparison.OrdinalIgnoreCase)) {
            Core.UI.EngineSdk.PrintLine("\n>>> Built-in TXD extraction");
            bool okTxd = FileHandlers.TxdExtractor.Main.Run(args);
            return okTxd;
        } else if (string.IsNullOrWhiteSpace(format)) {
            // Auto-detect format - default to TXD for now
            Core.UI.EngineSdk.PrintLine("\n>>> Built-in TXD extraction (auto-detected)");
            bool okTxd = FileHandlers.TxdExtractor.Main.Run(args);
            return okTxd;
        } else {
            Core.UI.EngineSdk.PrintLine($"ERROR: format-extract does not support format '{format}'");
            Core.UI.EngineSdk.PrintLine("Supported formats: txd (default)");
            return false;
        }
    }
}
