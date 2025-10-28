
using EngineNet.Core.ScriptEngines.Helpers;
using System.Threading;

namespace EngineNet.Core;

public sealed record RunAllResult(String Game, Boolean Success, Int32 TotalOperations, Int32 SucceededOperations);


public sealed partial class OperationsEngine {
    private readonly String _rootPath;

    private readonly Tools.IToolResolver _tools;
    private readonly EngineConfig _engineConfig;
    private readonly Sys.Registries _registries;
    private readonly Sys.CommandBuilder _builder;
    private readonly Sys.GitTools _git;

    public OperationsEngine(String rootPath, Tools.IToolResolver tools, EngineConfig engineConfig) {
        _rootPath = rootPath;
        _tools = tools;
        _engineConfig = engineConfig;
        _registries = new Sys.Registries(rootPath);
        _builder = new Sys.CommandBuilder(rootPath);
        _git = new Sys.GitTools(Path.Combine(rootPath, "RemakeRegistry", "Games"));
    }

    // getters for primary values and objects

    public String GetRootPath() => _rootPath;
    public Tools.IToolResolver GetToolResolver() => _tools;
    public EngineConfig GetEngineConfig() => _engineConfig;
    public Sys.Registries GetRegistries() => _registries;
    public Sys.CommandBuilder GetCommandBuilder() => _builder;
    public Sys.GitTools GetGitTools() => _git;

