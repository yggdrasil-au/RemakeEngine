using System.Collections.Generic;

namespace EngineNet.Core;
internal partial class OperationExecution {

    internal bool validate_files(IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers, string currentGame, Dictionary<string, Core.Utils.GameModuleInfo> games, string RootPath,  EngineConfig EngineConfig) {
        Dictionary<string, object?> ctx = new Dictionary<string, object?>(EngineConfig.Data, System.StringComparer.OrdinalIgnoreCase);
        if (!games.TryGetValue(currentGame, out Core.Utils.GameModuleInfo? gobjValidate)) {
            throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
        }
        // Built-in placeholders
        string gameRoot4 = gobjValidate.GameRoot;
        ctx["Game_Root"] = gameRoot4;
        ctx["Project_Root"] = RootPath;
        ctx["Registry_Root"] = System.IO.Path.Combine(RootPath, "EngineApps");
        ctx["Game"] = new Dictionary<string, object?> {
            ["RootPath"] = gameRoot4,
            ["Name"] = currentGame,
        };
        if (!ctx.TryGetValue("RemakeEngine", out object? re3) || re3 is not IDictionary<string, object?> reDict3) {
            ctx["RemakeEngine"] = reDict3 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        }

        if (!reDict3.TryGetValue("Config", out object? cfg3) || cfg3 is not IDictionary<string, object?> cfgDict3) {
            reDict3["Config"] = cfgDict3 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        }
        // Merge module placeholders from config.toml
        try {
            string cfgPath = System.IO.Path.Combine(gameRoot4, "config.toml");
            if (!string.IsNullOrWhiteSpace(gameRoot4) && System.IO.File.Exists(cfgPath)) {
                Dictionary<string, object?> fromToml = Core.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                foreach (KeyValuePair<string, object?> kv in fromToml) {
                    if (!ctx.ContainsKey(kv.Key)) {
                        ctx[kv.Key] = kv.Value;
                    }
                }
            }
        }  catch (System.Exception ex) {
            Core.Diagnostics.Bug($"[Engine.cs] err reading config.toml: {ex.Message}");
        }
        cfgDict3["module_path"] = gameRoot4;
        cfgDict3["project_path"] = RootPath;

        string? resolvedDbPath = null;
        if (op.TryGetValue("db", out object? dbObj) && dbObj is not null) {
            object? resolvedDb = Core.Utils.Placeholders.Resolve(dbObj, ctx);
            if (resolvedDb is IList<object?> dbList && dbList.Count > 0) {
                resolvedDbPath = dbList[0]?.ToString();
            } else {
                resolvedDbPath = resolvedDb?.ToString();
            }
        }

        List<string> argsValidate = new List<string>();
        if (!string.IsNullOrWhiteSpace(resolvedDbPath)) {
            argsValidate.Add(resolvedDbPath);
        }
        if (op.TryGetValue("args", out object? aobjValidate) && aobjValidate is System.Collections.IList aListValidate) {
            object? resolvedObj = Core.Utils.Placeholders.Resolve(aobjValidate, ctx);
            System.Collections.IList resolved = resolvedObj as System.Collections.IList ?? aListValidate;
            for (int i = 0; i < resolved.Count; i++) {
                object? a = resolved[i];
                if (a is null) {
                    continue;
                }

                string value = a.ToString()!;
                if (!string.IsNullOrWhiteSpace(resolvedDbPath) && argsValidate.Count == 1 && i == 0 && string.Equals(argsValidate[0], value, System.StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                argsValidate.Add(value);
            }
        }

        if (argsValidate.Count < 2) {
            Core.Utils.EngineSdk.PrintLine("validate-files requires a database path and base directory.");
            return false;
        }

        Core.Utils.EngineSdk.PrintLine("\n>>> Built-in file validation");
        bool okValidate = FileHandlers.FileValidator.Run(argsValidate);
        return okValidate;
    }
}
