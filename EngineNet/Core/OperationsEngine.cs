using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RemakeEngine.Actions;
using RemakeEngine.Tools;

namespace RemakeEngine.Core;

public sealed class OperationsEngine {
    private readonly string _rootPath;
    private readonly IToolResolver _tools;
    private readonly EngineConfig _engineConfig;
    private readonly Registries _registries;
    private readonly CommandBuilder _builder;
    private readonly GitTools _git;

    public OperationsEngine(string rootPath, IToolResolver tools, EngineConfig engineConfig) {
        _rootPath = rootPath;
        _tools = tools;
        _engineConfig = engineConfig;
        _registries = new Registries(rootPath);
        _builder = new CommandBuilder(rootPath);
        _git = new GitTools(System.IO.Path.Combine(rootPath, "RemakeRegistry", "Games"));
    }

    public Dictionary<string, object?> ListGames() {
        var games = new Dictionary<string, object?>();
        foreach (var kv in _registries.DiscoverGames()) {
            var info = new Dictionary<string, object?> {
                ["game_root"] = kv.Value.GameRoot,
                ["ops_file"] = kv.Value.OpsFile
            };
            if (!string.IsNullOrWhiteSpace(kv.Value.ExePath))
                info["exe"] = kv.Value.ExePath;
            if (!string.IsNullOrWhiteSpace(kv.Value.Title))
                info["title"] = kv.Value.Title;
            games[kv.Key] = info;
        }
        return games;
    }

    // Installed-only helpers
    public Dictionary<string, object?> GetInstalledGames()
        => ListGames();

    public bool IsModuleInstalled(string name) {
        var games = _registries.DiscoverGames();
        return games.ContainsKey(name);
    }

    public string? GetGameExecutable(string name) {
        var games = _registries.DiscoverGames();
        if (games.TryGetValue(name, out var gi))
            return gi.ExePath;
        return null;
    }

    public string? GetGamePath(string name) {
        // Prefer DiscoverGames (installed) first, then fall back to downloaded location
        var games = _registries.DiscoverGames();
        if (games.TryGetValue(name, out var gi))
            return gi.GameRoot;
        var dir = System.IO.Path.Combine(_rootPath, "RemakeRegistry", "Games", name);
        return Directory.Exists(dir) ? dir : null;
    }

