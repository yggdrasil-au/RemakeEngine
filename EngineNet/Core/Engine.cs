namespace EngineNet.Core;

public sealed record RunAllResult(string Game, bool Success, int TotalOperations, int SucceededOperations);

internal sealed partial class OperationsEngine {
    private readonly string _rootPath;

    private readonly Tools.IToolResolver _tools;
    private readonly EngineConfig _engineConfig;
    private readonly Sys.Registries _registries;
    private readonly Sys.CommandBuilder _builder;
    private readonly Sys.GitTools _git;

    public OperationsEngine(string rootPath, Tools.IToolResolver tools, EngineConfig engineConfig) {
        _rootPath = rootPath;
        _tools = tools;
        _engineConfig = engineConfig;
        _registries = new Sys.Registries(rootPath);
        _builder = new Sys.CommandBuilder(rootPath);
        _git = new Sys.GitTools(System.IO.Path.Combine(rootPath, "RemakeRegistry", "Games"));
    }

    // getters for primary values and objects

    public string GetRootPath() => _rootPath;
    public Tools.IToolResolver GetToolResolver() => _tools;
    public EngineConfig GetEngineConfig() => _engineConfig;
    public Sys.Registries GetRegistries() => _registries;
    public Sys.CommandBuilder GetCommandBuilder() => _builder;
    public Sys.GitTools GetGitTools() => _git;

