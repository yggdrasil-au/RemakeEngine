using System.Text.Json;

namespace RemakeEngine.Core;

public sealed partial class OperationsEngine {
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
			if (!await RunSingleOperationAsync(gameName, games, op, promptAnswers, cancellationToken))
				success = false;
		}
		return success;
	}

    public async Task<Boolean> RunSingleOperationAsync(
        String currentGame,
        IDictionary<String, Object?> games,
        IDictionary<String, Object?> op,
        IDictionary<String, Object?> promptAnswers,
        CancellationToken cancellationToken = default) {
        String scriptType = (op.TryGetValue("script_type", out Object? st) ? st?.ToString() : null)?.ToLowerInvariant() ?? "python";
        List<String> parts = _builder.Build(currentGame, games, _engineConfig.Data, op, promptAnswers);
        if (parts.Count < 2)
            return false;

        String scriptPath = parts[1];
        String[] args = parts.Skip(2).ToArray();

        Boolean result = false;
        try {
            switch (scriptType) {
                case "lua": {
                    Core.ScriptEngines.LuaScriptAction action = new Core.ScriptEngines.LuaScriptAction(scriptPath, args);
                    await action.ExecuteAsync(_tools, cancellationToken);
                    result = true;
                    break;
                }
                case "js": {
                    Core.ScriptEngines.JsScriptAction action = new Core.ScriptEngines.JsScriptAction(scriptPath, args);
                    await action.ExecuteAsync(_tools, cancellationToken);
                    result = true;
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

    public async Task<Boolean> ExecuteEngineOperationAsync(
        String currentGame,
        IDictionary<String, Object?> games,
        IDictionary<String, Object?> op,
        IDictionary<String, Object?> promptAnswers,
        CancellationToken cancellationToken = default) {
        if (!op.TryGetValue("script", out Object? s) || s is null)
            return false;
        String? action = s.ToString()?.ToLowerInvariant();
        switch (action) {
            case "download_tools": {
                // Expect a 'tools_manifest' value (path), or fallback to first arg
                String? manifest = null;
                if (op.TryGetValue("tools_manifest", out Object? tm) && tm is not null)
                    manifest = tm.ToString();
                else if (op.TryGetValue("args", out Object? argsObj) && argsObj is IList<Object?> list && list.Count > 0)
                    manifest = list[0]?.ToString();

                if (String.IsNullOrWhiteSpace(manifest))
                    return false;

                Dictionary<String, Object?> ctx = new Dictionary<String, Object?>(_engineConfig.Data, StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out Object? gobj) || gobj is not IDictionary<String, Object?> gdict)
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                ctx["Game"] = new Dictionary<String, Object?> {
                    ["RootPath"] = gdict.TryGetValue("game_root", out Object? gr) ? gr?.ToString() : String.Empty,
                    ["Name"] = currentGame,
                };
                String resolvedManifest = Sys.Placeholders.Resolve(manifest!, ctx)?.ToString() ?? manifest!;
                String central = Path.Combine(_rootPath, "RemakeRegistry/Tools.json");
                Boolean force = false;
                if (promptAnswers.TryGetValue("force download", out Object? fd) && fd is Boolean b1)
                    force = b1;
                if (promptAnswers.TryGetValue("force_download", out Object? fd2) && fd2 is Boolean b2)
                    force = b2;

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
                if (!games.TryGetValue(currentGame, out Object? gobj) || gobj is not IDictionary<String, Object?> gdict2)
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                ctx["Game"] = new Dictionary<String, Object?> {
                    ["RootPath"] = gdict2.TryGetValue("game_root", out Object? gr2) ? gr2?.ToString() : String.Empty,
                    ["Name"] = currentGame,
                };

                List<String> args = new List<String>();
                if (op.TryGetValue("args", out Object? aobj) && aobj is IList<Object?> aList) {
                    IList<Object?> resolved = (IList<Object?>)(Sys.Placeholders.Resolve(aList, ctx) ?? new List<Object?>());
                    foreach (Object? a in resolved)
                        if (a is not null)
                            args.Add(a.ToString()!);
                }

                // If format is TXD, use built-in extractor

                if (String.Equals(format, "txd", StringComparison.OrdinalIgnoreCase)) {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine("\n>>> Built-in TXD extraction");
                    Console.ResetColor();
                    Boolean okTxd = Sys.TxdExtractor.Run(args);
                    return okTxd;
                } else if (String.Equals(format, "str", StringComparison.OrdinalIgnoreCase)) {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine("\\n>>> Built-in BMS extraction");
                    Console.ResetColor();
                    Boolean okBms = Sys.QuickBmsExtractor.Run(args);
                    return okBms;
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
                if (!games.TryGetValue(currentGame, out Object? gobj) || gobj is not IDictionary<String, Object?> gdict2)
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                ctx["Game"] = new Dictionary<String, Object?> {
                    ["RootPath"] = gdict2.TryGetValue("game_root", out Object? gr2) ? gr2?.ToString() : String.Empty,
                    ["Name"] = currentGame,
                };

                List<String> args = new List<String>();
                if (op.TryGetValue("args", out Object? aobj) && aobj is IList<Object?> aList) {
                    IList<Object?> resolved = (IList<Object?>)(Sys.Placeholders.Resolve(aList, ctx) ?? new List<Object?>());
                    foreach (Object? a in resolved)
                        if (a is not null)
                            args.Add(a.ToString()!);
                }

                if (String.Equals(tool, "ffmpeg", StringComparison.OrdinalIgnoreCase) || String.Equals(tool, "vgmstream", StringComparison.OrdinalIgnoreCase)) {
                    // attempt built-in media conversion (ffmpeg/vgmstream) using the same CLI args
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine("\n>>> Built-in media conversion");
                    Console.ResetColor();
                    Boolean okMedia = Sys.MediaConverter.Run(args);
                    return okMedia;
                } else {
                    return false;
                }
            }
            case "validate-files":
            case "validate_files": {
                Dictionary<String, Object?> ctx = new Dictionary<String, Object?>(_engineConfig.Data, StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out Object? gobjValidate) || gobjValidate is not IDictionary<String, Object?> gdictValidate)
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                ctx["Game"] = new Dictionary<String, Object?> {
                    ["RootPath"] = gdictValidate.TryGetValue("game_root", out Object? grValidate) ? grValidate?.ToString() : String.Empty,
                    ["Name"] = currentGame,
                };

                List<String> argsValidate = new List<String>();
                if (op.TryGetValue("args", out Object? aobjValidate) && aobjValidate is IList<Object?> aListValidate) {
                    IList<Object?> resolved = (IList<Object?>)(Sys.Placeholders.Resolve(aListValidate, ctx) ?? new List<Object?>());
                    foreach (Object? a in resolved)
                        if (a is not null)
                            argsValidate.Add(a.ToString()!);
                }

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("\n>>> Built-in file validation");
                Console.ResetColor();
                Boolean okValidate = Sys.FileValidator.Run(argsValidate);
                return okValidate;
            }
            case "rename-folders":
            case "rename_folders": {
                Dictionary<String, Object?> ctx = new Dictionary<String, Object?>(_engineConfig.Data, StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out Object? gobj3) || gobj3 is not IDictionary<String, Object?> gdict3)
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                ctx["Game"] = new Dictionary<String, Object?> {
                    ["RootPath"] = gdict3.TryGetValue("game_root", out Object? gr3) ? gr3?.ToString() : String.Empty,
                    ["Name"] = currentGame,
                };

                List<String> args = new List<String>();
                if (op.TryGetValue("args", out Object? aobjRename) && aobjRename is IList<Object?> aListRename) {
                    IList<Object?> resolved = (IList<Object?>)(Sys.Placeholders.Resolve(aListRename, ctx) ?? new List<Object?>());
                    foreach (Object? a in resolved)
                        if (a is not null)
                            args.Add(a.ToString()!);
                }

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("\n>>> Built-in folder rename");
                Console.ResetColor();
                Boolean okRename = Sys.FolderRenamer.Run(args);
                return okRename;
            }

            default:
                return false;
        }
    }

    private String ResolvePythonExecutable() {
        return "python";
    }

    private void ReloadProjectConfig() {
        try {
            String projectJson = Path.Combine(_rootPath, "project.json");
            if (!File.Exists(projectJson))
                return;
            using FileStream fs = File.OpenRead(projectJson);
            using JsonDocument doc = JsonDocument.Parse(fs);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return;
            Dictionary<String, Object?> map = ToMap(doc.RootElement);
            IDictionary<String, Object?>? data = _engineConfig.Data;
            if (data is null)
                return;
            data.Clear();
            foreach (KeyValuePair<String, Object?> kv in map)
                data[kv.Key] = kv.Value;
        } catch {
            // Non-fatal; keep previous config if reload fails.
        }
    }
}
