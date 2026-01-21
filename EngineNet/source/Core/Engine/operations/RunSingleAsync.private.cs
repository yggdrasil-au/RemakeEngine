using System.Collections.Generic;

namespace EngineNet.Core;

internal sealed partial class Engine {

    // used by run single operation to execute engine operations of type "engine"

    /// <summary>
    /// Executes an engine operation of type "engine".
    /// </summary>
    /// <param name="currentGame"></param>
    /// <param name="games"></param>
    /// <param name="op"></param>
    /// <param name="promptAnswers"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="KeyNotFoundException"></exception>
    private async System.Threading.Tasks.Task<bool> ExecuteEngineOperationAsync(string currentGame, Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games, IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers, System.Threading.CancellationToken cancellationToken = default) {
        if (!op.TryGetValue("script", out object? s) || s is null) {
            Core.Diagnostics.Log("[Engine.private.cs :: OperationExecution()] Missing 'script' value in engine operation");
            return false;
        } else {
            Core.Diagnostics.Log($"[Engine.private.cs :: OperationExecution()]] engine operation script: {s}");
        }

        // internal operations
        string? type = op.TryGetValue("script_type", out object? st) ? st?.ToString()?.ToLowerInvariant() : null;
        if (type == "internal") {
            string? sourceFile = op.TryGetValue("_source_file", out object? sf) ? sf?.ToString() : null;
            if (string.IsNullOrWhiteSpace(sourceFile)) {
                Core.Utils.EngineSdk.Error("Internal operation blocked: Missing source file context.");
                return false;
            }
            string allowedDir = System.IO.Path.Combine(RootPath, "EngineApps", "Registries", "ops");
            string fullSource = System.IO.Path.GetFullPath(sourceFile);
            string fullAllowed = System.IO.Path.GetFullPath(allowedDir);

            if (!fullSource.StartsWith(fullAllowed, System.StringComparison.OrdinalIgnoreCase)) {
                Core.Utils.EngineSdk.Error($"Internal operation blocked: Source '{sourceFile}' is not in allowed directory '{allowedDir}'.");
                return false;
            }
        }

        // Determine action
        string? action = s.ToString()?.ToLowerInvariant();

        Core.Diagnostics.Log($"[Engine.private.cs :: OperationExecution()]] Executing engine action: {action}");
        switch (action) {
            case "download_module_git": {
                string? url = null;
                if (promptAnswers.TryGetValue("url", out object? u)) {
                    url = u?.ToString();
                }
                if (string.IsNullOrWhiteSpace(url)) {
                    Core.Utils.EngineSdk.Error("No URL provided.");
                    Core.Diagnostics.Trace("[Engine.private.cs :: OperationExecution()]] download_module_git: no url provided");
                    return false;
                }
                return GitService.CloneModule(url);
            }
            case "download_module_registry": {
                string? input = null;
                if (promptAnswers.TryGetValue("url", out object? u)) {
                    input = u?.ToString();
                }
                if (string.IsNullOrWhiteSpace(input)) {
                    Core.Utils.EngineSdk.Error("No input provided.");
                    return false;
                }

                var knownModules = GameRegistry.GetRegisteredModules();
                string? url = input;

                if (knownModules.TryGetValue(input!, out object? modObj) && modObj is Dictionary<string, object?> modData) {
                    if (modData.TryGetValue("url", out object? uObj)) {
                        url = uObj?.ToString();
                    }
                }

                if (string.IsNullOrWhiteSpace(url)) {
                    Core.Utils.EngineSdk.Error($"Could not resolve URL for '{input}'.");
                    return false;
                }

                return GitService.CloneModule(url);
            }
            case "download_tools": {
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
                    Core.Diagnostics.Log("[Engine.private.cs :: OperationExecution()]] Created RemakeEngine dictionary in placeholders context");
                }
                // Ensure Config dictionary exists
                if (!reDict0.TryGetValue("Config", out object? cfg0) || cfg0 is not IDictionary<string, object?> cfgDict0) {
                    reDict0["Config"] = cfgDict0 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                    Core.Diagnostics.Log("[Engine.private.cs :: OperationExecution()]] Created RemakeEngine.Config dictionary in placeholders context");
                }
                // Merge module placeholders from config.toml
                try {
                    string cfgPath = System.IO.Path.Combine(gameRoot, "config.toml");
                    if (!string.IsNullOrWhiteSpace(gameRoot) && System.IO.File.Exists(cfgPath)) {
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

                Tools.ToolsDownloader dl = new Tools.ToolsDownloader(RootPath, central);
                await dl.ProcessAsync(resolvedManifest, force);
                return true;
            }
            case "format-extract":
            case "format_extract": {
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
                    Core.Utils.EngineSdk.PrintLine("\n>>> Built-in TXD extraction");
                    bool okTxd = FileHandlers.TxdExtractor.Main.Run(args);
                    return okTxd;
                } else if (string.IsNullOrWhiteSpace(format)) {
                    // Auto-detect format - default to TXD for now
                    Core.Utils.EngineSdk.PrintLine("\n>>> Built-in TXD extraction (auto-detected)");
                    bool okTxd = FileHandlers.TxdExtractor.Main.Run(args);
                    return okTxd;
                } else {
                    Core.Utils.EngineSdk.PrintLine($"ERROR: format-extract does not support format '{format}'");
                    Core.Utils.EngineSdk.PrintLine("Supported formats: txd (default)");
                    return false;
                }
            }
            case "format-convert":
            case "format_convert": {
                Core.Diagnostics.Log("[Engine.private.cs :: OperationExecution()]] format-convert");
                // Determine tool - check both 'tool' field and '-m'/'--mode' in args
                string? tool = op.TryGetValue("tool", out object? ft) ? ft?.ToString()?.ToLowerInvariant() : null;

#if DEBUG
                if (op.TryGetValue("args", out object? argsDebugObj)) {
                    Core.Diagnostics.Log($"[Engine.private.cs :: OperationExecution()]] format-convert: args type = {argsDebugObj?.GetType().FullName ?? "null"}");
                    if (argsDebugObj is System.Collections.IList argsDebugList) {
                        Core.Diagnostics.Log($"[Engine.private.cs :: OperationExecution()]] format-convert: args count = {argsDebugList.Count}");
                        for (int i = 0; i < argsDebugList.Count; i++) {
                            Core.Diagnostics.Log($"[Engine.private.cs :: OperationExecution()]] format-convert: args[{i}] = '{argsDebugList[i]}'");
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
                                Core.Diagnostics.Log($"[Engine.private.cs :: OperationExecution()]] format-convert: extracted tool from args: '{tool}'");
                                break;
                            }
                        }

                        // If still no tool, try to infer from arguments pattern
                        if (string.IsNullOrWhiteSpace(tool)) {
                            bool hasSource = false;
                            bool hasInputExt = false;
                            bool hasOutputExt = false;
                            bool hasType = false;

                            foreach (object? a in argsList) {
                                string arg = a?.ToString() ?? string.Empty;
                                if (arg == "--source" || arg == "-s") hasSource = true;
                                if (arg == "--input-ext" || arg == "-i") hasInputExt = true;
                                if (arg == "--output-ext" || arg == "-o") hasOutputExt = true;
                                if (arg == "--type") hasType = true;
                            }

                            // If has --source, --input-ext, --output-ext but no --type, likely ImageMagick
                            if (hasSource && hasInputExt && hasOutputExt && !hasType) {
                                tool = "imagemagick";
                                Core.Diagnostics.Log($"[Engine.private.cs :: OperationExecution()]] format-convert: inferred tool from args pattern: '{tool}'");
                            }
                        }
                    }
                }
                Core.Diagnostics.Log($"[Engine.private.cs :: OperationExecution()]] format-convert: final tool = '{tool}'");

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
                        Dictionary<string, object?> fromToml = Core.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                        foreach (KeyValuePair<string, object?> kv in fromToml) {
                            if (!ctx.ContainsKey(kv.Key)) {
                                ctx[kv.Key] = kv.Value;
                            }
                        }
                    }
                } catch (System.Exception ex) {
                    Core.Diagnostics.Bug($"[Engine.private.cs :: OperationExecution()]] format-convert: failed to read config.toml: {ex.Message}");
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
                    Core.Utils.EngineSdk.PrintLine("\n>>> Built-in media conversion");
                    Core.Diagnostics.Log($"[Engine.private.cs :: OperationExecution()]] format-convert: running media conversion with args: {string.Join(' ', args)}");
                    bool okMedia = FileHandlers.MediaConverter.Run(ToolResolver, args);
                    return okMedia;
                } else if (string.Equals(tool, "ImageMagick", System.StringComparison.OrdinalIgnoreCase)) {
                    // attempt image conversion (ImageMagick) using the CLI args
                    Core.Utils.EngineSdk.PrintLine("\n>>> Built-in image conversion");
                    Core.Diagnostics.Log($"[Engine.private.cs :: OperationExecution()]] format-convert: running image conversion with args: {string.Join(' ', args)}");
                    bool okImage = FileHandlers.ImageMagickConverter.Run(ToolResolver, args);
                    return okImage;
                } else {
                    Core.Diagnostics.Log($"[Engine.private.cs :: OperationExecution()]] format-convert: unknown tool '{tool}'");
                    Core.Utils.EngineSdk.PrintLine($"ERROR: format-convert requires a valid tool. Found: '{tool ?? "(null)"}'");
                    Core.Utils.EngineSdk.PrintLine("Supported tools: ffmpeg, vgmstream, ImageMagick");
                    Core.Utils.EngineSdk.PrintLine("Specify tool with --tool parameter or -m/--mode in args.");
                    return false;
                }
            }
            case "validate-files":
            case "validate_files": {
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
            case "rename-folders":
            case "rename_folders": {
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

                Core.Utils.EngineSdk.PrintLine("\n>>> Built-in folder rename");
                Core.Utils.EngineSdk.PrintLine($"with args: {string.Join(' ', args)}");
                bool okRename = FileHandlers.FolderRenamer.Run(args);
                return okRename;
            }

            default: {
                Core.Diagnostics.Log($"[Engine.private.cs :: OperationExecution()]] Unknown engine action: {action}");
                return false;
            }
        }
    }


    /// <summary>
    /// Try to get the list of operations defined in the "onsuccess" or "on_success" field of the given operation.
    /// </summary>
    /// <param name="op"></param>
    /// <param name="ops"></param>
    /// <returns></returns>
    private static bool TryGetOnSuccessOperations(
        IDictionary<string, object?> op,
        out List<Dictionary<string, object?>>? ops
    ) {
        ops = null;
        if (op is null) return false;

        static List<Dictionary<string, object?>>? Coerce(object? value) {
            if (value is null) return null;
            List<Dictionary<string, object?>> list = new List<Dictionary<string, object?>>();
            if (value is IList<object?> arr) {
                foreach (object? item in arr) {
                    if (item is IDictionary<string, object?> map) {
                        list.Add(new Dictionary<string, object?>(map, System.StringComparer.OrdinalIgnoreCase));
                    }
                }
            } else if (value is IDictionary<string, object?> single) {
                list.Add(new Dictionary<string, object?>(single, System.StringComparer.OrdinalIgnoreCase));
            }
            return list.Count > 0 ? list : null;
        }

        if (op.TryGetValue("onsuccess", out object? v1)) {
            ops = Coerce(v1);
            if (ops is not null) return true;
        }
        if (op.TryGetValue("on_success", out object? v2)) {
            ops = Coerce(v2);
            if (ops is not null) return true;
        }
        return false;
    }
}