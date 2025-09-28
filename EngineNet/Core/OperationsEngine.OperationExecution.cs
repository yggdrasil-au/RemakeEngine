using System;
using System.Collections.Generic;
using System.IO;
using System.Buffers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
namespace EngineNet.Core;

public sealed partial class OperationsEngine {
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
    public async Task<Boolean> RunOperationGroupAsync(
        String gameName,
		IDictionary<String, Object?> games,
        String groupName,
		IList<Dictionary<String, Object?>> operations,
		IDictionary<String, Object?> promptAnswers,
		CancellationToken cancellationToken = default)
	{
        Boolean success = true;
		foreach (Dictionary<String, Object?> op in operations)
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
    public async Task<Boolean> RunSingleOperationAsync(
        String currentGame,
        IDictionary<String, Object?> games,
        IDictionary<String, Object?> op,
        IDictionary<String, Object?> promptAnswers,
        CancellationToken cancellationToken = default) {
        String scriptType = (op.TryGetValue("script_type", out Object? st) ? st?.ToString() : null)?.ToLowerInvariant() ?? "python";
        List<String> parts = _builder.Build(currentGame, games, _engineConfig.Data, op, promptAnswers);
        if (parts.Count < 2) {
            return false;
        }

        String scriptPath = parts[1];
        String[] args = parts.Skip(2).ToArray();

        Boolean result = false;
        try {
            switch (scriptType) {
                case "lua": {
                    try {
                        Core.ScriptEngines.LuaScriptAction action = new Core.ScriptEngines.LuaScriptAction(scriptPath, args);
                        await action.ExecuteAsync(_tools, cancellationToken);
                        result = true;
                    } catch (Exception ex) {
                        Console.Error.WriteLine($"ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                case "js": {
                    try {
                        Core.ScriptEngines.JsScriptAction action = new Core.ScriptEngines.JsScriptAction(scriptPath, args);
                        await action.ExecuteAsync(_tools, cancellationToken);
                        result = true;
                    } catch (Exception ex) {
                        Console.Error.WriteLine($"ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                case "bms": {
                    try {
                        if (!games.TryGetValue(currentGame, out Object? gobjBms) || gobjBms is not IDictionary<String, Object?> gdictBms) {
                            throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                        }
                        String gameRootBms = gdictBms.TryGetValue("game_root", out Object? grBms) ? grBms?.ToString() ?? String.Empty : String.Empty;

                        // Build placeholder context
                        Dictionary<String, Object?> ctx = new Dictionary<String, Object?>(_engineConfig.Data, StringComparer.OrdinalIgnoreCase) {
                            ["Game_Root"] = gameRootBms,
                            ["Project_Root"] = _rootPath,
                            ["Registry_Root"] = Path.Combine(_rootPath, "RemakeRegistry"),
                            ["Game"] = new Dictionary<String, Object?> { ["RootPath"] = gameRootBms, ["Name"] = currentGame },
                        };
                        if (!ctx.TryGetValue("RemakeEngine", out Object? reB) || reB is not IDictionary<String, Object?> reBdict) {
                            ctx["RemakeEngine"] = reBdict = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
                        }
                        if (!reBdict.TryGetValue("Config", out Object? cfgB) || cfgB is not IDictionary<String, Object?> cfgBdict) {
                            reBdict["Config"] = cfgBdict = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
                        }
                        ((IDictionary<String, Object?>)ctx["RemakeEngine"]!)["Config"] = cfgBdict;
                        cfgBdict["module_path"] = gameRootBms;
                        cfgBdict["project_path"] = _rootPath;
                        try {
                            String cfgPath = Path.Combine(gameRootBms, "config.toml");
                            if (!String.IsNullOrWhiteSpace(gameRootBms) && File.Exists(cfgPath)) {
                                Dictionary<String, Object?> fromToml = EngineNet.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                                foreach (KeyValuePair<String, Object?> kv in fromToml) {
                                    if (!ctx.ContainsKey(kv.Key)) ctx[kv.Key] = kv.Value;
                                }
                            }
                        } catch { }

                        String inputDir = op.TryGetValue("input", out Object? in0) ? in0?.ToString() ?? String.Empty : String.Empty;
                        String outputDir = op.TryGetValue("output", out Object? out0) ? out0?.ToString() ?? String.Empty : String.Empty;
                        String? extension = op.TryGetValue("extension", out Object? ext0) ? ext0?.ToString() : null;
                        String resolvedInput = Sys.Placeholders.Resolve(inputDir, ctx)?.ToString() ?? inputDir;
                        String resolvedOutput = Sys.Placeholders.Resolve(outputDir, ctx)?.ToString() ?? outputDir;
                        String? resolvedExt = extension is null ? null : Sys.Placeholders.Resolve(extension, ctx)?.ToString() ?? extension;

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
                    } catch (Exception ex) {
                        Console.Error.WriteLine($"ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                case "engine": {
                    try {
                        String? action = op.TryGetValue("script", out Object? s) ? s?.ToString() : null;
                        String? title = op.TryGetValue("Name", out Object? n) ? n?.ToString() ?? action : action;
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.WriteLine($"\n>>> Engine operation: {title}");
                        Console.ResetColor();
                        result = await ExecuteEngineOperationAsync(currentGame, games, op, promptAnswers, cancellationToken);
                    } catch (Exception ex) {
                        Console.Error.WriteLine($"ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                case "python":
                default: {
                    Sys.ProcessRunner runner = new Sys.ProcessRunner();
                    result = runner.Execute(parts, Path.GetFileName(scriptPath), cancellationToken: cancellationToken);
                    break;
                }
            }
        } finally {
            // Ensure we pick up any config changes (e.g., init.lua writes project.json)
            ReloadProjectConfig();
        }
        return result;
    }

    /// <summary>
    /// Executes built-in engine actions such as tool downloads, format extraction/conversion,
    /// file validation, and folder rename helpers.
    /// </summary>
    public async Task<Boolean> ExecuteEngineOperationAsync(
        String currentGame,
        IDictionary<String, Object?> games,
        IDictionary<String, Object?> op,
        IDictionary<String, Object?> promptAnswers,
        CancellationToken cancellationToken = default) {
        if (!op.TryGetValue("script", out Object? s) || s is null) {
            return false;
        }

        String? action = s.ToString()?.ToLowerInvariant();
        switch (action) {
            case "download_tools": {
                // Expect a 'tools_manifest' value (path), or fallback to first arg
                String? manifest = null;
                if (op.TryGetValue("tools_manifest", out Object? tm) && tm is not null) {
                    manifest = tm.ToString();
                } else if (op.TryGetValue("args", out Object? argsObj) && argsObj is IList<Object?> list && list.Count > 0) {
                    manifest = list[0]?.ToString();
                }

                if (String.IsNullOrWhiteSpace(manifest)) {
                    return false;
                }

                Dictionary<String, Object?> ctx = new Dictionary<String, Object?>(_engineConfig.Data, StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out Object? gobj) || gobj is not IDictionary<String, Object?> gdict) {
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                // Built-in placeholders
                String gameRoot = gdict.TryGetValue("game_root", out Object? gr0) ? gr0?.ToString() ?? String.Empty : String.Empty;
                ctx["Game_Root"] = gameRoot;
                ctx["Project_Root"] = _rootPath;
                ctx["Registry_Root"] = System.IO.Path.Combine(_rootPath, "RemakeRegistry");
                ctx["Game"] = new Dictionary<String, Object?> {
                    ["RootPath"] = gameRoot,
                    ["Name"] = currentGame,
                };
                if (!ctx.TryGetValue("RemakeEngine", out Object? re0) || re0 is not IDictionary<String, Object?> reDict0) {
                    ctx["RemakeEngine"] = reDict0 = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
                }

                if (!reDict0.TryGetValue("Config", out Object? cfg0) || cfg0 is not IDictionary<String, Object?> cfgDict0) {
                    reDict0["Config"] = cfgDict0 = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
                }
                // Merge module placeholders from config.toml
                try {
                    String cfgPath = System.IO.Path.Combine(gameRoot, "config.toml");
                    if (!String.IsNullOrWhiteSpace(gameRoot) && System.IO.File.Exists(cfgPath)) {
                        Dictionary<String, Object?> fromToml = EngineNet.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                        foreach (KeyValuePair<String, Object?> kv in fromToml) {
                            if (!ctx.ContainsKey(kv.Key)) {
                                ctx[kv.Key] = kv.Value;
                            }
                        }
                    }
                } catch { }
                cfgDict0["module_path"] = gameRoot;
                cfgDict0["project_path"] = _rootPath;
                String resolvedManifest = Sys.Placeholders.Resolve(manifest!, ctx)?.ToString() ?? manifest!;
                String central = Path.Combine(_rootPath, "RemakeRegistry/Tools.json");
                Boolean force = false;
                if (promptAnswers.TryGetValue("force download", out Object? fd) && fd is Boolean b1) {
                    force = b1;
                }

                if (promptAnswers.TryGetValue("force_download", out Object? fd2) && fd2 is Boolean b2) {
                    force = b2;
                }

                Tools.ToolsDownloader dl = new Tools.ToolsDownloader(_rootPath, central);
                await dl.ProcessAsync(resolvedManifest, force);
                return true;
            }
            case "format-extract":
            case "format_extract": {
                // Determine input file format
                String? format = op.TryGetValue("format", out Object? ft) ? ft?.ToString()?.ToLowerInvariant() : null;

                // Resolve args (used for both TXD and media conversions)
                Dictionary<String, Object?> ctx = new Dictionary<String, Object?>(_engineConfig.Data, StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out Object? gobj) || gobj is not IDictionary<String, Object?> gdict2) {
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                // Built-in placeholders
                String gameRoot2 = gdict2.TryGetValue("game_root", out Object? gr2a) ? gr2a?.ToString() ?? String.Empty : String.Empty;
                ctx["Game_Root"] = gameRoot2;
                ctx["Project_Root"] = _rootPath;
                ctx["Registry_Root"] = System.IO.Path.Combine(_rootPath, "RemakeRegistry");
                ctx["Game"] = new Dictionary<String, Object?> {
                    ["RootPath"] = gameRoot2,
                    ["Name"] = currentGame,
                };
                if (!ctx.TryGetValue("RemakeEngine", out Object? re1) || re1 is not IDictionary<String, Object?> reDict1) {
                    ctx["RemakeEngine"] = reDict1 = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
                }

                if (!reDict1.TryGetValue("Config", out Object? cfg1) || cfg1 is not IDictionary<String, Object?> cfgDict1) {
                    reDict1["Config"] = cfgDict1 = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
                }
                // Merge module placeholders from config.toml
                try {
                    String cfgPath = System.IO.Path.Combine(gameRoot2, "config.toml");
                    if (!String.IsNullOrWhiteSpace(gameRoot2) && System.IO.File.Exists(cfgPath)) {
                        Dictionary<String, Object?> fromToml = EngineNet.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                        foreach (KeyValuePair<String, Object?> kv in fromToml) {
                            if (!ctx.ContainsKey(kv.Key)) {
                                ctx[kv.Key] = kv.Value;
                            }
                        }
                    }
                } catch { }
                cfgDict1["module_path"] = gameRoot2;
                cfgDict1["project_path"] = _rootPath;

                List<String> args = new List<String>();
                if (op.TryGetValue("args", out Object? aobj) && aobj is IList<Object?> aList) {
                    IList<Object?> resolved = (IList<Object?>)(Sys.Placeholders.Resolve(aList, ctx) ?? new List<Object?>());
                    foreach (Object? a in resolved) {
                        if (a is not null) {
                            args.Add(a.ToString()!);
                        }
                    }
                }

                // If format is TXD, use built-in extractor

                if (String.Equals(format, "txd", StringComparison.OrdinalIgnoreCase)) {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine("\n>>> Built-in TXD extraction");
                    Console.ResetColor();
                    Boolean okTxd = FileHandlers.TxdExtractor.Main.Run(args);
                    return okTxd;
                } else {
                    return false;
                }
            }
            case "format-convert":
            case "format_convert": {
                // Determine tool
                String? tool = op.TryGetValue("tool", out Object? ft) ? ft?.ToString()?.ToLowerInvariant() : null;

                // Resolve args (used for both TXD and media conversions)
                Dictionary<String, Object?> ctx = new Dictionary<String, Object?>(_engineConfig.Data, StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out Object? gobj) || gobj is not IDictionary<String, Object?> gdict2) {
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                // Built-in placeholders
                String gameRoot3 = gdict2.TryGetValue("game_root", out Object? gr3a) ? gr3a?.ToString() ?? String.Empty : String.Empty;
                ctx["Game_Root"] = gameRoot3;
                ctx["Project_Root"] = _rootPath;
                ctx["Registry_Root"] = System.IO.Path.Combine(_rootPath, "RemakeRegistry");
                ctx["Game"] = new Dictionary<String, Object?> {
                    ["RootPath"] = gameRoot3,
                    ["Name"] = currentGame,
                };
                if (!ctx.TryGetValue("RemakeEngine", out Object? re2) || re2 is not IDictionary<String, Object?> reDict2) {
                    ctx["RemakeEngine"] = reDict2 = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
                }

                if (!reDict2.TryGetValue("Config", out Object? cfg2) || cfg2 is not IDictionary<String, Object?> cfgDict2) {
                    reDict2["Config"] = cfgDict2 = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
                }
                // Merge module placeholders from config.toml
                try {
                    String cfgPath = System.IO.Path.Combine(gameRoot3, "config.toml");
                    if (!String.IsNullOrWhiteSpace(gameRoot3) && System.IO.File.Exists(cfgPath)) {
                        Dictionary<String, Object?> fromToml = EngineNet.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                        foreach (KeyValuePair<String, Object?> kv in fromToml) {
                            if (!ctx.ContainsKey(kv.Key)) {
                                ctx[kv.Key] = kv.Value;
                            }
                        }
                    }
                } catch { }
                cfgDict2["module_path"] = gameRoot3;
                cfgDict2["project_path"] = _rootPath;

                List<String> args = new List<String>();
                if (op.TryGetValue("args", out Object? aobj) && aobj is IList<Object?> aList) {
                    IList<Object?> resolved = (IList<Object?>)(Sys.Placeholders.Resolve(aList, ctx) ?? new List<Object?>());
                    foreach (Object? a in resolved) {
                        if (a is not null) {
                            args.Add(a.ToString()!);
                        }
                    }
                }

                if (String.Equals(tool, "ffmpeg", StringComparison.OrdinalIgnoreCase) || String.Equals(tool, "vgmstream", StringComparison.OrdinalIgnoreCase)) {
                    // attempt built-in media conversion (ffmpeg/vgmstream) using the same CLI args
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine("\n>>> Built-in media conversion");
                    Console.ResetColor();
                    Boolean okMedia = FileHandlers.MediaConverter.Run(args);
                    return okMedia;
                } else {
                    return false;
                }
            }
            case "validate-files":
            case "validate_files": {
                Dictionary<String, Object?> ctx = new Dictionary<String, Object?>(_engineConfig.Data, StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out Object? gobjValidate) || gobjValidate is not IDictionary<String, Object?> gdictValidate) {
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                // Built-in placeholders
                String gameRoot4 = gdictValidate.TryGetValue("game_root", out Object? grValidate0) ? grValidate0?.ToString() ?? String.Empty : String.Empty;
                ctx["Game_Root"] = gameRoot4;
                ctx["Project_Root"] = _rootPath;
                ctx["Registry_Root"] = System.IO.Path.Combine(_rootPath, "RemakeRegistry");
                ctx["Game"] = new Dictionary<String, Object?> {
                    ["RootPath"] = gameRoot4,
                    ["Name"] = currentGame,
                };
                if (!ctx.TryGetValue("RemakeEngine", out Object? re3) || re3 is not IDictionary<String, Object?> reDict3) {
                    ctx["RemakeEngine"] = reDict3 = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
                }

                if (!reDict3.TryGetValue("Config", out Object? cfg3) || cfg3 is not IDictionary<String, Object?> cfgDict3) {
                    reDict3["Config"] = cfgDict3 = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
                }
                // Merge module placeholders from config.toml
                try {
                    String cfgPath = System.IO.Path.Combine(gameRoot4, "config.toml");
                    if (!String.IsNullOrWhiteSpace(gameRoot4) && System.IO.File.Exists(cfgPath)) {
                        Dictionary<String, Object?> fromToml = EngineNet.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                        foreach (KeyValuePair<String, Object?> kv in fromToml) {
                            if (!ctx.ContainsKey(kv.Key)) {
                                ctx[kv.Key] = kv.Value;
                            }
                        }
                    }
                } catch { }
                cfgDict3["module_path"] = gameRoot4;
                cfgDict3["project_path"] = _rootPath;

                List<String> argsValidate = new List<String>();
                if (op.TryGetValue("args", out Object? aobjValidate) && aobjValidate is IList<Object?> aListValidate) {
                    IList<Object?> resolved = (IList<Object?>)(Sys.Placeholders.Resolve(aListValidate, ctx) ?? new List<Object?>());
                    foreach (Object? a in resolved) {
                        if (a is not null) {
                            argsValidate.Add(a.ToString()!);
                        }
                    }
                }

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("\n>>> Built-in file validation");
                Console.ResetColor();
                Boolean okValidate = FileHandlers.FileValidator.Run(argsValidate);
                return okValidate;
            }
            case "rename-folders":
            case "rename_folders": {
                Dictionary<String, Object?> ctx = new Dictionary<String, Object?>(_engineConfig.Data, StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out Object? gobj3) || gobj3 is not IDictionary<String, Object?> gdict3) {
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                // Built-in placeholders
                String gameRoot5 = gdict3.TryGetValue("game_root", out Object? gr3) ? gr3?.ToString() ?? String.Empty : String.Empty;
                ctx["Game_Root"] = gameRoot5;
                ctx["Project_Root"] = _rootPath;
                ctx["Registry_Root"] = System.IO.Path.Combine(_rootPath, "RemakeRegistry");
                ctx["Game"] = new Dictionary<String, Object?> {
                    ["RootPath"] = gameRoot5,
                    ["Name"] = currentGame,
                };
                if (!ctx.TryGetValue("RemakeEngine", out Object? re4) || re4 is not IDictionary<String, Object?> reDict4) {
                    ctx["RemakeEngine"] = reDict4 = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
                }

                if (!reDict4.TryGetValue("Config", out Object? cfg4) || cfg4 is not IDictionary<String, Object?> cfgDict4) {
                    reDict4["Config"] = cfgDict4 = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
                }
                // Merge module placeholders from config.toml
                try {
                    String cfgPath = System.IO.Path.Combine(gameRoot5, "config.toml");
                    if (!String.IsNullOrWhiteSpace(gameRoot5) && System.IO.File.Exists(cfgPath)) {
                        Dictionary<String, Object?> fromToml = EngineNet.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                        foreach (KeyValuePair<String, Object?> kv in fromToml) {
                            if (!ctx.ContainsKey(kv.Key)) {
                                ctx[kv.Key] = kv.Value;
                            }
                        }
                    }
                } catch { }
                cfgDict4["module_path"] = gameRoot5;
                cfgDict4["project_path"] = _rootPath;

                List<String> args = new List<String>();
                if (op.TryGetValue("args", out Object? aobjRename) && aobjRename is IList<Object?> aListRename) {
                    IList<Object?> resolved = (IList<Object?>)(Sys.Placeholders.Resolve(aListRename, ctx) ?? new List<Object?>());
                    foreach (Object? a in resolved) {
                        if (a is not null) {
                            args.Add(a.ToString()!);
                        }
                    }
                }

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("\n>>> Built-in folder rename");
                Console.WriteLine($"with args: {String.Join(' ', args)}");
                Console.ResetColor();
                Boolean okRename = FileHandlers.FolderRenamer.Run(args);
                return okRename;
            }
            case "flatten":
            case "flatten-folder-structure": {
                Dictionary<String, Object?> ctx = new Dictionary<String, Object?>(_engineConfig.Data, StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out Object? gobjFlatten) || gobjFlatten is not IDictionary<String, Object?> gdictFlatten) {
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                }
                // Built-in placeholders
                String gameRoot6 = gdictFlatten.TryGetValue("game_root", out Object? grFlatten0) ? grFlatten0?.ToString() ?? String.Empty : String.Empty;
                ctx["Game_Root"] = gameRoot6;
                ctx["Project_Root"] = _rootPath;
                ctx["Registry_Root"] = System.IO.Path.Combine(_rootPath, "RemakeRegistry");
                ctx["Game"] = new Dictionary<String, Object?> {
                    ["RootPath"] = gameRoot6,
                    ["Name"] = currentGame,
                };
                if (!ctx.TryGetValue("RemakeEngine", out Object? re5) || re5 is not IDictionary<String, Object?> reDict5) {
                    ctx["RemakeEngine"] = reDict5 = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
                }

                if (!reDict5.TryGetValue("Config", out Object? cfg5) || cfg5 is not IDictionary<String, Object?> cfgDict5) {
                    reDict5["Config"] = cfgDict5 = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
                }
                // Merge module placeholders from config.toml
                try {
                    String cfgPath = System.IO.Path.Combine(gameRoot6, "config.toml");
                    if (!String.IsNullOrWhiteSpace(gameRoot6) && System.IO.File.Exists(cfgPath)) {
                        Dictionary<String, Object?> fromToml = EngineNet.Tools.SimpleToml.ReadPlaceholdersFile(cfgPath);
                        foreach (KeyValuePair<String, Object?> kv in fromToml) {
                            if (!ctx.ContainsKey(kv.Key)) {
                                ctx[kv.Key] = kv.Value;
                            }
                        }
                    }
                } catch { }
                cfgDict5["module_path"] = gameRoot6;
                cfgDict5["project_path"] = _rootPath;

                List<String> argsFlatten = new List<String>();
                if (op.TryGetValue("args", out Object? aobjFlatten) && aobjFlatten is IList<Object?> aListFlatten) {
                    IList<Object?> resolved = (IList<Object?>)(Sys.Placeholders.Resolve(aListFlatten, ctx) ?? new List<Object?>());
                    foreach (Object? a in resolved) {
                        if (a is not null) {
                            argsFlatten.Add(a.ToString()!);
                        }
                    }
                }

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("\n>>> Built-in directory flatten");
                Console.ResetColor();
                Boolean okFlatten = FileHandlers.DirectoryFlattener.Run(argsFlatten);
                return okFlatten;
            }

            default:
                return false;
        }
    }

    private void ReloadProjectConfig() {
        try {
            String projectJson = Path.Combine(_rootPath, "project.json");
            if (!File.Exists(projectJson)) {
                return;
            }

            using FileStream fs = File.OpenRead(projectJson);
            using JsonDocument doc = JsonDocument.Parse(fs);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                return;
            }

            Dictionary<String, Object?> map = ToMap(doc.RootElement);
            IDictionary<String, Object?>? data = _engineConfig.Data;
            if (data is null) {
                return;
            }

            data.Clear();
            foreach (KeyValuePair<String, Object?> kv in map) {
                data[kv.Key] = kv.Value;
            }
        } catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("error reloading project.json:");
            Console.Error.WriteLine(ex.Message);
            Console.ResetColor();
        }
    }
}
