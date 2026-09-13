
using EngineNet.Shared.IO.UI;

namespace EngineNet.Core.Operations;

public sealed record RunAllResult(string Game, bool Success, int TotalOperations, int SucceededOperations);

public sealed class All {

    // this is the meathod used to execute the run all ops by both GUI and TUI

    /// <summary>
    /// this meathod runs all operations marked with "run-all" or "run_all" flag, as well as any "init" operations, for the specified game.
    /// </summary>
    /// <param name="gameName"></param>
    /// <param name="Context"></param>
    /// <param name="OperationContext"></param>
    /// <param name="onOutput"></param>
    /// <param name="onEvent"></param>
    /// <param name="stdinProvider"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="System.ArgumentException"></exception>
    /// <exception cref="KeyNotFoundException"></exception>
    /// <exception cref="System.IO.FileNotFoundException"></exception>
    /// <exception cref="System.Exception"></exception>
    public static async System.Threading.Tasks.Task<RunAllResult> RunAsync(
        string gameName,
        Core.Engine.EngineContext Context,
        Core.Engine.OperationContext OperationContext,
        Core.ProcessRunner.OutputHandler? onOutput = null,
        Core.ProcessRunner.EventHandler? onEvent = null,
        Core.ProcessRunner.StdinProvider? stdinProvider = null,
        System.Threading.CancellationToken cancellationToken = default(CancellationToken)
    ) {

        Shared.IO.Diagnostics.Log($"[RunAll.cs::RunAllAsync()] Starting RunAllAsync for game '{gameName}', onOutput: {(onOutput is null ? "null" : "set")}, onEvent: {(onEvent is null ? "null" : "set")}, stdinProvider: {(stdinProvider is null ? "null" : "set")}");

        if (string.IsNullOrWhiteSpace(gameName)) {
            throw new System.ArgumentException("Game name is required.", paramName: nameof(gameName));
        }

        Core.Data.GameModules games = Context.GameRegistry.GetModules(filter: Core.Data.ModuleFilter.All);
        if (!games.TryGetValue(key: gameName, out EngineNet.Core.Data.GameModuleInfo? gameInfo)) {
            throw new KeyNotFoundException($"Game '{gameName}' not found.");
        }

        Core.Data.ModuleOperationSession session = OperationContext.OperationsService.LoadModuleSession(gameName: gameName, games: games, engineConfig: Context.EngineConfig.Data);
        if (!session.PreparedOperations.IsLoaded) {
            throw new System.InvalidOperationException(session.PreparedOperations.ErrorMessage ?? $"Failed to load operations file for '{gameName}'.");
        }

        List<Dictionary<string, object?>> allOps = session.PreparedOperations.InitOperations
            .Concat(second: session.PreparedOperations.RegularOperations)
            .Select(selector: operation => operation.Operation)
            .ToList();

        // --- NEW DEPENDENCY GRAPH LOGIC ---
        // Build the graph and print it to the trace log for debugging.
        // It does not alter 'allOps' or affect the standard linear execution.
        var dependencyGraph = new helpers.OpDependencyGraph(operations: allOps);
        dependencyGraph.PrintGraphToTrace();

        if (!dependencyGraph.IsValid) {
            Shared.IO.Diagnostics.Log($"[RunAll.cs::RunAllAsync()] Warning: Dependency graph is invalid. See trace.log for details.");
        }
        // ----------------------------------

        List<Dictionary<string, object?>> selected = new List<Dictionary<string, object?>>();
        foreach (Core.Data.PreparedOperation operation in session.PreparedOperations.InitOperations) {
            AddUnique(list: selected, op: operation.Operation);
        }

        foreach (Core.Data.PreparedOperation operation in session.PreparedOperations.RunAllOperations) {
            AddUnique(list: selected, op: operation.Operation);
        }

        if (selected.Count == 0) {
            selected.AddRange(collection: allOps);
        }

        EmitSequenceEvent(sink: onEvent, evt: EngineSdk.Events.RunAllStart, game: gameName, extras: new Dictionary<string, object?> {
            [key: "total"] = selected.Count
        });

        System.IO.TextReader? previousReader = null;
        if (stdinProvider is not null) {
            previousReader = System.Console.In;
            System.Console.SetIn(newIn: new StdinRedirectReader(provider: stdinProvider));
        }

        OperationState currentOperation = new OperationState();
        using Shared.IO.UI.SdkEventScope? sdkScope = onEvent is not null
            ? new Shared.IO.UI.SdkEventScope(
                sink: evt => {
                    Dictionary<string, object?> payload = CloneEvent(evt: evt);
                    payload[key: "game"] = gameName;
                    if (!string.IsNullOrEmpty(currentOperation.Value)) {
                        payload[key: "operation"] = currentOperation.Value;
                    }
                    onEvent(evt: payload);
                },
                muteStdout: true,
                autoPromptResponses: null)
            : null;

        bool overallSuccess = true;
        int succeeded = 0;

        try {
            // run each selected operation
            for (int index = 0; index < selected.Count; index++) {
                if (cancellationToken.IsCancellationRequested) {
                    overallSuccess = false;
                    break;
                }

                Dictionary<string, object?> op = selected[index: index];
                currentOperation.Value = ResolveOperationName(op: op);
                EmitSequenceEvent(sink: onEvent, evt: EngineSdk.Events.RunAllOpStart, game: gameName, extras: new Dictionary<string, object?> {
                    [key: "index"] = index,
                    [key: "total"] = selected.Count,
                    [key: "name"] = currentOperation.Value
                });

                Core.Data.PromptAnswers promptAnswers = BuildPromptDefaults(op: op);
                bool ok = false;
                try {
                    string? scriptType = GetScriptType(op: op);
                    // ensure script type is valid
                    if (Core.Utils.ScriptConstants.IsSupported(script_type: scriptType)) {
                        ok = await OperationContext.Single.RunAsync(currentGame: gameName, games: games, op: op, promptAnswers: promptAnswers, Context: Context,OperationContext: OperationContext, cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                    } else if (string.IsNullOrEmpty(scriptType)) {
                        Shared.IO.Diagnostics.Log($"[RunAll.cs::RunAllAsync()] Skipping operation '{currentOperation.Value}' due to null or empty script type");
                        overallSuccess = false;
                    } else {
                        Shared.IO.Diagnostics.Log($"[RunAll.cs::RunAllAsync()] Skipping operation '{currentOperation.Value}' due to unsupported script type '{scriptType}'");
                        overallSuccess = false;
                    }
                } catch (System.Exception ex) {
                    overallSuccess = false;
                    EmitSequenceEvent(sink: onEvent, evt: EngineSdk.Events.RunAllOpError, game: gameName, extras: new Dictionary<string, object?> {
                        [key: "name"] = currentOperation.Value,
                        [key: "message"] = ex.Message
                    });
                    Shared.IO.Diagnostics.Bug($"[RunAll.cs::RunAllAsync()] err running op '{currentOperation.Value}': {ex.Message}");
                }

                overallSuccess &= ok;
                if (ok) {
                    succeeded++;
                }

                EmitSequenceEvent(sink: onEvent, evt: EngineSdk.Events.RunAllOpEnd, game: gameName, extras: new Dictionary<string, object?> {
                    [key: "index"] = index,
                    [key: "total"] = selected.Count,
                    [key: "name"] = currentOperation.Value,
                    [key: "success"] = ok
                });
            }
        } finally {
            if (previousReader is not null) {
                System.Console.SetIn(newIn: previousReader);
            }

            currentOperation.Value = string.Empty;
            Shared.IO.Diagnostics.Trace($"[RunAll.cs::RunAllAsync()] finished running all operations for game '{gameName}'");
        }

        EmitSequenceEvent(sink: onEvent, evt: EngineSdk.Events.RunAllComplete, game: gameName, extras: new Dictionary<string, object?> {
            [key: "success"] = overallSuccess,
            [key: "total"] = selected.Count,
            [key: "succeeded"] = succeeded
        });

        return new RunAllResult(Game: gameName, Success: overallSuccess, TotalOperations: selected.Count, SucceededOperations: succeeded);
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
        Dictionary<string, object?> clone = new Dictionary<string, object?>(comparer: System.StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, object?> kv in evt) {
            clone[key: kv.Key] = kv.Value;
        }

        return clone;
    }

    /// <summary>
    /// Holds the current operation name for event callbacks without reassigning the captured local.
    /// </summary>
    private sealed class OperationState {
        internal string Value { get; set; } = string.Empty;
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

        Dictionary<string, object?> payload = new Dictionary<string, object?>(comparer: System.StringComparer.OrdinalIgnoreCase) {
            [key: "event"] = evt,
            [key: "game"] = game
        };

        if (extras is not null) {
            foreach (KeyValuePair<string, object?> kv in extras) {
                payload[key: kv.Key] = kv.Value;
            }
        }

        sink(evt: payload);
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
            if (ReferenceEquals(objA: existing, objB: op)) {
                return;
            }
        }

        list.Add(item: op);
    }

