using System.Collections.Generic;

namespace EngineNet.Core.Engine;

internal sealed record RunAllResult(string Game, bool Success, int TotalOperations, int SucceededOperations);

public sealed partial class Engine {

    // this is the meathod used to execute the run all ops by both GUI and TUI

    /// <summary>
    /// this meathod runs all operations marked with "run-all" or "run_all" flag, as well as any "init" operations, for the specified game.
    /// </summary>
    /// <param name="gameName"></param>
    /// <param name="onOutput"></param>
    /// <param name="onEvent"></param>
    /// <param name="stdinProvider"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="System.ArgumentException"></exception>
    /// <exception cref="KeyNotFoundException"></exception>
    /// <exception cref="System.IO.FileNotFoundException"></exception>
    /// <exception cref="System.Exception"></exception>
    internal async System.Threading.Tasks.Task<RunAllResult> RunAllAsync(string gameName, Core.ProcessRunner.OutputHandler? onOutput = null, Core.ProcessRunner.EventHandler? onEvent = null, Core.ProcessRunner.StdinProvider? stdinProvider = null, System.Threading.CancellationToken cancellationToken = default) {

        Core.Diagnostics.Log($"[RunAll.cs::RunAllAsync()] Starting RunAllAsync for game '{gameName}', onOutput: {(onOutput is null ? "null" : "set")}, onEvent: {(onEvent is null ? "null" : "set")}, stdinProvider: {(stdinProvider is null ? "null" : "set")}");

        if (string.IsNullOrWhiteSpace(gameName)) {
            throw new System.ArgumentException("Game name is required.", nameof(gameName));
        }

        Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games = Modules(Core.Utils.ModuleFilter.All);
        if (!games.TryGetValue(gameName, out EngineNet.Core.Utils.GameModuleInfo? gameInfo)) {
            throw new KeyNotFoundException($"Game '{gameName}' not found.");
        }

        if (!System.IO.File.Exists(gameInfo.OpsFile)) {
            throw new System.IO.FileNotFoundException($"Operations file for '{gameName}' is missing.", gameInfo.OpsFile);
        }

        List<Dictionary<string, object?>>? allOps = LoadOperationsList(gameInfo.OpsFile);
        if (allOps is null) {
            throw new System.Exception($"Failed to load operations file for '{gameName}'.");
        }
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
            previousReader = System.Console.In;
            System.Console.SetIn(new StdinRedirectReader(stdinProvider));
        }

        System.Action<Dictionary<string, object?>>? previousSink = Core.UI.EngineSdk.LocalEventSink;
        bool previousMute = Core.UI.EngineSdk.MuteStdoutWhenLocalSink;
        string currentOperation = string.Empty;
        Core.Utils.SdkEventScope? sdkScope = null;
        if (onEvent is not null) {
            Core.UI.EngineSdk.LocalEventSink = evt => {
                Dictionary<string, object?> payload = CloneEvent(evt);
                payload["game"] = gameName;
                if (!string.IsNullOrEmpty(currentOperation)) {
                    payload["operation"] = currentOperation;
                }
                onEvent(payload);
            };
            Core.UI.EngineSdk.MuteStdoutWhenLocalSink = true;
            sdkScope = new Core.Utils.SdkEventScope(sink: Core.UI.EngineSdk.LocalEventSink, muteStdout: true, autoPromptResponses: null);
        }

        bool overallSuccess = true;
        int succeeded = 0;

        try {
            // run each selected operation
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
                    string? scriptType = GetScriptType(op);
                    // ensure script type is valid
                    if (Core.Utils.ScriptConstants.IsSupported(scriptType)) {
                        ok = await Engino.RunSingleOperationAsync(gameName, games, op, promptAnswers, EngineConfig, ToolResolver, GitService, GameRegistry, CommandService, OperationExecution, cancellationToken).ConfigureAwait(false);
                    } else if (string.IsNullOrEmpty(scriptType)) {
                        Core.Diagnostics.Log($"[RunAll.cs::RunAllAsync()] Skipping operation '{currentOperation}' due to null or empty script type");
                        overallSuccess = false;
                    } else {
                        Core.Diagnostics.Log($"[RunAll.cs::RunAllAsync()] Skipping operation '{currentOperation}' due to unsupported script type '{scriptType}'");
                        overallSuccess = false;
                    }

                    /*else {
                        // this feature is disabled, for any non builtin script types, execute via lua
                        // External script type
                        List<string> command = BuildCommand(gameName, games, op, promptAnswers);
                        if (command.Count >= 2) {
                            Core.ProcessRunner.EventHandler? proxy = onEvent is null ? null : evt => {
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
                    }*/
                } catch (System.Exception ex) {
                    overallSuccess = false;
                    EmitSequenceEvent(onEvent, evt: "run-all-op-error", gameName, extras: new Dictionary<string, object?> {
                        [key: "name"] = currentOperation,
                        [key: "message"] = ex.Message
                    });
                    Core.Diagnostics.Bug($"[RunAll.cs::RunAllAsync()] err running op '{currentOperation}': {ex.Message}");
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
            if (sdkScope is not null) { sdkScope.Dispose(); }
            if (onEvent is not null) { Core.UI.EngineSdk.LocalEventSink = previousSink; Core.UI.EngineSdk.MuteStdoutWhenLocalSink = previousMute; }

            if (previousReader is not null) {
                System.Console.SetIn(previousReader);
            }

            currentOperation = string.Empty;
            Core.Diagnostics.Trace($"[RunAll.cs::RunAllAsync()] finished running all operations for game '{gameName}'");
        }