    public async Task<RunAllResult> RunAllAsync(
        String gameName,
        Sys.ProcessRunner.OutputHandler? onOutput = null,
        Sys.ProcessRunner.EventHandler? onEvent = null,
        Sys.ProcessRunner.StdinProvider? stdinProvider = null,
        CancellationToken cancellationToken = default) {

        #if DEBUG
        Console.WriteLine($"[ENGINE.cs] Starting RunAllAsync for game '{gameName}', onOutput: {(onOutput is null ? "null" : "set")}, onEvent: {(onEvent is null ? "null" : "set")}, stdinProvider: {(stdinProvider is null ? "null" : "set")}");
        #endif

        if (String.IsNullOrWhiteSpace(gameName)) {
            throw new ArgumentException("Game name is required.", nameof(gameName));
        }

        Dictionary<String, Object?> games = ListGames();
        if (!games.TryGetValue(gameName, out Object? infoObj) || infoObj is not IDictionary<String, Object?> gameInfo) {
            throw new KeyNotFoundException($"Game '{gameName}' not found.");
        }

        if (!gameInfo.TryGetValue("ops_file", out Object? opsObj) || opsObj is not String opsFile || !File.Exists(opsFile)) {
            throw new FileNotFoundException($"Operations file for '{gameName}' is missing.", opsObj?.ToString());
        }

        List<Dictionary<String, Object?>> allOps = LoadOperationsList(opsFile);
        List<Dictionary<String, Object?>> selected = new List<Dictionary<String, Object?>>();
        foreach (Dictionary<String, Object?> op in allOps) {
            if (IsFlagSet(op, "init")) {
                AddUnique(selected, op);
            }
        }

        foreach (Dictionary<String, Object?> op in allOps) {
            if (IsFlagSet(op, "run-all") || IsFlagSet(op, "run_all")) {
                AddUnique(selected, op);
            }
        }

        if (selected.Count == 0) {
            selected.AddRange(allOps);
        }

        EmitSequenceEvent(onEvent, "run-all-start", gameName, new Dictionary<String, Object?> {
            ["total"] = selected.Count
        });

        TextReader? previousReader = null;
        if (stdinProvider is not null) {
            previousReader = Console.In;
            Console.SetIn(new StdinRedirectReader(stdinProvider));
        }

        Action<Dictionary<String, Object?>>? previousSink = EngineSdk.LocalEventSink;
        Boolean previousMute = EngineSdk.MuteStdoutWhenLocalSink;
        String currentOperation = String.Empty;
        if (onEvent is not null) {
            EngineSdk.LocalEventSink = evt => {
                Dictionary<String, Object?> payload = CloneEvent(evt);
                payload["game"] = gameName;
                if (!String.IsNullOrEmpty(currentOperation)) {
                    payload["operation"] = currentOperation;
                }

                onEvent(payload);
            };
            EngineSdk.MuteStdoutWhenLocalSink = true;
        }

        Boolean overallSuccess = true;
        Int32 succeeded = 0;

        try {
            for (Int32 index = 0; index < selected.Count; index++) {
                if (cancellationToken.IsCancellationRequested) {
                    overallSuccess = false;
                    break;
                }

                Dictionary<String, Object?> op = selected[index];
                currentOperation = ResolveOperationName(op);
                EmitSequenceEvent(onEvent, "run-all-op-start", gameName, new Dictionary<String, Object?> {
                    ["index"] = index,
                    ["total"] = selected.Count,
                    ["name"] = currentOperation
                });

                Dictionary<String, Object?> promptAnswers = BuildPromptDefaults(op);
                Boolean ok = false;
                try {
                    String scriptType = GetScriptType(op);
                    if (IsEmbeddedScript(scriptType)) {
                        ok = await RunSingleOperationAsync(gameName, games, op, promptAnswers, cancellationToken).ConfigureAwait(false);
                    } else {
                        List<String> command = BuildCommand(gameName, games, op, promptAnswers);
                        if (command.Count >= 2) {
                            Sys.ProcessRunner.EventHandler? proxy = onEvent is null ? null : evt => {
                                Dictionary<String, Object?> payload = CloneEvent(evt);
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
                } catch (Exception ex) {
                    overallSuccess = false;
                    EmitSequenceEvent(onEvent, "run-all-op-error", gameName, new Dictionary<String, Object?> {
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

                EmitSequenceEvent(onEvent, "run-all-op-end", gameName, new Dictionary<String, Object?> {
                    ["index"] = index,
                    ["total"] = selected.Count,
                    ["name"] = currentOperation,
                    ["success"] = ok
                });
            }
        } finally {
            if (onEvent is not null) {
                EngineSdk.LocalEventSink = previousSink;
                EngineSdk.MuteStdoutWhenLocalSink = previousMute;
            }

            if (previousReader is not null) {
                Console.SetIn(previousReader);
            }

            currentOperation = String.Empty;
        }

        EmitSequenceEvent(onEvent, "run-all-complete", gameName, new Dictionary<String, Object?> {
            ["success"] = overallSuccess,
            ["total"] = selected.Count,
            ["succeeded"] = succeeded
        });

        return new RunAllResult(gameName, overallSuccess, selected.Count, succeeded);
    }

    private static void EmitSequenceEvent(
        Sys.ProcessRunner.EventHandler? sink,
        String evt,
        String game,
        IDictionary<String, Object?>? extras = null) {
        if (sink is null) {
            return;
        }

        Dictionary<String, Object?> payload = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase) {
            ["event"] = evt,
            ["game"] = game
        };

        if (extras is not null) {
            foreach (KeyValuePair<String, Object?> kv in extras) {
                payload[kv.Key] = kv.Value;
            }
        }

        sink(payload);
    }

    private static Dictionary<String, Object?> CloneEvent(Dictionary<String, Object?> evt) {
        Dictionary<String, Object?> clone = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<String, Object?> kv in evt) {
            clone[kv.Key] = kv.Value;
        }

        return clone;
    }

    private static void AddUnique(List<Dictionary<String, Object?>> list, Dictionary<String, Object?> op) {
        foreach (Dictionary<String, Object?> existing in list) {
            if (ReferenceEquals(existing, op)) {
                return;
            }
        }

        list.Add(op);
    }

    private static Boolean IsFlagSet(Dictionary<String, Object?> op, String key) {
        if (!op.TryGetValue(key, out Object? value) || value is null) {
            return false;
        }

        if (value is Boolean b) {
            return b;
        }

        if (value is String s) {
            return Boolean.TryParse(s, out Boolean parsed) && parsed;
        }

        try {
            return Convert.ToInt32(value) != 0;
        } catch {
            return false;
        }
    }

    private static String ResolveOperationName(Dictionary<String, Object?> op) {
        if (op.TryGetValue("Name", out Object? nameObj) && nameObj is not null) {
            String name = nameObj.ToString() ?? String.Empty;
            if (!String.IsNullOrWhiteSpace(name)) {
                return name;
            }
        }

        if (op.TryGetValue("script", out Object? scriptObj) && scriptObj is not null) {
            String script = scriptObj.ToString() ?? String.Empty;
            if (!String.IsNullOrWhiteSpace(script)) {
                return Path.GetFileName(script);
            }
        }

        return "Operation";
    }

    private static String GetScriptType(Dictionary<String, Object?> op) {
        if (op.TryGetValue("script_type", out Object? value) && value is not null) {
            return value.ToString()?.ToLowerInvariant() ?? "python";
        }

        return "python";
    }

    private static Boolean IsEmbeddedScript(String scriptType)
        => scriptType == "engine" || scriptType == "lua" || scriptType == "js";

    private static Dictionary<String, Object?> BuildPromptDefaults(Dictionary<String, Object?> op) {
        Dictionary<String, Object?> answers = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
        if (!op.TryGetValue("prompts", out Object? promptsObj) || promptsObj is not IList<Object?> prompts) {
            return answers;
        }

        foreach (Object? promptObj in prompts) {
            if (promptObj is not Dictionary<String, Object?> prompt) {
                continue;
            }

            String name = GetString(prompt, "Name");
            if (String.IsNullOrEmpty(name)) {
                continue;
            }

            String type = GetString(prompt, "type").ToLowerInvariant();
            if (prompt.TryGetValue("condition", out Object? conditionObj) && conditionObj is String conditionName) {
                if (!answers.TryGetValue(conditionName, out Object? conditionValue)) {
                    foreach (Object? other in prompts) {
                        if (other is Dictionary<String, Object?> otherPrompt &&
                            String.Equals(GetString(otherPrompt, "Name"), conditionName, StringComparison.OrdinalIgnoreCase)) {
                            if (!answers.ContainsKey(conditionName) && otherPrompt.TryGetValue("default", out Object? condDefault)) {
                                answers[conditionName] = condDefault;
                            }

                            break;
                        }
                    }
                }

                if (!answers.TryGetValue(conditionName, out Object? evaluated) || evaluated is not Boolean condBool || !condBool) {
                    answers[name] = EmptyForPrompt(type);
                    continue;
                }
            }

            if (prompt.TryGetValue("default", out Object? defaultValue)) {
                answers[name] = defaultValue;
            } else if (!answers.ContainsKey(name)) {
                answers[name] = EmptyForPrompt(type);
            }
        }

        return answers;
    }

    private static Object? EmptyForPrompt(String type) => type switch {
        "confirm" => false,
        "checkbox" => new List<Object?>(),
        _ => null
    };

    private static String GetString(IDictionary<String, Object?> dict, String key)
        => dict.TryGetValue(key, out Object? value) ? value?.ToString() ?? String.Empty : String.Empty;

    private sealed class StdinRedirectReader:TextReader {
        private readonly Sys.ProcessRunner.StdinProvider _provider;

        public StdinRedirectReader(Sys.ProcessRunner.StdinProvider provider) => _provider = provider;

        public override String? ReadLine() => _provider();
    }
}
