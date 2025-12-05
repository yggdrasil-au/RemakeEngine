using System.Collections.Generic;

namespace EngineNet.Core;

internal sealed record RunAllResult(string Game, bool Success, int TotalOperations, int SucceededOperations);

internal sealed partial class Engine {
    internal async System.Threading.Tasks.Task<RunAllResult> RunAllAsync(string gameName, Core.ProcessRunner.OutputHandler? onOutput = null, Core.ProcessRunner.EventHandler? onEvent = null, Core.ProcessRunner.StdinProvider? stdinProvider = null, System.Threading.CancellationToken cancellationToken = default) {

        Core.Diagnostics.Log($"[ENGINE.cs] Starting RunAllAsync for game '{gameName}', onOutput: {(onOutput is null ? "null" : "set")}, onEvent: {(onEvent is null ? "null" : "set")}, stdinProvider: {(stdinProvider is null ? "null" : "set")}");

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

        System.Action<Dictionary<string, object?>>? previousSink = Core.Utils.EngineSdk.LocalEventSink;
        bool previousMute = Core.Utils.EngineSdk.MuteStdoutWhenLocalSink;
        string currentOperation = string.Empty;
        Core.Utils.SdkEventScope? sdkScope = null;
        if (onEvent is not null) {
            Core.Utils.EngineSdk.LocalEventSink = evt => {
                Dictionary<string, object?> payload = CloneEvent(evt);
                payload["game"] = gameName;
                if (!string.IsNullOrEmpty(currentOperation)) {
                    payload["operation"] = currentOperation;
                }
                onEvent(payload);
            };
            Core.Utils.EngineSdk.MuteStdoutWhenLocalSink = true;
            sdkScope = new Core.Utils.SdkEventScope(sink: Core.Utils.EngineSdk.LocalEventSink, muteStdout: true, autoPromptResponses: null);
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
                    }
                } catch (System.Exception ex) {
                    overallSuccess = false;
                    EmitSequenceEvent(onEvent, evt: "run-all-op-error", gameName, extras: new Dictionary<string, object?> {
                        [key: "name"] = currentOperation,
                        [key: "message"] = ex.Message
                    });
                    Core.Diagnostics.Bug($"[Engine.cs] err running op '{currentOperation}': {ex.Message}");
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
            if (onEvent is not null) { Core.Utils.EngineSdk.LocalEventSink = previousSink; Core.Utils.EngineSdk.MuteStdoutWhenLocalSink = previousMute; }

            if (previousReader is not null) {
                System.Console.SetIn(previousReader);
            }

            currentOperation = string.Empty;
            Core.Diagnostics.Trace($"[Engine.cs] finished running all operations for game '{gameName}'");
        }

        EmitSequenceEvent(onEvent, "run-all-complete", gameName, new Dictionary<string, object?> {
            ["success"] = overallSuccess,
            ["total"] = selected.Count,
            ["succeeded"] = succeeded
        });

        return new RunAllResult(gameName, overallSuccess, selected.Count, succeeded);
    }

}
