
using EngineNet.Core.Serialization.Toml;

namespace EngineNet.Core.Engine.operations.Built_inActions;

internal partial class BuiltInOperations {

    internal bool format_convert(
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers,
        string currentGame,
        Dictionary<string, Core.Data.GameModuleInfo> games,
        EngineContext context,
        System.Threading.CancellationToken cancellationToken = default
    ) {
        Core.Diagnostics.Log("[Engine.private.cs :: Operations()]] format-convert");
        // Determine tool - check both 'tool' field and '-m'/'--mode' in args
        string? tool = op.TryGetValue("tool", out object? ft) ? ft?.ToString()?.ToLowerInvariant() : null;

#if DEBUG
        if (op.TryGetValue("args", out object? argsDebugObj)) {
            Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] format-convert: args type = {argsDebugObj?.GetType().FullName ?? "null"}");
            if (argsDebugObj is System.Collections.IList argsDebugList) {
                Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] format-convert: args count = {argsDebugList.Count}");
                for (int i = 0; i < argsDebugList.Count; i++) {
                    Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] format-convert: args[{i}] = '{argsDebugList[i]}'");
                }
            }
        }
#endif

        // If tool not specified, try to extract from args
        if (string.IsNullOrWhiteSpace(tool) && op.TryGetValue("args", out object? argsObj)) {
            if (argsObj is System.Collections.IList argsList) {
                // Check for -m/--mode flag
                for (int i = 0; i < argsList.Count - 1; i++) {
                    string arg = argsList[i]?.ToString() ?? string.Empty;
                    if (arg == "-m" || arg == "--mode") {
                        tool = argsList[i + 1]?.ToString()?.ToLowerInvariant();
                        Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] format-convert: extracted tool from args: '{tool}'");
                        break;
                    }
                }

            }
        }
        Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] format-convert: final tool = '{tool}'");



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



        // execute
        if (string.Equals(tool, "ffmpeg", System.StringComparison.OrdinalIgnoreCase) || string.Equals(tool, "vgmstream", System.StringComparison.OrdinalIgnoreCase)) {
            // attempt built-in media conversion (ffmpeg/vgmstream) using the same CLI args
            Core.UI.EngineSdk.PrintLine("\n>>> Built-in media conversion");
            Core.Diagnostics.Log($"[format-convert.cs :: format_convert()]] format-convert: running media conversion with args: {string.Join(' ', args)}");
            bool okMedia = FileHandlers.MediaConverter.Run(context.ToolResolver, args, cancellationToken);
            return okMedia;
        } else if (string.Equals(tool, "ImageMagick", System.StringComparison.OrdinalIgnoreCase)) {
            // attempt image conversion (ImageMagick) using the CLI args
            Core.UI.EngineSdk.PrintLine("\n>>> Built-in image conversion");
            Core.Diagnostics.Log($"[format-convert.cs :: format_convert()]] format-convert: running image conversion with args: {string.Join(' ', args)}");
            bool okImage = FileHandlers.ImageMagickConverter.Run(context.ToolResolver, args, cancellationToken);
            return okImage;
        } else {
            Core.Diagnostics.Log($"[format-convert.cs :: format_convert()]] format-convert: unknown tool '{tool}'");
            Core.UI.EngineSdk.PrintLine($"ERROR: format-convert requires a valid tool. Found: '{tool ?? "(null)"}'");
            Core.UI.EngineSdk.PrintLine("Supported tools: ffmpeg, vgmstream, ImageMagick");
            Core.UI.EngineSdk.PrintLine("Specify tool with --tool parameter or -m/--mode in args.");
            return false;
        }
    }
}

