using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RemakeEngine.Actions;
using RemakeEngine.Tools;
using Tomlyn;
using Tomlyn.Model;

namespace RemakeEngine.Core;

public sealed class OperationsEngine {
    private readonly String _rootPath;
    private readonly IToolResolver _tools;
    private readonly EngineConfig _engineConfig;
    private readonly Registries _registries;
    private readonly CommandBuilder _builder;
    private readonly GitTools _git;

    public OperationsEngine(String rootPath, IToolResolver tools, EngineConfig engineConfig) {
        _rootPath = rootPath;
        _tools = tools;
        _engineConfig = engineConfig;
        _registries = new Registries(rootPath);
        _builder = new CommandBuilder(rootPath);
        _git = new GitTools(System.IO.Path.Combine(rootPath, "RemakeRegistry", "Games"));
    }

    public Dictionary<String, Object?> ListGames() {
        Dictionary<String, Object?> games = new Dictionary<String, Object?>();
        // Also look up installed games to enrich entries with exe/title when available
        Dictionary<String, GameInfo> installed = _registries.DiscoverInstalledGames();
        foreach (KeyValuePair<String, GameInfo> kv in _registries.DiscoverGames()) {
            Dictionary<String, Object?> info = new Dictionary<String, Object?> {
                ["game_root"] = kv.Value.GameRoot,
                ["ops_file"] = kv.Value.OpsFile
            };
            if (installed.TryGetValue(kv.Key, out GameInfo? gi)) {
                if (!String.IsNullOrWhiteSpace(gi.ExePath))
                    info["exe"] = gi.ExePath;
                if (!String.IsNullOrWhiteSpace(gi.Title))
                    info["title"] = gi.Title;
            }
            games[kv.Key] = info;
        }
        return games;
    }

    // Installed-only helpers
    public Dictionary<String, Object?> GetInstalledGames()
    {
        Dictionary<String, Object?> games = new Dictionary<String, Object?>();
        foreach (KeyValuePair<String, GameInfo> kv in _registries.DiscoverInstalledGames())
        {
            Dictionary<String, Object?> info = new Dictionary<String, Object?> {
                ["game_root"] = kv.Value.GameRoot,
                ["ops_file"] = kv.Value.OpsFile
            };
            if (!String.IsNullOrWhiteSpace(kv.Value.ExePath))
                info["exe"] = kv.Value.ExePath;
            if (!String.IsNullOrWhiteSpace(kv.Value.Title))
                info["title"] = kv.Value.Title;
            games[kv.Key] = info;
        }
        return games;
    }

    public IReadOnlyDictionary<String, Object?> GetRegisteredModules()
        => _registries.GetRegisteredModules();

    public Boolean IsModuleInstalled(String name) {
        Dictionary<String, GameInfo> games = _registries.DiscoverInstalledGames();
        return games.ContainsKey(name);
    }

    public String? GetGameExecutable(String name) {
        Dictionary<String, GameInfo> games = _registries.DiscoverInstalledGames();
        return games.TryGetValue(name, out GameInfo? gi) ? gi.ExePath : null;
    }

    public String? GetGamePath(String name) {
        // Prefer installed location first, then fall back to downloaded location
        Dictionary<String, GameInfo> games = _registries.DiscoverInstalledGames();
        if (games.TryGetValue(name, out GameInfo? gi))
            return gi.GameRoot;
        String dir = System.IO.Path.Combine(_rootPath, "RemakeRegistry", "Games", name);
        return Directory.Exists(dir) ? dir : null;
    }