    public async System.Threading.Tasks.Task<RunAllResult> RunAllAsync(
        string gameName,
        Sys.ProcessRunner.OutputHandler? onOutput = null,
        Sys.ProcessRunner.EventHandler? onEvent = null,
        Sys.ProcessRunner.StdinProvider? stdinProvider = null,
        System.Threading.CancellationToken cancellationToken = default) {

#if DEBUG
        Program.Direct.Console.WriteLine($"[ENGINE.cs] Starting RunAllAsync for game '{gameName}', onOutput: {(onOutput is null ? "null" : "set")}, onEvent: {(onEvent is null ? "null" : "set")}, stdinProvider: {(stdinProvider is null ? "null" : "set")}");
#endif

        if (string.IsNullOrWhiteSpace(gameName)) {
            throw new System.ArgumentException("Game name is required.", nameof(gameName));
        }

        Dictionary<string, object?> games = ListGames();
        if (!games.TryGetValue(gameName, out object? infoObj) || infoObj is not IDictionary<string, object?> gameInfo) {
            throw new KeyNotFoundException($"Game '{gameName}' not found.");
        }

        if (!gameInfo.TryGetValue("ops_file", out object? opsObj) || opsObj is not string opsFile || !System.IO.File.Exists(opsFile)) {
            throw new System.IO.FileNotFoundException($"Operations file for '{gameName}' is missing.", opsObj?.ToString());
        }

        List<Dictionary<string, object?>> allOps = LoadOperationsList(opsFile);
        List<Dictionary<string, object?>> selected = new List<Dictionary<string, object?>>();
        foreach (Dictionary<string, object?> op in allOps) {
            if (IsFlagSet(op, "init")) {
                AddUnique(selected, op);
            }
        }

        foreach (Dictionary<string, object?> op in allOps) {
            if (IsFlagSet(op, "run-all") || IsFlagSet(op, "run_all")) {
                AddUnique(selected, op);
            }
        }

        if (selected.Count == 0) {
            selected.AddRange(allOps);
        }

        EmitSequenceEvent(onEvent, "run-all-start", gameName, new Dictionary<string, object?> {
            ["total"] = selected.Count
        });

        System.IO.TextReader? previousReader = null;
        if (stdinProvider is not null) {
            previousReader = Program.Direct.Console.In;
            Program.Direct.Console.SetIn(new StdinRedirectReader(stdinProvider));
        }

        System.Action<Dictionary<string, object?>>? previousSink = ScriptEngines.Helpers.EngineSdk.LocalEventSink;
        bool previousMute = ScriptEngines.Helpers.EngineSdk.MuteStdoutWhenLocalSink;
        string currentOperation = string.Empty;
        if (onEvent is not null) {
            ScriptEngines.Helpers.EngineSdk.LocalEventSink = evt => {
                Dictionary<string, object?> payload = CloneEvent(evt);
                payload["game"] = gameName;
                if (!string.IsNullOrEmpty(currentOperation)) {
                    payload["operation"] = currentOperation;
                }

                onEvent(payload);
            };
            ScriptEngines.Helpers.EngineSdk.MuteStdoutWhenLocalSink = true;
        }

        bool overallSuccess = true;
        int succeeded = 0;

        try {
            for (int index = 0; index < selected.Count; index++) {
                if (cancellationToken.IsCancellationRequested) {
                    overallSuccess = false;
                    break;
                }

                Dictionary<string, object?> op = selected[index];
                currentOperation = ResolveOperationName(op);
                EmitSequenceEvent(onEvent, "run-all-op-start", gameName, new Dictionary<string, object?> {
                    ["index"] = index,
                    ["total"] = selected.Count,
                    ["name"] = currentOperation
                });

                Dictionary<string, object?> promptAnswers = BuildPromptDefaults(op);
                bool ok = false;
                try {
                    string scriptType = GetScriptType(op);
                    if (IsEmbeddedScript(scriptType)) {
                        ok = await RunSingleOperationAsync(gameName, games, op, promptAnswers, cancellationToken).ConfigureAwait(false);
                    } else {
                        List<string> command = BuildCommand(gameName, games, op, promptAnswers);
                        if (command.Count >= 2) {
                            Sys.ProcessRunner.EventHandler? proxy = onEvent is null ? null : evt => {
                                Dictionary<string, object?> payload = CloneEvent(evt);
                                payload["game"] = gameName;
                                payload["operation"] = currentOperation;
                                onEvent(payload);
                            };

                            ok = ExecuteCommand(
                                command,
                                currentOperation,
                                onOutput,
                                proxy,
                                stdinProvider,
                                cancellationToken: cancellationToken);
                        }
                    }
                } catch (System.Exception ex) {
                    overallSuccess = false;
                    EmitSequenceEvent(onEvent, "run-all-op-error", gameName, new Dictionary<string, object?> {
                        ["name"] = currentOperation,
                        ["message"] = ex.Message
                    });
                } finally {
                    ReloadProjectConfig();
                }

                overallSuccess &= ok;
                if (ok) {
                    succeeded++;
                }

                EmitSequenceEvent(onEvent, "run-all-op-end", gameName, new Dictionary<string, object?> {
                    ["index"] = index,
                    ["total"] = selected.Count,
                    ["name"] = currentOperation,
                    ["success"] = ok
                });
            }
        } finally {
            if (onEvent is not null) {
                ScriptEngines.Helpers.EngineSdk.LocalEventSink = previousSink;
                ScriptEngines.Helpers.EngineSdk.MuteStdoutWhenLocalSink = previousMute;
            }

            if (previousReader is not null) {
                Program.Direct.Console.SetIn(previousReader);
            }

            currentOperation = string.Empty;
        }

        EmitSequenceEvent(onEvent, "run-all-complete", gameName, new Dictionary<string, object?> {
            ["success"] = overallSuccess,
            ["total"] = selected.Count,
            ["succeeded"] = succeeded
        });

        return new RunAllResult(gameName, overallSuccess, selected.Count, succeeded);
    }

    private static void EmitSequenceEvent(
        Sys.ProcessRunner.EventHandler? sink,
        string evt,
        string game,
        IDictionary<string, object?>? extras = null) {
        if (sink is null) {
            return;
        }

        Dictionary<string, object?> payload = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase) {
            ["event"] = evt,
            ["game"] = game
        };

        if (extras is not null) {
            foreach (KeyValuePair<string, object?> kv in extras) {
                payload[kv.Key] = kv.Value;
            }
        }

