namespace EngineNet.Core;

internal sealed partial class OperationsEngine {
    /// <summary>
    /// Executes a group of operations sequentially, aggregating success across steps.
    /// </summary>
    /// <param name="gameName">Game/module id.</param>
    /// <param name="games">Games map.</param>
    /// <param name="groupName">Group label (for diagnostics).</param>
    /// <param name="operations">List of operation maps.</param>
    /// <param name="promptAnswers">Answers to prompt definitions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if all operations reported success.</returns>
    public async System.Threading.Tasks.Task<bool> RunOperationGroupAsync(
        string gameName,
		IDictionary<string, object?> games,
        string groupName,
		IList<Dictionary<string, object?>> operations,
		IDictionary<string, object?> promptAnswers,
		System.Threading.CancellationToken cancellationToken = default) {
        bool success = true;
		foreach (Dictionary<string, object?> op in operations)
		{
			if (!await RunSingleOperationAsync(gameName, games, op, promptAnswers, cancellationToken)) {
                success = false;
            }
        }
		return success;
	}

    /// <summary>
    /// Executes a single operation, delegating to embedded engines (Lua/JS), built-in handlers (engine),
    /// or external processes depending on <c>script_type</c>.
    /// </summary>
    /// <returns>True on successful completion.</returns>
    public async System.Threading.Tasks.Task<bool> RunSingleOperationAsync(
        string currentGame,
        IDictionary<string, object?> games,
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers,
        System.Threading.CancellationToken cancellationToken = default) {
        string scriptType = (op.TryGetValue("script_type", out object? st) ? st?.ToString() : null)?.ToLowerInvariant() ?? "python";
        List<string> parts = _builder.Build(currentGame, games, _engineConfig.Data, op, promptAnswers);
        if (parts.Count < 2) {
            return false;
        }

        string scriptPath = parts[1];
        string[] args = parts.Skip(2).ToArray();

        bool result = false;
        try {
            switch (scriptType) {
                case "lua": {
                    try {
                        Core.ScriptEngines.LuaScriptAction action = new Core.ScriptEngines.LuaScriptAction(scriptPath, args);
                        await action.ExecuteAsync(_tools, cancellationToken);
                        result = true;
                    } catch (System.Exception ex) {
                        Program.Direct.Console.WriteLine($"lua engine ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                case "js": {
                    try {
                        EngineNet.Core.ScriptEngines.JsScriptAction action = new EngineNet.Core.ScriptEngines.JsScriptAction(scriptPath, args);
                        await action.ExecuteAsync(_tools, cancellationToken);
                        result = true;
                    } catch (System.Exception ex) {
                        Program.Direct.Console.WriteLine($"js engine ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                case "bms": {
                    try {
                        if (!games.TryGetValue(currentGame, out object? gobjBms) || gobjBms is not IDictionary<string, object?> gdictBms) {
                            throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                        }
                        string gameRootBms = gdictBms.TryGetValue("game_root", out object? grBms) ? grBms?.ToString() ?? string.Empty : string.Empty;

                        // Build placeholder context
                        Dictionary<string, object?> ctx = new Dictionary<string, object?>(_engineConfig.Data, System.StringComparer.OrdinalIgnoreCase) {
                            ["Game_Root"] = gameRootBms,
                            ["Project_Root"] = _rootPath,
                            ["Registry_Root"] = System.IO.Path.Combine(_rootPath, "RemakeRegistry"),
                            ["Game"] = new Dictionary<string, object?> { ["RootPath"] = gameRootBms, ["Name"] = currentGame },
                        };
                        if (!ctx.TryGetValue("RemakeEngine", out object? reB) || reB is not IDictionary<string, object?> reBdict) {
                            ctx["RemakeEngine"] = reBdict = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                        }
                        if (!reBdict.TryGetValue("Config", out object? cfgB) || cfgB is not IDictionary<string, object?> cfgBdict) {
                            reBdict["Config"] = cfgBdict = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                        }
                        ((IDictionary<string, object?>)ctx["RemakeEngine"]!)["Config"] = cfgBdict;
                        cfgBdict["module_path"] = gameRootBms;
                        cfgBdict["project_path"] = _rootPath;
                        try {
                            string cfgPath = System.IO.Path.Combine(gameRootBms, "config.toml");
                            if (!string.IsNullOrWhiteSpace(gameRootBms) && System.IO.File.Exists(cfgPath)) {
                                Dictionary<string, object?> fromToml = EngineNet.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                                foreach (KeyValuePair<string, object?> kv in fromToml) {
                                    if (!ctx.ContainsKey(kv.Key)) ctx[kv.Key] = kv.Value;
                                }
                            }
                        }  catch {
#if DEBUG
            Program.Direct.Console.WriteLine($"[Engine.OperationExecution] bms: error reading config.toml");
#endif
        }

                        string inputDir = op.TryGetValue("input", out object? in0) ? in0?.ToString() ?? string.Empty : string.Empty;
                        string outputDir = op.TryGetValue("output", out object? out0) ? out0?.ToString() ?? string.Empty : string.Empty;
                        string? extension = op.TryGetValue("extension", out object? ext0) ? ext0?.ToString() : null;
                        string resolvedInput = Sys.Placeholders.Resolve(inputDir, ctx)?.ToString() ?? inputDir;
                        string resolvedOutput = Sys.Placeholders.Resolve(outputDir, ctx)?.ToString() ?? outputDir;
                        string? resolvedExt = extension is null ? null : Sys.Placeholders.Resolve(extension, ctx)?.ToString() ?? extension;

                        Core.ScriptEngines.QuickBmsScriptAction action = new Core.ScriptEngines.QuickBmsScriptAction(
                            scriptPath,
                            gameRootBms,
                            _rootPath,
                            resolvedInput,
                            resolvedOutput,
                            resolvedExt
                        );
                        await action.ExecuteAsync(_tools, cancellationToken);
                        result = true;
                    } catch (System.Exception ex) {
                        Program.Direct.Console.WriteLine($"bms engine ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                case "engine": {
                    try {
                        string? action = op.TryGetValue("script", out object? s) ? s?.ToString() : null;
                        string? title = op.TryGetValue("Name", out object? n) ? n?.ToString() ?? action : action;
                        Program.Direct.Console.ForegroundColor = System.ConsoleColor.DarkCyan;
#if DEBUG
                        Program.Direct.Console.WriteLine($"Executing engine operation {title} ({action})");
#endif
                        Program.Direct.Console.WriteLine($"\n>>> Engine operation: {title}");
                        Program.Direct.Console.ResetColor();
                        result = await ExecuteEngineOperationAsync(currentGame, games, op, promptAnswers, cancellationToken);
                    } catch (System.Exception ex) {
                        Program.Direct.Console.WriteLine($"engine ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                case "python":
                default: {
                    // not supported
                    //Sys.ProcessRunner runner = new Sys.ProcessRunner();
                    //result = runner.Execute(parts, System.IO.Path.GetFileName(scriptPath), cancellationToken: cancellationToken);
                    break;
                }
            }
        } finally {
            // Ensure we pick up any config changes (e.g., init.lua writes project.json)
            ReloadProjectConfig();
        }
        // If the main operation succeeded, run any nested [[operation.onsuccess]] steps
        if (result && TryGetOnSuccessOperations(op, out List<Dictionary<string, object?>>? followUps) && followUps is not null) {
            foreach (Dictionary<string, object?> childOp in followUps) {
                if (cancellationToken.IsCancellationRequested) break;
                bool ok = await RunSingleOperationAsync(currentGame, games, childOp, promptAnswers, cancellationToken);
                if (!ok) {
                    result = false; // propagate failure from any onsuccess step
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Executes built-in engine actions such as tool downloads, format extraction/conversion,
    /// file validation, and folder rename helpers.
    /// </summary>
    public async System.Threading.Tasks.Task<bool> ExecuteEngineOperationAsync(
        string currentGame,
        IDictionary<string, object?> games,
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers,
        System.Threading.CancellationToken cancellationToken = default) {
        if (!op.TryGetValue("script", out object? s) || s is null) {
            return false;
        }

        string? action = s.ToString()?.ToLowerInvariant();
        switch (action) {
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

                Dictionary<string, object?> ctx = new Dictionary<string, object?>(_engineConfig.Data, System.StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out object? gobj) || gobj is not IDictionary<string, object?> gdict) {
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                // Built-in placeholders
                string gameRoot = gdict.TryGetValue("game_root", out object? gr0) ? gr0?.ToString() ?? string.Empty : string.Empty;
                ctx["Game_Root"] = gameRoot;
                ctx["Project_Root"] = _rootPath;
                ctx["Registry_Root"] = System.IO.Path.Combine(_rootPath, "RemakeRegistry");
                ctx["Game"] = new Dictionary<string, object?> {
                    ["RootPath"] = gameRoot,
                    ["Name"] = currentGame,
                };
                if (!ctx.TryGetValue("RemakeEngine", out object? re0) || re0 is not IDictionary<string, object?> reDict0) {
                    ctx["RemakeEngine"] = reDict0 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                }

                if (!reDict0.TryGetValue("Config", out object? cfg0) || cfg0 is not IDictionary<string, object?> cfgDict0) {
                    reDict0["Config"] = cfgDict0 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                }
                // Merge module placeholders from config.toml
                try {
                    string cfgPath = System.IO.Path.Combine(gameRoot, "config.toml");
                    if (!string.IsNullOrWhiteSpace(gameRoot) && System.IO.File.Exists(cfgPath)) {
                        Dictionary<string, object?> fromToml = EngineNet.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                        foreach (KeyValuePair<string, object?> kv in fromToml) {
                            if (!ctx.ContainsKey(kv.Key)) {
                                ctx[kv.Key] = kv.Value;
                            }
                        }
                    }
                }  catch {
#if DEBUG
            Program.Direct.Console.WriteLine($"Error .....'");
#endif
        }
                cfgDict0["module_path"] = gameRoot;
                cfgDict0["project_path"] = _rootPath;
                string resolvedManifest = Sys.Placeholders.Resolve(manifest!, ctx)?.ToString() ?? manifest!;
                string central = System.IO.Path.Combine(_rootPath, "RemakeRegistry/Tools.json");
                bool force = false;
                if (promptAnswers.TryGetValue("force download", out object? fd) && fd is bool b1) {
                    force = b1;
                }

                if (promptAnswers.TryGetValue("force_download", out object? fd2) && fd2 is bool b2) {
                    force = b2;
                }

                Tools.ToolsDownloader dl = new Tools.ToolsDownloader(_rootPath, central);
                await dl.ProcessAsync(resolvedManifest, force);
                return true;
            }
            case "format-extract":
            case "format_extract": {
                // Determine input file format
                string? format = op.TryGetValue("format", out object? ft) ? ft?.ToString()?.ToLowerInvariant() : null;

                // Resolve args (used for both TXD and media conversions)
                Dictionary<string, object?> ctx = new Dictionary<string, object?>(_engineConfig.Data, System.StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out object? gobj) || gobj is not IDictionary<string, object?> gdict2) {
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                // Built-in placeholders
                string gameRoot2 = gdict2.TryGetValue("game_root", out object? gr2a) ? gr2a?.ToString() ?? string.Empty : string.Empty;
                ctx["Game_Root"] = gameRoot2;
                ctx["Project_Root"] = _rootPath;
                ctx["Registry_Root"] = System.IO.Path.Combine(_rootPath, "RemakeRegistry");
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
                        Dictionary<string, object?> fromToml = EngineNet.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                        foreach (KeyValuePair<string, object?> kv in fromToml) {
                            if (!ctx.ContainsKey(kv.Key)) {
                                ctx[kv.Key] = kv.Value;
                            }
                        }
                    }
                }  catch {
#if DEBUG
            Program.Direct.Console.WriteLine($"Error .....'");
#endif
        }
                cfgDict1["module_path"] = gameRoot2;
                cfgDict1["project_path"] = _rootPath;

                List<string> args = new List<string>();
                if (op.TryGetValue("args", out object? aobj) && aobj is IList<object?> aList) {
                    IList<object?> resolved = (IList<object?>)(Sys.Placeholders.Resolve(aList, ctx) ?? new List<object?>());
                    foreach (object? a in resolved) {
                        if (a is not null) {
                            args.Add(a.ToString()!);
                        }
                    }
                }

                // If format is TXD, use built-in extractor

                if (string.Equals(format, "txd", System.StringComparison.OrdinalIgnoreCase)) {
                    Program.Direct.Console.ForegroundColor = System.ConsoleColor.DarkCyan;
                    Program.Direct.Console.WriteLine("\n>>> Built-in TXD extraction");
                    Program.Direct.Console.ResetColor();
                    bool okTxd = FileHandlers.TxdExtractor.Main.Run(args);
                    return okTxd;
                } else {
                    return false;
                }
            }
            case "format-convert":
            case "format_convert": {
#if DEBUG
                    Program.Direct.Console.WriteLine("[Engine.OperationExecution] format-convert");
#endif
                // Determine tool
                string? tool = op.TryGetValue("tool", out object? ft) ? ft?.ToString()?.ToLowerInvariant() : null;

                // Resolve args (used for both TXD and media conversions)
                Dictionary<string, object?> ctx = new Dictionary<string, object?>(_engineConfig.Data, System.StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out object? gobj) || gobj is not IDictionary<string, object?> gdict2) {
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                // Built-in placeholders
                string gameRoot3 = gdict2.TryGetValue("game_root", out object? gr3a) ? gr3a?.ToString() ?? string.Empty : string.Empty;
                ctx["Game_Root"] = gameRoot3;
                ctx["Project_Root"] = _rootPath;
                ctx["Registry_Root"] = System.IO.Path.Combine(_rootPath, "RemakeRegistry");
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
                        Dictionary<string, object?> fromToml = EngineNet.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                        foreach (KeyValuePair<string, object?> kv in fromToml) {
                            if (!ctx.ContainsKey(kv.Key)) {
                                ctx[kv.Key] = kv.Value;
                            }
                        }
                    }
                } catch (System.Exception ex) {
#if DEBUG
                    Program.Direct.Console.WriteLine($"[Engine.OperationExecution] format-convert: failed to read config.toml: {ex.Message}");
#endif
                // ignore
                }
                cfgDict2["module_path"] = gameRoot3;
                cfgDict2["project_path"] = _rootPath;

                List<string> args = new List<string>();
                if (op.TryGetValue("args", out object? aobj) && aobj is IList<object?> aList) {
                    IList<object?> resolved = (IList<object?>)(Sys.Placeholders.Resolve(aList, ctx) ?? new List<object?>());
                    foreach (object? a in resolved) {
                        if (a is not null) {
                            args.Add(a.ToString()!);
                        }
                    }
                }

                if (string.Equals(tool, "ffmpeg", System.StringComparison.OrdinalIgnoreCase) || string.Equals(tool, "vgmstream", System.StringComparison.OrdinalIgnoreCase)) {
                    // attempt built-in media conversion (ffmpeg/vgmstream) using the same CLI args
                    Program.Direct.Console.ForegroundColor = System.ConsoleColor.DarkCyan;
                    Program.Direct.Console.WriteLine("\n>>> Built-in media conversion");
                    Program.Direct.Console.ResetColor();
#if DEBUG
                    Program.Direct.Console.WriteLine($"[Engine.OperationExecution] format-convert: running media conversion with args: {string.Join(' ', args)}");
#endif
                    bool okMedia = FileHandlers.MediaConverter.Run(_tools, args);
                    return okMedia;
                } else {
                    return false;
                }
            }
            case "validate-files":
            case "validate_files": {
                Dictionary<string, object?> ctx = new Dictionary<string, object?>(_engineConfig.Data, System.StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out object? gobjValidate) || gobjValidate is not IDictionary<string, object?> gdictValidate) {
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                // Built-in placeholders
                string gameRoot4 = gdictValidate.TryGetValue("game_root", out object? grValidate0) ? grValidate0?.ToString() ?? string.Empty : string.Empty;
                ctx["Game_Root"] = gameRoot4;
                ctx["Project_Root"] = _rootPath;
                ctx["Registry_Root"] = System.IO.Path.Combine(_rootPath, "RemakeRegistry");
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
                        Dictionary<string, object?> fromToml = EngineNet.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                        foreach (KeyValuePair<string, object?> kv in fromToml) {
                            if (!ctx.ContainsKey(kv.Key)) {
                                ctx[kv.Key] = kv.Value;
                            }
                        }
                    }
                }  catch {
#if DEBUG
            Program.Direct.Console.WriteLine($"Error .....'");
#endif
        }
                cfgDict3["module_path"] = gameRoot4;
                cfgDict3["project_path"] = _rootPath;

                string? resolvedDbPath = null;
                if (op.TryGetValue("db", out object? dbObj) && dbObj is not null) {
                    object? resolvedDb = Sys.Placeholders.Resolve(dbObj, ctx);
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
                if (op.TryGetValue("args", out object? aobjValidate) && aobjValidate is IList<object?> aListValidate) {
                    IList<object?> resolved = (IList<object?>)(Sys.Placeholders.Resolve(aListValidate, ctx) ?? new List<object?>());
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
                    Program.Direct.Console.WriteLine("validate-files requires a database path and base directory.");
                    return false;
                }

                Program.Direct.Console.ForegroundColor = System.ConsoleColor.DarkCyan;
                Program.Direct.Console.WriteLine("\n>>> Built-in file validation");
                Program.Direct.Console.ResetColor();
                bool okValidate = FileHandlers.FileValidator.Run(argsValidate);
                return okValidate;
            }
            case "rename-folders":
            case "rename_folders": {
                Dictionary<string, object?> ctx = new Dictionary<string, object?>(_engineConfig.Data, System.StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out object? gobj3) || gobj3 is not IDictionary<string, object?> gdict3) {
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                // Built-in placeholders
                string gameRoot5 = gdict3.TryGetValue("game_root", out object? gr3) ? gr3?.ToString() ?? string.Empty : string.Empty;
                ctx["Game_Root"] = gameRoot5;
                ctx["Project_Root"] = _rootPath;
                ctx["Registry_Root"] = System.IO.Path.Combine(_rootPath, "RemakeRegistry");
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
                        Dictionary<string, object?> fromToml = EngineNet.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                        foreach (KeyValuePair<string, object?> kv in fromToml) {
                            if (!ctx.ContainsKey(kv.Key)) {
                                ctx[kv.Key] = kv.Value;
                            }
                        }
                    }
                }  catch {
#if DEBUG
            Program.Direct.Console.WriteLine($"Error .....'");
#endif
        }
                cfgDict4["module_path"] = gameRoot5;
                cfgDict4["project_path"] = _rootPath;

                List<string> args = new List<string>();
                if (op.TryGetValue("args", out object? aobjRename) && aobjRename is IList<object?> aListRename) {
                    IList<object?> resolved = (IList<object?>)(Sys.Placeholders.Resolve(aListRename, ctx) ?? new List<object?>());
                    foreach (object? a in resolved) {
                        if (a is not null) {
                            args.Add(a.ToString()!);
                        }
                    }
                }

                Program.Direct.Console.ForegroundColor = System.ConsoleColor.DarkCyan;
                Program.Direct.Console.WriteLine("\n>>> Built-in folder rename");
                Program.Direct.Console.WriteLine($"with args: {string.Join(' ', args)}");
                Program.Direct.Console.ResetColor();
                bool okRename = FileHandlers.FolderRenamer.Run(args);
                return okRename;
            }
            case "flatten":
            case "flatten-folder-structure": {
                Dictionary<string, object?> ctx = new Dictionary<string, object?>(_engineConfig.Data, System.StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out object? gobjFlatten) || gobjFlatten is not IDictionary<string, object?> gdictFlatten) {
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                // Built-in placeholders
                string gameRoot6 = gdictFlatten.TryGetValue("game_root", out object? grFlatten0) ? grFlatten0?.ToString() ?? string.Empty : string.Empty;
                ctx["Game_Root"] = gameRoot6;
                ctx["Project_Root"] = _rootPath;
                ctx["Registry_Root"] = System.IO.Path.Combine(_rootPath, "RemakeRegistry");
                ctx["Game"] = new Dictionary<string, object?> {
                    ["RootPath"] = gameRoot6,
                    ["Name"] = currentGame,
                };
                if (!ctx.TryGetValue("RemakeEngine", out object? re5) || re5 is not IDictionary<string, object?> reDict5) {
                    ctx["RemakeEngine"] = reDict5 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                }

                if (!reDict5.TryGetValue("Config", out object? cfg5) || cfg5 is not IDictionary<string, object?> cfgDict5) {
                    reDict5["Config"] = cfgDict5 = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                }
                // Merge module placeholders from config.toml
                try {
                    string cfgPath = System.IO.Path.Combine(gameRoot6, "config.toml");
                    if (!string.IsNullOrWhiteSpace(gameRoot6) && System.IO.File.Exists(cfgPath)) {
                        Dictionary<string, object?> fromToml = EngineNet.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                        foreach (KeyValuePair<string, object?> kv in fromToml) {
                            if (!ctx.ContainsKey(kv.Key)) {
                                ctx[kv.Key] = kv.Value;
                            }
                        }
                    }
                }  catch {
#if DEBUG
            Program.Direct.Console.WriteLine($"Error .....'");
#endif
        }
                cfgDict5["module_path"] = gameRoot6;
                cfgDict5["project_path"] = _rootPath;

                List<string> argsFlatten = new List<string>();
                if (op.TryGetValue("args", out object? aobjFlatten) && aobjFlatten is IList<object?> aListFlatten) {
                    IList<object?> resolved = (IList<object?>)(Sys.Placeholders.Resolve(aListFlatten, ctx) ?? new List<object?>());
                    foreach (object? a in resolved) {
                        if (a is not null) {
                            argsFlatten.Add(a.ToString()!);
                        }
                    }
                }

                Program.Direct.Console.ForegroundColor = System.ConsoleColor.DarkCyan;
                Program.Direct.Console.WriteLine("\n>>> Built-in directory flatten");
                Program.Direct.Console.ResetColor();
                bool okFlatten = FileHandlers.DirectoryFlattener.Run(argsFlatten);
                return okFlatten;
            }

            default:
                return false;
        }
    }

    private void ReloadProjectConfig() {
        try {
            string projectJson = System.IO.Path.Combine(_rootPath, "project.json");
            if (!System.IO.File.Exists(projectJson)) {
                return;
            }

            using System.IO.FileStream fs = System.IO.File.OpenRead(projectJson);
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(fs);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) {
                return;
            }

            Dictionary<string, object?> map = ToMap(doc.RootElement);
            IDictionary<string, object?>? data = _engineConfig.Data;
            if (data is null) {
                return;
            }

            data.Clear();
            foreach (KeyValuePair<string, object?> kv in map) {
                data[kv.Key] = kv.Value;
            }
        } catch (System.Exception ex) {
            Program.Direct.Console.ForegroundColor = System.ConsoleColor.Red;
            Program.Direct.Console.WriteLine("error reloading project.json:");
            Program.Direct.Console.WriteLine(ex.Message);
            Program.Direct.Console.ResetColor();
        }
    }

    /// <summary>
    /// Extracts any nested onsuccess operations from an operation map. Supports keys 'onsuccess' and 'on_success'.
    /// </summary>
    private static bool TryGetOnSuccessOperations(IDictionary<string, object?> op, out List<Dictionary<string, object?>>? ops) {
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