    public Boolean LaunchGame(String name) {
        String? exe = GetGameExecutable(name);
        String root = GetGamePath(name) ?? _rootPath;
        if (String.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            return false;
        try {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
                FileName = exe!,
                WorkingDirectory = root!,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            return true;
        } catch {
            return false;
        }
    }

    public String GetModuleState(String name) {
        String dir = System.IO.Path.Combine(_rootPath, "RemakeRegistry", "Games", name);
        return !Directory.Exists(dir) ? "not_downloaded" : IsModuleInstalled(name) ? "installed" : "downloaded";
    }

    public List<Dictionary<String, Object?>> LoadOperationsList(String opsFile) {
        String ext = Path.GetExtension(opsFile);
        if (ext.Equals(".toml", StringComparison.OrdinalIgnoreCase)) {
            Tomlyn.Syntax.DocumentSyntax tdoc = Toml.Parse(File.ReadAllText(opsFile));
            TomlTable model = tdoc.ToModel();
            List<Dictionary<String, Object?>> list = new List<Dictionary<String, Object?>>();
            if (model is TomlTable table) {
                foreach (KeyValuePair<String, Object> kv in table) {
                    if (kv.Value is TomlTableArray arr) {
                        foreach (TomlTable item in arr) {
                            if (item is TomlTable tt)
                                list.Add(ToMap(tt));
                        }
                    }
                }
            }
            return list;
        }
		using FileStream fs = File.OpenRead(opsFile);
        using JsonDocument jdoc = JsonDocument.Parse(fs);
        if (jdoc.RootElement.ValueKind == JsonValueKind.Array) {
            List<Dictionary<String, Object?>> list = new List<Dictionary<String, Object?>>();
            foreach (JsonElement item in jdoc.RootElement.EnumerateArray()) {
                if (item.ValueKind == JsonValueKind.Object)
                    list.Add(ToMap(item));
            }
            return list;
        }
        if (jdoc.RootElement.ValueKind == JsonValueKind.Object) {
            // Fallback: flatten grouped format into a single list (preserving group order)
            List<Dictionary<String, Object?>> flat = new List<Dictionary<String, Object?>>();
            foreach (JsonProperty prop in jdoc.RootElement.EnumerateObject()) {
                if (prop.Value.ValueKind == JsonValueKind.Array) {
                    foreach (JsonElement item in prop.Value.EnumerateArray()) {
                        if (item.ValueKind == JsonValueKind.Object)
                            flat.Add(ToMap(item));
                    }
                }
            }
            return flat;
        }
        return new();
    }

    public Dictionary<String, List<Dictionary<String, Object?>>> LoadOperations(String opsFile) {
        String ext = Path.GetExtension(opsFile);
        if (ext.Equals(".toml", StringComparison.OrdinalIgnoreCase)) {
            Tomlyn.Syntax.DocumentSyntax tdoc = Toml.Parse(File.ReadAllText(opsFile));
            TomlTable model = tdoc.ToModel();
            Dictionary<String, List<Dictionary<String, Object?>>> result = new Dictionary<String, List<Dictionary<String, Object?>>>(StringComparer.OrdinalIgnoreCase);
            if (model is TomlTable table) {
                foreach (KeyValuePair<String, Object> kv in table) {
                    if (kv.Value is TomlTableArray arr) {
                        List<Dictionary<String, Object?>> list = new List<Dictionary<String, Object?>>();
                        foreach (TomlTable item in arr) {
                            if (item is TomlTable tt)
                                list.Add(ToMap(tt));
                        }
                        result[kv.Key] = list;
                    }
                }
            }
            return result;
        }

        using FileStream fs = File.OpenRead(opsFile);
        using JsonDocument doc = JsonDocument.Parse(fs);
        Dictionary<String, List<Dictionary<String, Object?>>> resultJson = new Dictionary<String, List<Dictionary<String, Object?>>>(StringComparer.OrdinalIgnoreCase);
        if (doc.RootElement.ValueKind == JsonValueKind.Object) {
            foreach (JsonProperty prop in doc.RootElement.EnumerateObject()) {
                List<Dictionary<String, Object?>> list = new List<Dictionary<String, Object?>>();
                if (prop.Value.ValueKind == JsonValueKind.Array) {
                    foreach (JsonElement item in prop.Value.EnumerateArray()) {
                        if (item.ValueKind == JsonValueKind.Object)
                            list.Add(ToMap(item));
                    }
                }
                resultJson[prop.Name] = list;
            }
        }
        return resultJson;
    }

    private static Dictionary<String, Object?> ToMap(JsonElement obj) {
        Dictionary<String, Object?> dict = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty p in obj.EnumerateObject()) {
            dict[p.Name] = FromJson(p.Value);
        }
        return dict;
    }