        sink(payload);
    }

    private static Dictionary<string, object?> CloneEvent(Dictionary<string, object?> evt) {
        Dictionary<string, object?> clone = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, object?> kv in evt) {
            clone[kv.Key] = kv.Value;
        }

        return clone;
    }

    private static void AddUnique(List<Dictionary<string, object?>> list, Dictionary<string, object?> op) {
        foreach (Dictionary<string, object?> existing in list) {
            if (ReferenceEquals(existing, op)) {
                return;
            }
        }

        list.Add(op);
    }

    private static bool IsFlagSet(Dictionary<string, object?> op, string key) {
        if (!op.TryGetValue(key, out object? value) || value is null) {
            return false;
        }

        if (value is bool b) {
            return b;
        }

        if (value is string s) {
            return bool.TryParse(s, out bool parsed) && parsed;
        }

        try {
            return System.Convert.ToInt32(value) != 0;
        } catch {
            return false;
        }
    }

    private static string ResolveOperationName(Dictionary<string, object?> op) {
        if (op.TryGetValue("Name", out object? nameObj) && nameObj is not null) {
            string name = nameObj.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name)) {
                return name;
            }
        }

        if (op.TryGetValue("script", out object? scriptObj) && scriptObj is not null) {
            string script = scriptObj.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(script)) {
                return System.IO.Path.GetFileName(script);
            }
        }

        return "Operation";
    }

    private static string GetScriptType(Dictionary<string, object?> op) {
        if (op.TryGetValue("script_type", out object? value) && value is not null) {
            return value.ToString()?.ToLowerInvariant() ?? "python";
        }

        return "python";
    }

    private static bool IsEmbeddedScript(string scriptType)
        => scriptType == "engine" || scriptType == "lua" || scriptType == "js";

    private static Dictionary<string, object?> BuildPromptDefaults(Dictionary<string, object?> op) {
        Dictionary<string, object?> answers = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        if (!op.TryGetValue("prompts", out object? promptsObj) || promptsObj is not IList<object?> prompts) {
            return answers;
        }

        foreach (object? promptObj in prompts) {
            if (promptObj is not Dictionary<string, object?> prompt) {
                continue;
            }

            string name = GetString(prompt, "Name");
            if (string.IsNullOrEmpty(name)) {
                continue;
            }

            string type = GetString(prompt, "type").ToLowerInvariant();
            if (prompt.TryGetValue("condition", out object? conditionObj) && conditionObj is string conditionName) {
                if (!answers.TryGetValue(conditionName, out object? conditionValue)) {
                    foreach (object? other in prompts) {
                        if (other is Dictionary<string, object?> otherPrompt &&
                            string.Equals(GetString(otherPrompt, "Name"), conditionName, System.StringComparison.OrdinalIgnoreCase)) {
                            if (!answers.ContainsKey(conditionName) && otherPrompt.TryGetValue("default", out object? condDefault)) {
                                answers[conditionName] = condDefault;
                            }

                            break;
                        }
                    }
                }

                if (!answers.TryGetValue(conditionName, out object? evaluated) || evaluated is not bool condBool || !condBool) {
                    answers[name] = EmptyForPrompt(type);
                    continue;
                }
            }

            if (prompt.TryGetValue("default", out object? defaultValue)) {
                answers[name] = defaultValue;
            } else if (!answers.ContainsKey(name)) {
                answers[name] = EmptyForPrompt(type);
            }
        }

        return answers;
    }

    private static object? EmptyForPrompt(string type) => type switch {
        "confirm" => false,
        "checkbox" => new List<object?>(),
        _ => null
    };

    private static string GetString(IDictionary<string, object?> dict, string key)
        => dict.TryGetValue(key, out object? value) ? value?.ToString() ?? string.Empty : string.Empty;

    private sealed class StdinRedirectReader:System.IO.TextReader {
        private readonly Sys.ProcessRunner.StdinProvider _provider;

        public StdinRedirectReader(Sys.ProcessRunner.StdinProvider provider) => _provider = provider;

        public override string? ReadLine() => _provider();
    }

    /// <summary>
    /// Clones a game module repository into the local registry.
    /// </summary>
    /// <param name="url">Git remote URL.</param>
    /// <returns>True if cloning succeeded.</returns>
    public bool DownloadModule(string url) => _git.CloneModule(url);

}