    public bool LaunchGame(string name) {
        var exe = GetGameExecutable(name);
        var root = GetGamePath(name) ?? _rootPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            return false;
        try {
            var psi = new System.Diagnostics.ProcessStartInfo {
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

    public string GetModuleState(string name) {
        var dir = System.IO.Path.Combine(_rootPath, "RemakeRegistry", "Games", name);
        if (!Directory.Exists(dir))
            return "not_downloaded";
        return IsModuleInstalled(name) ? "installed" : "downloaded";
    }

    public List<Dictionary<string, object?>> LoadOperationsList(string opsFile) {
        using var fs = File.OpenRead(opsFile);
        using var doc = JsonDocument.Parse(fs);
        if (doc.RootElement.ValueKind == JsonValueKind.Array) {
            var list = new List<Dictionary<string, object?>>();
            foreach (var item in doc.RootElement.EnumerateArray()) {
                if (item.ValueKind == JsonValueKind.Object)
                    list.Add(ToMap(item));
            }
            return list;
        }
        if (doc.RootElement.ValueKind == JsonValueKind.Object) {
            // Fallback: flatten grouped format into a single list (preserving group order)
            var flat = new List<Dictionary<string, object?>>();
            foreach (var prop in doc.RootElement.EnumerateObject()) {
                if (prop.Value.ValueKind == JsonValueKind.Array) {
                    foreach (var item in prop.Value.EnumerateArray()) {
                        if (item.ValueKind == JsonValueKind.Object)
                            flat.Add(ToMap(item));
                    }
                }
            }
            return flat;
        }
        return new();
    }

    public Dictionary<string, List<Dictionary<string, object?>>> LoadOperations(string opsFile) {
        using var fs = File.OpenRead(opsFile);
        using var doc = JsonDocument.Parse(fs);
        var result = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
        if (doc.RootElement.ValueKind == JsonValueKind.Object) {
            foreach (var prop in doc.RootElement.EnumerateObject()) {
                var list = new List<Dictionary<string, object?>>();
                if (prop.Value.ValueKind == JsonValueKind.Array) {
                    foreach (var item in prop.Value.EnumerateArray()) {
                        if (item.ValueKind == JsonValueKind.Object)
                            list.Add(ToMap(item));
                    }
                }
                result[prop.Name] = list;
            }
        }
        return result;
    }

    private static Dictionary<string, object?> ToMap(JsonElement obj) {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in obj.EnumerateObject()) {
            dict[p.Name] = FromJson(p.Value);
        }
        return dict;
    }

    private static object? FromJson(JsonElement el) {
        return el.ValueKind switch {
            JsonValueKind.Object => ToMap(el),
            JsonValueKind.Array => ToList(el),
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.TryGetDouble(out var d) ? d : el.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static List<object?> ToList(JsonElement arr) {
        var list = new List<object?>();
        foreach (var item in arr.EnumerateArray())
            list.Add(FromJson(item));
        return list;
    }

    public async Task<bool> RunOperationGroupAsync(
        string gameName,
        IDictionary<string, object?> games,
        string groupName,
        IList<Dictionary<string, object?>> operations,
        IDictionary<string, object?> promptAnswers,
        CancellationToken cancellationToken = default) {
        var success = true;
        foreach (var op in operations) {
            if (!await RunSingleOperationAsync(gameName, games, op, promptAnswers, cancellationToken))
                success = false;
        }
        return success;
    }

    public async Task<bool> RunSingleOperationAsync(
        string currentGame,
        IDictionary<string, object?> games,
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers,
        CancellationToken cancellationToken = default) {
        var scriptType = (op.TryGetValue("script_type", out var st) ? st?.ToString() : null)?.ToLowerInvariant() ?? "python";
        var parts = _builder.Build(currentGame, games, _engineConfig.Data, op, promptAnswers);
        if (parts.Count < 2)
            return false;

        var scriptPath = parts[1];
        var args = parts.Skip(2).ToArray();

        switch (scriptType) {
            case "lua": {
                var action = new LuaScriptAction(scriptPath, args);
                await action.ExecuteAsync(_tools, cancellationToken);
                return true;
            }
            case "js": {
                var action = new JsScriptAction(scriptPath, args);
                await action.ExecuteAsync(_tools, cancellationToken);
                return true;
            }
            case "engine": {
                try {
                    var action = op.TryGetValue("script", out var s) ? s?.ToString() : null;
                    var title = op.TryGetValue("Name", out var n) ? n?.ToString() ?? action : action;
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine($"\n>>> Engine operation: {title}");
                    Console.ResetColor();
                    var ok = await ExecuteEngineOperationAsync(currentGame, games, op, promptAnswers, cancellationToken);
                    return ok;
                } catch (Exception ex) {
                    Console.Error.WriteLine($"ERROR: {ex.Message}");
                    return false;
                }
            }
            case "python":
            default: {
                var runner = new ProcessRunner();
                return runner.Execute(parts, Path.GetFileName(scriptPath), cancellationToken: cancellationToken);
            }
        }
    }

    public async Task<bool> ExecuteEngineOperationAsync(
        string currentGame,
        IDictionary<string, object?> games,
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers,
        CancellationToken cancellationToken = default) {
        if (!op.TryGetValue("script", out var s) || s is null)
            return false;
        var action = s.ToString()?.ToLowerInvariant();
        switch (action) {
            case "download_tools": {
                // Expect a 'tools_manifest' value (path), or fallback to first arg
                string? manifest = null;
                if (op.TryGetValue("tools_manifest", out var tm) && tm is not null)
                    manifest = tm.ToString();
                else if (op.TryGetValue("args", out var argsObj) && argsObj is IList<object?> list && list.Count > 0)
                    manifest = list[0]?.ToString();

                if (string.IsNullOrWhiteSpace(manifest))
                    return false;

                var ctx = new Dictionary<string, object?>(_engineConfig.Data, StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out var gobj) || gobj is not IDictionary<string, object?> gdict)
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                ctx["Game"] = new Dictionary<string, object?> {
                    ["RootPath"] = gdict.TryGetValue("game_root", out var gr) ? gr?.ToString() : string.Empty,
                    ["Name"] = currentGame,
                };
                var resolvedManifest = Placeholders.Resolve(manifest!, ctx)?.ToString() ?? manifest!;
                var central = Path.Combine(_rootPath, "Tools.json");
                var force = false;
                if (promptAnswers.TryGetValue("force download", out var fd) && fd is bool b1)
                    force = b1;
                if (promptAnswers.TryGetValue("force_download", out var fd2) && fd2 is bool b2)
                    force = b2;

                var dl = new Tools.ToolsDownloader(_rootPath, central);
                await dl.ProcessAsync(resolvedManifest, force);
                return true;
            }
            case "format-extract":
            case "format_extract": {
                // Determine input file format
                var format = op.TryGetValue("format", out var ft) ? ft?.ToString()?.ToLowerInvariant() : null;

                // Resolve args (used for both TXD and media conversions)
                var ctx = new Dictionary<string, object?>(_engineConfig.Data, StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out var gobj) || gobj is not IDictionary<string, object?> gdict2)
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                ctx["Game"] = new Dictionary<string, object?> {
                    ["RootPath"] = gdict2.TryGetValue("game_root", out var gr2) ? gr2?.ToString() : string.Empty,
                    ["Name"] = currentGame,
                };

                var args = new List<string>();
                if (op.TryGetValue("args", out var aobj) && aobj is IList<object?> aList) {
                    var resolved = (IList<object?>)(Placeholders.Resolve(aList, ctx) ?? new List<object?>());
                    foreach (var a in resolved)
                        if (a is not null)
                            args.Add(a.ToString()!);
                }

                // If format is TXD, delegate to existing Python handler
                if (string.Equals(format, "txd", StringComparison.OrdinalIgnoreCase)) {
                    var scriptPath = System.IO.Path.Combine(_rootPath, "EnginePy", "FormatHandlers", "Export_txd.py");
                    var pythonExe = ResolvePythonExecutable();

                    var parts = new List<string> { pythonExe, scriptPath };
                    parts.AddRange(args);

                    var runner = new ProcessRunner();
                    // Use simple console handlers to ensure output visibility
                    ProcessRunner.OutputHandler outH = (line, stream) => {
                        var prev = Console.ForegroundColor;
                        Console.ForegroundColor = (stream == "stderr") ? ConsoleColor.Red : ConsoleColor.Gray;
                        Console.WriteLine(line);
                        Console.ForegroundColor = prev;
                    };
                    return runner.Execute(
                        parts,
                        op.TryGetValue("Name", out var nn) ? nn?.ToString() ?? "format-extract" : "format-extract",
                        onOutput: outH,
                        onEvent: null,
                        stdinProvider: null,
                        envOverrides: new Dictionary<string, object?> { ["TERM"] = "dumb" },
                        cancellationToken: cancellationToken);
                } else if (string.Equals(format, "str", StringComparison.OrdinalIgnoreCase)) {
                    // Use existing BMS extraction script
                    var scriptPath = System.IO.Path.Combine(_rootPath, "EnginePy", "Tooling", "bms_extract.py");
                    var pythonExe = ResolvePythonExecutable();
                    var runner = new ProcessRunner();
                    // Use simple console handlers to ensure output visibility
                    ProcessRunner.OutputHandler outH = (line, stream) => {
                        var prev = Console.ForegroundColor;
                        Console.ForegroundColor = (stream == "stderr") ? ConsoleColor.Red : ConsoleColor.Gray;
                        Console.WriteLine(line);
                        Console.ForegroundColor = prev;
                    };
                    var parts = new List<string> { pythonExe, scriptPath };
                    parts.AddRange(args);
                    return runner.Execute(
                        parts,
                        op.TryGetValue("Name", out var nn) ? nn?.ToString() ?? "format-extract" : "format-extract",
                        onOutput: outH,
                        onEvent: null,
                        stdinProvider: null,
                        envOverrides: new Dictionary<string, object?> { ["TERM"] = "dumb" },
                        cancellationToken: cancellationToken);
                } else {
                    return false;
                }
            }
            case "format-convert":
            case "format_convert": {
                // Determine tool
                var tool = op.TryGetValue("tool", out var ft) ? ft?.ToString()?.ToLowerInvariant() : null;

                // Resolve args (used for both TXD and media conversions)
                var ctx = new Dictionary<string, object?>(_engineConfig.Data, StringComparer.OrdinalIgnoreCase);
                if (!games.TryGetValue(currentGame, out var gobj) || gobj is not IDictionary<string, object?> gdict2)
                    throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                ctx["Game"] = new Dictionary<string, object?> {
                    ["RootPath"] = gdict2.TryGetValue("game_root", out var gr2) ? gr2?.ToString() : string.Empty,
                    ["Name"] = currentGame,
                };

                var args = new List<string>();
                if (op.TryGetValue("args", out var aobj) && aobj is IList<object?> aList) {
                    var resolved = (IList<object?>)(Placeholders.Resolve(aList, ctx) ?? new List<object?>());
                    foreach (var a in resolved)
                        if (a is not null)
                            args.Add(a.ToString()!);
                }

                if (string.Equals(tool, "ffmpeg", StringComparison.OrdinalIgnoreCase) || string.Equals(tool, "vgmstream", StringComparison.OrdinalIgnoreCase)) {
                    // attempt built-in media conversion (ffmpeg/vgmstream) using the same CLI args
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine("\n>>> Built-in media conversion");
                    Console.ResetColor();
                    var okMedia = MediaConverter.Run(args);
                    return okMedia;
                } else {
                    return false;
                }
            }
            default:
                return false;
        }
    }

    private string ResolvePythonExecutable() {
        return "python";
    }


    // --- CLI helpers to match Python CLI behavior ---
    public List<string> BuildCommand(
        string currentGame,
        IDictionary<string, object?> games,
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers)
        => _builder.Build(currentGame, games, _engineConfig.Data, op, promptAnswers);

    public bool ExecuteCommand(
        IList<string> commandParts,
        string title,
        ProcessRunner.OutputHandler? onOutput = null,
        ProcessRunner.EventHandler? onEvent = null,
        ProcessRunner.StdinProvider? stdinProvider = null,
        IDictionary<string, object?>? envOverrides = null,
        CancellationToken cancellationToken = default) {
        var runner = new ProcessRunner();
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
    public bool DownloadModule(string url) => _git.CloneModule(url);

    // Install a downloaded module by running the "run-all" group (fallback to first group or flat list)
    public async Task<bool> InstallModuleAsync(
        string name,
        ProcessRunner.OutputHandler? onOutput = null,
        ProcessRunner.EventHandler? onEvent = null,
        ProcessRunner.StdinProvider? stdinProvider = null,
        CancellationToken cancellationToken = default)
    {
        var gameDir = System.IO.Path.Combine(_rootPath, "RemakeRegistry", "Games", name);
        var opsFile = System.IO.Path.Combine(gameDir, "operations.json");
        if (!File.Exists(opsFile))
            return false;

        // Build a minimal games map for the command builder
        var games = new Dictionary<string, object?> {
            [name] = new Dictionary<string, object?> {
                ["game_root"] = gameDir,
                ["ops_file"] = opsFile,
            }
        };

        // Load groups or flatten
        var groups = LoadOperations(opsFile);
        IList<Dictionary<string, object?>> opsList;
        if (groups.Count > 0) {
            // Prefer a key named "run-all" (any case)
            var key = groups.Keys.FirstOrDefault(k => string.Equals(k, "run-all", StringComparison.OrdinalIgnoreCase))
                      ?? groups.Keys.First();
            opsList = groups[key];
        } else {
            opsList = LoadOperationsList(opsFile);
        }
        if (opsList.Count == 0)
            return false;

        // Run each op streaming output and events
        var okAll = true;
        foreach (var op in opsList) {
            if (cancellationToken.IsCancellationRequested)
                break;
            var parts = BuildCommand(name, games, op, new Dictionary<string, object?>());
            if (parts.Count == 0)
                continue;
            var title = op.TryGetValue("Name", out var n) ? n?.ToString() ?? System.IO.Path.GetFileName(parts[1]) : System.IO.Path.GetFileName(parts[1]);
            var ok = ExecuteCommand(parts, title, onOutput: onOutput, onEvent: onEvent, stdinProvider: stdinProvider, cancellationToken: cancellationToken);
            if (!ok)
                okAll = false;
        }

        return okAll;
    }
}