        EmitSequenceEvent(onEvent, "run-all-complete", gameName, new Dictionary<string, object?> {
            ["success"] = overallSuccess,
            ["total"] = selected.Count,
            ["succeeded"] = succeeded
        });

        return new RunAllResult(gameName, overallSuccess, selected.Count, succeeded);
    }


    /* :: End of RunAllAsync :: */
    //
    /* :: Helper methods for RunAllAsync :: */


    /// <summary>
    /// Clones an event dictionary.
    /// </summary>
    /// <param name="evt"></param>
    /// <returns></returns>
    private static Dictionary<string, object?> CloneEvent(Dictionary<string, object?> evt) {
        Dictionary<string, object?> clone = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, object?> kv in evt) {
            clone[kv.Key] = kv.Value;
        }

        return clone;
    }

    /// <summary>
    /// Emits a sequence event if the sink is provided.
    /// </summary>
    /// <param name="sink"></param>
    /// <param name="evt"></param>
    /// <param name="game"></param>
    /// <param name="extras"></param>
    private static void EmitSequenceEvent(Core.ProcessRunner.EventHandler? sink, string evt, string game, IDictionary<string, object?>? extras = null) {
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

    /// <summary>
    /// Checks if a flag is set in the operation dictionary.
    /// </summary>
    /// <param name="op"></param>
    /// <param name="key"></param>
    /// <returns></returns>
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

    /// <summary>
    /// Text reader that redirects stdin from a provider.
    /// </summary>
    private sealed class StdinRedirectReader:System.IO.TextReader {
        private readonly Core.ProcessRunner.StdinProvider _provider;
        internal StdinRedirectReader(Core.ProcessRunner.StdinProvider provider) => _provider = provider;
        public override string? ReadLine() => _provider();
    }


    /// <summary>
    /// Adds an operation to the list if it's not already present.
    /// </summary>
    /// <param name="list"></param>
    /// <param name="op"></param>
    private static void AddUnique(List<Dictionary<string, object?>> list, Dictionary<string, object?> op) {
        foreach (Dictionary<string, object?> existing in list) {
            if (ReferenceEquals(existing, op)) {
                return;
            }
        }

        list.Add(op);
    }


    /// <summary>
    /// Builds default answers for prompts defined in the operation.
    /// </summary>
    /// <param name="op"></param>
    /// <returns></returns>
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
    /// <summary>
    /// Gets a string value from a dictionary by key.
    /// </summary>
    /// <param name="dict"></param>
    /// <param name="key"></param>
    /// <returns></returns>
    private static string GetString(Dictionary<string, object?> dict, string key) {
        return dict.TryGetValue(key, out object? value) ? value?.ToString() ?? string.Empty : string.Empty;
    }



    /// <summary>
    /// Provides an empty value for a prompt based on its type.
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    private static object? EmptyForPrompt(string type) => type switch {
        "confirm" => false,
        "checkbox" => new List<object?>(),
        _ => null
    };


    /// <summary>
    /// Resolves the operation name for event reporting.
    /// </summary>
    /// <param name="op"></param>
    /// <returns></returns>
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

    /// <summary>
    /// Emits a sequence event if the sink is provided.
    /// </summary>
    /// <param name="op"></param>
    /// <returns></returns>
    private static string? GetScriptType(Dictionary<string, object?> op) {
        if (op.TryGetValue("script_type", out object? value) && value is not null) {
            return value.ToString()?.ToLowerInvariant();
        }

        return null;
    }
}