    /// <summary>
    /// Builds default answers for prompts defined in the operation.
    /// </summary>
    /// <param name="op"></param>
    /// <returns></returns>
    private static Core.Data.PromptAnswers BuildPromptDefaults(Dictionary<string, object?> op) {
        Core.Data.PromptAnswers answers = new Core.Data.PromptAnswers();
        if (!op.TryGetValue(key: "prompts", out object? promptsObj) || promptsObj is not IList<object?> prompts) {
            return answers;
        }

        foreach (object? promptObj in prompts) {
            if (promptObj is not Dictionary<string, object?> prompt) {
                continue;
            }

            string name = GetString(dict: prompt, key: "Name");
            if (string.IsNullOrEmpty(name)) {
                continue;
            }

            string type = GetString(dict: prompt, key: "type").ToLowerInvariant();
            if (prompt.TryGetValue(key: "condition", out object? conditionObj) && conditionObj is string conditionName) {
                if (!answers.TryGetValue(key: conditionName, out object? _)) {
                    foreach (object? other in prompts) {
                        if (other is Dictionary<string, object?> otherPrompt &&
                            string.Equals(a: GetString(dict: otherPrompt, key: "Name"), b: conditionName, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                            if (!answers.ContainsKey(key: conditionName) && otherPrompt.TryGetValue(key: "default", out object? condDefault)) {
                                answers[key: conditionName] = condDefault;
                            }

                            break;
                        }
                    }
                }

                if (!answers.TryGetValue(key: conditionName, out object? evaluated) || evaluated is not bool condBool || !condBool) {
                    answers[key: name] = EmptyForPrompt(type: type);
                    continue;
                }
            }

            if (prompt.TryGetValue(key: "default", out object? defaultValue)) {
                answers[key: name] = defaultValue;
            } else if (!answers.ContainsKey(key: name)) {
                answers[key: name] = EmptyForPrompt(type: type);
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
        return dict.TryGetValue(key: key, out object? value) ? value?.ToString() ?? string.Empty : string.Empty;
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
        if (op.TryGetValue(key: "Name", out object? nameObj) && nameObj is not null) {
            string name = nameObj.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name)) {
                return name;
            }
        }

        if (op.TryGetValue(key: "script", out object? scriptObj) && scriptObj is not null) {
            string script = scriptObj.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(script)) {
                return System.IO.Path.GetFileName(path: script);
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
        if (op.TryGetValue(key: "script_type", out object? value) && value is not null) {
            return value.ToString()?.ToLowerInvariant();
        }

        return null;
    }
}
