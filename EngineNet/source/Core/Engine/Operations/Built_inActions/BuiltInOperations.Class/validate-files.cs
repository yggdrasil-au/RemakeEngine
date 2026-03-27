
using EngineNet.Core.Serialization.Toml;

namespace EngineNet.Core.Engine.operations.Built_inActions;

internal partial class BuiltInOperations {

    internal bool validate_files(
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers,
        string currentGame,
        Dictionary<string, Core.Data.GameModuleInfo> games,
        EngineContext context,
        System.Threading.CancellationToken cancellationToken = default
    ) {



        // Resolve args
        Dictionary<string, object?> ctx = new Dictionary<string, object?>(context.EngineConfig.Data, System.StringComparer.OrdinalIgnoreCase);
        if (!games.TryGetValue(currentGame, out Core.Data.GameModuleInfo? gobj)) {
            throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
        }
        // Built-in placeholders
        string gameRoot = gobj.GameRoot;
        ctx["Game_Root"] = gameRoot;
        ctx["Project_Root"] = EngineNet.Core.Main.RootPath;
        ctx["Registry_Root"] = System.IO.Path.Combine(EngineNet.Core.Main.RootPath, "EngineApps");
        ctx["Game"] = new Dictionary<string, object?> {
            ["RootPath"] = gameRoot,
            ["Name"] = currentGame,
        };
        // Ensure RemakeEngine dictionary exists
        if (!ctx.TryGetValue("RemakeEngine", out object? re) || re is not IDictionary<string, object?> reDict) {
            ctx["RemakeEngine"] = reDict = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
            Core.Diagnostics.Log("[] Created RemakeEngine dictionary in placeholders context");
        }
        // Ensure Config dictionary exists
        if (!reDict.TryGetValue("Config", out object? cfg) || cfg is not IDictionary<string, object?> cfgDict) {
            reDict["Config"] = cfgDict = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
            Core.Diagnostics.Log("[] Created RemakeEngine.Config dictionary in placeholders context");
        }
        // Merge module placeholders from config.toml
        try {
            string cfgPath = System.IO.Path.Combine(gameRoot, "config.toml");
            if (!string.IsNullOrWhiteSpace(gameRoot) && System.IO.File.Exists(cfgPath)) {
                Dictionary<string, object?> fromToml = TomlHelpers.ReadPlaceholdersFile(cfgPath);
                foreach (KeyValuePair<string, object?> kv in fromToml) {
                    ctx[kv.Key] = kv.Value;
                }
            }
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"[] failed to read config.toml: {ex.Message}");
        }
        // assign default placeholders
        cfgDict["module_path"] = gameRoot;
        cfgDict["project_path"] = EngineNet.Core.Main.RootPath;



        string? resolvedDbPath = null;
        if (op.TryGetValue("db", out object? dbObj) && dbObj is not null) {
            object? resolvedDb = Core.Utils.Placeholders.Resolve(dbObj, ctx);
            if (resolvedDb is IList<object?> dbList && dbList.Count > 0) {
                resolvedDbPath = dbList[0]?.ToString();
            } else {
                resolvedDbPath = resolvedDb?.ToString();
            }
        }

        // create args list
        List<string> args = new List<string>();
        // if a db path was resolved and is not already in args, add it as the first arg
        if (!string.IsNullOrWhiteSpace(resolvedDbPath)) {
            args.Add(resolvedDbPath);
        }
        // Resolve args
        if (op.TryGetValue("args", out object? aobj) && aobj is System.Collections.IList aList) {
            object? resolvedObj = Core.Utils.Placeholders.Resolve(aobj, ctx);
            System.Collections.IList resolved = resolvedObj as System.Collections.IList ?? aList;
            for (int i = 0; i < resolved.Count; i++) {
                object? a = resolved[i];
                if (a is null) {
                    continue;
                }

                string value = a.ToString()!;
                if (!string.IsNullOrWhiteSpace(resolvedDbPath) && args.Count == 1 && i == 0 && string.Equals(args[0], value, System.StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                args.Add(value);
            }
        }
        // if less than 2 args, print message and return false
        if (args.Count < 2) {
            Core.UI.EngineSdk.PrintLine("validate-files requires a database path and base directory.");
            return false;
        }



        // execute
        Core.UI.EngineSdk.PrintLine("\n>>> Built-in file validation");
        Core.UI.EngineSdk.PrintLine($"with args: {string.Join(' ', args)}");
        bool ok = helpers.FileValidator.Run(args, cancellationToken);
        return ok;
    }
}
