using System.Collections.Generic;

namespace EngineNet.Core.Engine.operations.Built_inActions;
public partial class InternalOperations {
    internal bool format_convert(IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers, string currentGame, Dictionary<string, Core.Utils.GameModuleInfo> games, string RootPath,  EngineConfig EngineConfig, ExternalTools.IToolResolver ToolResolver) {
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

        // Resolve args (used for both TXD and media conversions)
        Dictionary<string, object?> ctx = new Dictionary<string, object?>(EngineConfig.Data, System.StringComparer.OrdinalIgnoreCase);
        if (!games.TryGetValue(currentGame, out Core.Utils.GameModuleInfo? gobj)) {
            throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
        }
        // Built-in placeholders
        string gameRoot3 = gobj.GameRoot;
        ctx["Game_Root"] = gameRoot3;
        ctx["Project_Root"] = RootPath;
        ctx["Registry_Root"] = System.IO.Path.Combine(RootPath, "EngineApps");
        ctx["Game"] = new Dictionary<string, object?> {
            ["RootPath"] = gameRoot3,
            ["Name"] = currentGame,
        };
        if (!ctx.TryGetValue("RemakeEngine", out object? re2) || re2 is not IDictionary<string, object?> reDict2) {
            ctx["RemakeEngine"] = reDict2 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        }

        if (!reDict2.TryGetValue("Config", out object? cfg2) || cfg2 is not IDictionary<string, object?> cfgDict2) {
            reDict2["Config"] = cfgDict2 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        }
        // Merge module placeholders from config.toml
        try {
            string cfgPath = System.IO.Path.Combine(gameRoot3, "config.toml");
            if (!string.IsNullOrWhiteSpace(gameRoot3) && System.IO.File.Exists(cfgPath)) {
                Dictionary<string, object?> fromToml = Core.ExternalTools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                foreach (KeyValuePair<string, object?> kv in fromToml) {
                    if (!ctx.ContainsKey(kv.Key)) {
                        ctx[kv.Key] = kv.Value;
                    }
                }
            }
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"[Engine.private.cs :: Operations()]] format-convert: failed to read config.toml: {ex.Message}");
        // ignore
        }
        cfgDict2["module_path"] = gameRoot3;
        cfgDict2["project_path"] = RootPath;

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

        if (string.Equals(tool, "ffmpeg", System.StringComparison.OrdinalIgnoreCase) || string.Equals(tool, "vgmstream", System.StringComparison.OrdinalIgnoreCase)) {
            // attempt built-in media conversion (ffmpeg/vgmstream) using the same CLI args
            Core.UI.EngineSdk.PrintLine("\n>>> Built-in media conversion");
            Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] format-convert: running media conversion with args: {string.Join(' ', args)}");
            bool okMedia = FileHandlers.MediaConverter.Run(ToolResolver, args);
            return okMedia;
        } else if (string.Equals(tool, "ImageMagick", System.StringComparison.OrdinalIgnoreCase)) {
            // attempt image conversion (ImageMagick) using the CLI args
            Core.UI.EngineSdk.PrintLine("\n>>> Built-in image conversion");
            Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] format-convert: running image conversion with args: {string.Join(' ', args)}");
            bool okImage = FileHandlers.ImageMagickConverter.Run(ToolResolver, args);
            return okImage;
        } else {
            Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] format-convert: unknown tool '{tool}'");
            Core.UI.EngineSdk.PrintLine($"ERROR: format-convert requires a valid tool. Found: '{tool ?? "(null)"}'");
            Core.UI.EngineSdk.PrintLine("Supported tools: ffmpeg, vgmstream, ImageMagick");
            Core.UI.EngineSdk.PrintLine("Specify tool with --tool parameter or -m/--mode in args.");
            return false;
        }
    }
}

