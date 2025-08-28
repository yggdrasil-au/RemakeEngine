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

public sealed class OperationsEngine
{
    private readonly string _rootPath;
    private readonly IToolResolver _tools;
    private readonly EngineConfig _engineConfig;
    private readonly Registries _registries;
    private readonly CommandBuilder _builder;

    public OperationsEngine(string rootPath, IToolResolver tools, EngineConfig engineConfig)
    {
        _rootPath = rootPath;
        _tools = tools;
        _engineConfig = engineConfig;
        _registries = new Registries(rootPath);
        _builder = new CommandBuilder(rootPath);
    }

    public Dictionary<string, object?> ListGames()
    {
        var games = new Dictionary<string, object?>();
        foreach (var kv in _registries.DiscoverGames())
        {
            games[kv.Key] = new Dictionary<string, object?>
            {
                ["game_root"] = kv.Value.GameRoot,
                ["ops_file"] = kv.Value.OpsFile
            };
        }
        return games;
    }

    public List<Dictionary<string, object?>> LoadOperationsList(string opsFile)
    {
        using var fs = File.OpenRead(opsFile);
        using var doc = JsonDocument.Parse(fs);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            fs.Position = 0;
            var list = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(fs, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return list ?? new();
        }
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            // Fallback: flatten grouped format into a single list (preserving group order)
            var flat = new List<Dictionary<string, object?>>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object)
                        {
                            var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(item.GetRawText(), new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });
                            if (dict != null) flat.Add(dict);
                        }
                    }
                }
            }
            return flat;
        }
        return new();
    }

    public Dictionary<string, List<Dictionary<string, object?>>> LoadOperations(string opsFile)
    {
        using var fs = File.OpenRead(opsFile);
        var doc = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, object?>>>>(fs, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        return doc ?? new();
    }

    public async Task<bool> RunOperationGroupAsync(
        string gameName,
        IDictionary<string, object?> games,
        string groupName,
        IList<Dictionary<string, object?>> operations,
        IDictionary<string, object?> promptAnswers,
        CancellationToken cancellationToken = default)
    {
        var success = true;
        foreach (var op in operations)
        {
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
        CancellationToken cancellationToken = default)
    {
        var scriptType = (op.TryGetValue("script_type", out var st) ? st?.ToString() : null)?.ToLowerInvariant() ?? "python";
        var parts = _builder.Build(currentGame, games, _engineConfig.Data, op, promptAnswers);
        if (parts.Count < 2)
            return false;

        var scriptPath = parts[1];
        var args = parts.Skip(2).ToArray();

        switch (scriptType)
        {
            case "lua":
                {
                    var action = new LuaScriptAction(scriptPath, args);
                    await action.ExecuteAsync(_tools, cancellationToken);
                    return true;
                }
            case "js":
                {
                    var action = new JsScriptAction(scriptPath, args);
                    await action.ExecuteAsync(_tools, cancellationToken);
                    return true;
                }
            case "python":
            default:
                {
                    var runner = new ProcessRunner();
                    return runner.Execute(parts, Path.GetFileName(scriptPath), cancellationToken: cancellationToken);
                }
        }
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
        CancellationToken cancellationToken = default)
    {
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
}