    private static Object? FromJson(JsonElement el) {
        return el.ValueKind switch {
            JsonValueKind.Object => ToMap(el),
            JsonValueKind.Array => ToList(el),
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out Int64 l) ? l : el.TryGetDouble(out global::System.Double d) ? d : el.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static List<Object?> ToList(JsonElement arr) {
        List<Object?> list = new List<Object?>();
        foreach (JsonElement item in arr.EnumerateArray())
            list.Add(FromJson(item));
        return list;
    }

    private static Dictionary<String, Object?> ToMap(TomlTable table) {
        Dictionary<String, Object?> dict = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<String, Object> kv in table)
            dict[kv.Key] = FromToml(kv.Value);
        return dict;
    }

    private static Object? FromToml(Object? value) {
        switch (value) {
            case TomlTable tt:
                return ToMap(tt);
            case TomlTableArray ta:
                List<Object?> listTa = new List<Object?>();
                foreach (TomlTable item in ta)
                    listTa.Add(FromToml(item));
                return listTa;
            case TomlArray arr:
                List<Object?> listArr = new List<Object?>();
                foreach (Object? item in arr)
                    listArr.Add(FromToml(item));
                return listArr;
            default:
                return value;
        }
    }


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
                    LuaScriptAction action = new LuaScriptAction(scriptPath, args);
                    await action.ExecuteAsync(_tools, cancellationToken);
                    result = true;
                    break;
                }
                case "js": {
                    JsScriptAction action = new JsScriptAction(scriptPath, args);
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
                    ProcessRunner runner = new ProcessRunner();
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
                String resolvedManifest = Placeholders.Resolve(manifest!, ctx)?.ToString() ?? manifest!;
                String central = Path.Combine(_rootPath, "RemakeRegistry/Tools.json");
                Boolean force = false;
                if (promptAnswers.TryGetValue("force download", out Object? fd) && fd is Boolean b1)
                    force = b1;
                if (promptAnswers.TryGetValue("force_download", out Object? fd2) && fd2 is Boolean b2)
                    force = b2;

                ToolsDownloader dl = new Tools.ToolsDownloader(_rootPath, central);
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
                    IList<Object?> resolved = (IList<Object?>)(Placeholders.Resolve(aList, ctx) ?? new List<Object?>());
                    foreach (Object? a in resolved)
                        if (a is not null)
                            args.Add(a.ToString()!);
                }

                // If format is TXD, use built-in extractor

                if (String.Equals(format, "txd", StringComparison.OrdinalIgnoreCase)) {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine("\n>>> Built-in TXD extraction");
                    Console.ResetColor();
                    Boolean okTxd = TxdExtractor.Run(args);
                    return okTxd;
                } else if (String.Equals(format, "str", StringComparison.OrdinalIgnoreCase)) {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine("\\n>>> Built-in BMS extraction");
                    Console.ResetColor();
                    Boolean okBms = QuickBmsExtractor.Run(args);
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
                    IList<Object?> resolved = (IList<Object?>)(Placeholders.Resolve(aList, ctx) ?? new List<Object?>());
                    foreach (Object? a in resolved)
                        if (a is not null)
                            args.Add(a.ToString()!);
                }

                if (String.Equals(tool, "ffmpeg", StringComparison.OrdinalIgnoreCase) || String.Equals(tool, "vgmstream", StringComparison.OrdinalIgnoreCase)) {
                    // attempt built-in media conversion (ffmpeg/vgmstream) using the same CLI args
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine("\n>>> Built-in media conversion");
                    Console.ResetColor();
                    Boolean okMedia = MediaConverter.Run(args);
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
                    IList<Object?> resolved = (IList<Object?>)(Placeholders.Resolve(aListValidate, ctx) ?? new List<Object?>());
                    foreach (Object? a in resolved)
                        if (a is not null)
                            argsValidate.Add(a.ToString()!);
                }

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("\n>>> Built-in file validation");
                Console.ResetColor();
                Boolean okValidate = FileValidator.Run(argsValidate);
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
                    IList<Object?> resolved = (IList<Object?>)(Placeholders.Resolve(aListRename, ctx) ?? new List<Object?>());
                    foreach (Object? a in resolved)
                        if (a is not null)
                            args.Add(a.ToString()!);
                }

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("\n>>> Built-in folder rename");
                Console.ResetColor();
                Boolean okRename = FolderRenamer.Run(args);
                return okRename;
            }

            default:
                return false;
        }
    }

    private String ResolvePythonExecutable() {
        return "python";
    }

    // Refresh in-memory config from project.json so placeholders resolve with latest values.
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


    // --- CLI helpers to match Python CLI behavior ---
    public List<String> BuildCommand(
        String currentGame,
        IDictionary<String, Object?> games,
        IDictionary<String, Object?> op,
        IDictionary<String, Object?> promptAnswers)
        => _builder.Build(currentGame, games, _engineConfig.Data, op, promptAnswers);

    public Boolean ExecuteCommand(
        IList<String> commandParts,
        String title,
        ProcessRunner.OutputHandler? onOutput = null,
        ProcessRunner.EventHandler? onEvent = null,
        ProcessRunner.StdinProvider? stdinProvider = null,
        IDictionary<String, Object?>? envOverrides = null,
        CancellationToken cancellationToken = default) {
        ProcessRunner runner = new ProcessRunner();
        return runner.Execute(
            commandParts,
            title,
            onOutput: onOutput,
            onEvent: onEvent,
            stdinProvider: stdinProvider,
            envOverrides: envOverrides,
            cancellationToken: cancellationToken);
    }

    // --- Module management ---
    public Boolean DownloadModule(String url) => _git.CloneModule(url);

    // Install a downloaded module by running the "run-all" operation
    public async Task<Boolean> InstallModuleAsync(
        String name,
        ProcessRunner.OutputHandler? onOutput = null,
        ProcessRunner.EventHandler? onEvent = null,
        ProcessRunner.StdinProvider? stdinProvider = null,
        CancellationToken cancellationToken = default)
    {
        String gameDir = System.IO.Path.Combine(_rootPath, "RemakeRegistry", "Games", name);
        String opsToml = System.IO.Path.Combine(gameDir, "operations.toml");
        String opsJson = System.IO.Path.Combine(gameDir, "operations.json");
        String? opsFile = null;
        if (File.Exists(opsToml))
            opsFile = opsToml;
        else if (File.Exists(opsJson))
            opsFile = opsJson;
        if (opsFile is null)
            return false;

        // Build a minimal games map for the command builder
        Dictionary<String, Object?> games = new Dictionary<String, Object?> {
            [name] = new Dictionary<String, Object?> {
                ["game_root"] = gameDir,
                ["ops_file"] = opsFile,
            }
        };

        // Load groups or flatten
        Dictionary<String, List<Dictionary<String, Object?>>> groups = LoadOperations(opsFile);
        IList<Dictionary<String, Object?>> opsList;
        if (groups.Count > 0) {
            // Prefer a key named "run-all" (any case)
            String key = groups.Keys.FirstOrDefault(k => String.Equals(k, "run-all", StringComparison.OrdinalIgnoreCase))
                      ?? groups.Keys.First();
            opsList = groups[key];
        } else {
            opsList = LoadOperationsList(opsFile);
        }
        if (opsList.Count == 0)
            return false;

        // Run each op streaming output and events
        Boolean okAll = true;
        foreach (Dictionary<String, Object?> op in opsList) {
            if (cancellationToken.IsCancellationRequested)
                break;
            List<String> parts = BuildCommand(name, games, op, new Dictionary<String, Object?>());
            if (parts.Count == 0)
                continue;
            String title = op.TryGetValue("Name", out Object? n) ? n?.ToString() ?? System.IO.Path.GetFileName(parts[1]) : System.IO.Path.GetFileName(parts[1]);
            Boolean ok = ExecuteCommand(parts, title, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdinProvider, cancellationToken: cancellationToken);
            // After each operation, refresh project.json in memory for subsequent resolutions.
            ReloadProjectConfig();
            if (!ok)
                okAll = false;
        }

        return okAll;
    }
}
