
namespace EngineNet.Core.Operations;

public sealed class Single {

    private readonly Core.Abstractions.IScriptActionDispatcher _scriptActionDispatcher;

    internal Single(Core.Abstractions.IScriptActionDispatcher scriptActionDispatcher) {
        this._scriptActionDispatcher = scriptActionDispatcher;
    }

    // used to run a single op by both runallasync and direct operation execution in the GUI/TUI

    /// <summary>
    /// Runs a single operation, which may be any supported script type.
    /// </summary>
    /// <param name="currentGame"></param>
    /// <param name="games"></param>
    /// <param name="op"></param>
    /// <param name="promptAnswers"></param>
    /// <param name="Context"></param>
    /// <param name="OperationContext"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    internal async System.Threading.Tasks.Task<bool> RunAsync(
        string currentGame,
        Core.Data.GameModules games,
        IDictionary<string, object?> op,
        Core.Data.PromptAnswers promptAnswers,
        Engine.EngineContext Context,
        Core.Engine.OperationContext OperationContext,
        System.Threading.CancellationToken cancellationToken = default(CancellationToken)
    ) {
        // Keep the incoming operation metadata raw so nested on-success operations are
        // resolved only when each child actually executes.
        IDictionary<string, object?> rawOperation = op;

        // 1. Build the execution Context once
        Dictionary<string, object?> ctx = Core.Utils.ExecutionContextBuilder.Build(currentGame, games, Context.EngineConfig.Data);

        // 2. Resolve placeholders for the current operation execution payload.
        //    Nested onsuccess blocks are intentionally excluded from this resolution pass.
        IDictionary<string, object?> executableOperation = Single.ResolveExecutionPayload(rawOperation, ctx);

        string? scriptType = (executableOperation.TryGetValue("script_type", out object? st) ? st?.ToString() : null)?.ToLowerInvariant();
        List<string> parts = Context.CommandService.BuildCommand(currentGame, games, Context.EngineConfig.Data, executableOperation, promptAnswers);
        if (parts.Count < 2) {
            return false;
        }

        string scriptPath = parts[1];
        string[] args = parts.Skip(2).ToArray();

        if (scriptType is null) {
            Shared.IO.Diagnostics.Log($"[RunSingleAsync.cs::RunSingleOperationAsync()] Missing script_type for operation with script '{scriptPath}'");
            return false;
        }

        bool result = false;
        try {
            switch (scriptType) {
                // for running built-in engine operations (like download-tools) or internal operations (like download_module_git)
                case var _ when Utils.ScriptConstants.IsBuiltIn(scriptType): {
                    try {
                        string? action = executableOperation.TryGetValue("script", out object? s) ? s?.ToString() : null;
                        string? title = executableOperation.TryGetValue("Name", out object? n) ? n?.ToString() ?? action : action;
                        Shared.IO.Diagnostics.Log($"[RunSingleAsync.cs::RunSingleOperationAsync()] Executing engine operation {title} ({action})");
                        IO.writeLine($"\n>>> Engine operation: {title}");
                        // delegate engine type handling to ExecuteEngineOperationAsync
                        //var op_dispatcher = new helpers.OpDispatcher();
                        result = await helpers.OpDispatcher.DispatchAsync(executableOperation, promptAnswers, currentGame, games, Context, cancellationToken);
                    } catch (System.Exception ex) {
                        Shared.IO.Diagnostics.Bug($"[Single.cs::RunAsync()] Engine operation catch triggered: {ex}");
                        IO.writeLine($"engine ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                // for running external script types (like bms) via the external action dispatcher for calling third party tools like QuickBMS
                case var _ when Utils.ScriptConstants.IsExternal(scriptType): {
                    try {
                        string inputDir = executableOperation.TryGetValue("input", out object? in0) ? in0?.ToString() ?? string.Empty : string.Empty;
                        string outputDir = executableOperation.TryGetValue("output", out object? out0) ? out0?.ToString() ?? string.Empty : string.Empty;
                        string? extension = executableOperation.TryGetValue("extension", out object? ext0) ? ext0?.ToString() : null;

                        if (!games.TryGetValue(currentGame, out Core.Data.GameModuleInfo? gameInfo)) {
                            throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                        }

                        var action = this._scriptActionDispatcher.TryCreateExternal(
                            scriptType: scriptType,
                            scriptPath: scriptPath,
                            gameRoot: gameInfo.GameRoot,
                            inputDir: inputDir,
                            outputDir: outputDir,
                            extension: extension,
                            projectRoot: Shared.State.RootPath
                        );

                        if (action is null) {
                            result = false;
                            Shared.IO.Diagnostics.Log($"[RunSingleAsync.cs::RunSingleOperationAsync()] Unsupported external script type '{scriptType}'");
                            break;
                        }

                        await action.ExecuteAsync(Context.ToolResolver, Context.CommandService, cancellationToken);
                        result = true;
                    } catch (System.Exception ex) {
                        Shared.IO.Diagnostics.Bug($"[Single.cs::RunAsync()] External action catch triggered: {ex}");
                        IO.writeLine($"bms engine ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                // for running embedded script types (lua, js, python)
                case var _ when Core.Utils.ScriptConstants.IsEmbedded(scriptType): {
                    try {
                        // create the action with the dispatcher
                        IEnumerable<string> argsEnum = args;
                        Core.Abstractions.IScriptAction? act = this._scriptActionDispatcher.TryCreateEmbedded(
                            scriptType: scriptType,
                            scriptPath: scriptPath,
                            args: argsEnum,
                            currentGame: currentGame,
                            games: games,
                            projectRoot: Shared.State.RootPath
                        );
                        // null act means unsupported script type
                        if (act is null) {
                            result = false;
                            Shared.IO.Diagnostics.Log($"[RunSingleAsync.cs::RunSingleOperationAsync()] Unsupported embedded script type '{scriptType}'");
                            break;
                        }
                        // execute the action
                        await act.ExecuteAsync(Context.ToolResolver, Context.CommandService, cancellationToken);
                        result = true;
                    } catch (System.Exception ex) {
                        Shared.IO.Diagnostics.Bug($"[Single.cs::RunAsync()] Embedded action catch triggered for '{scriptType}': {ex}");
                        IO.writeLine($"{scriptType} engine ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                default: {
                    // not supported
                    IO.Warn($"operation ERROR: Unsupported script type '{scriptType}'");
                    Shared.IO.Diagnostics.Log($"[RunSingleAsync.cs::RunSingleOperationAsync()] Unsupported script type '{scriptType}'");
                    break;
                }
            }
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[RunSingleAsync.cs::RunSingleOperationAsync()] err running single op: {ex.Message}");
            IO.writeLine($"operation ERROR: {ex.Message}");
            result = false;
        }

        // If the main operation succeeded, run any nested [[operation.onsuccess]] steps
        if (result && helpers.OpMetadataExtractor.ExtractSuccessActions(rawOperation, out List<Dictionary<string, object?>>? followUps) && followUps is not null) {
            // if we have follow-up operations, run them sequentially, propagating cancellation and failure but not short-circuiting on failure of any individual follow-up so all follow-ups get a chance to run regardless of any individual failures
            foreach (Dictionary<string, object?> childOp in followUps) {
                if (cancellationToken.IsCancellationRequested) break;
                bool ok = await OperationContext.Single.RunAsync( currentGame, games, op: childOp, promptAnswers, Context, OperationContext, cancellationToken);
                if (!ok) {
                    result = false; // propagate failure from any onsuccess step
                }
            }
        }
        // log in GameRoot an execution log for each operation execution, with timestamp, operation name, script type, script id (if any), and result (success/failure)
        try {
            string logEntry = $"{(executableOperation.TryGetValue("Id", out object? id) ? id?.ToString() : "No ID")} | {System.DateTime.UtcNow:o} | {(result ? "SUCCESS" : "FAILURE")} | {(executableOperation.TryGetValue("Name", out object? n) ? n?.ToString() : "Unnamed Operation")} | {scriptType} | {scriptPath}";
            if (games.TryGetValue(currentGame, out Core.Data.GameModuleInfo? gameInfo)) {
                string logPath = System.IO.Path.Combine(gameInfo.GameRoot, "operation_execution.log");
                await System.IO.File.AppendAllLinesAsync(logPath, new[] { logEntry }, cancellationToken);
            }
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[RunSingleAsync.cs::RunSingleOperationAsync()] Failed to write execution log: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Resolves the execution payload for the current operation by resolving placeholders in all top level fields except for nested onsuccess blocks,
    /// which are resolved at execution time of each child operation.
    /// </summary>
    /// <param name="rawOperation"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    private static IDictionary<string, object?> ResolveExecutionPayload(
        IDictionary<string, object?> rawOperation,
        IDictionary<string, object?> context
    ) {
        Dictionary<string, object?> executionPayload = new Dictionary<string, object?>(rawOperation, System.StringComparer.OrdinalIgnoreCase);
        executionPayload.Remove("onsuccess");
        executionPayload.Remove("on_success");

        // tmp debug log each in diagnostics trace before resolution
        foreach (var kvp in executionPayload) {
            Shared.IO.Diagnostics.Trace($"[Single.cs::ResolveExecutionPayload()] Pre-resolution payload field: {kvp.Key} = {kvp.Value}");
            // if value is args array, log each arg separately
            if (kvp.Key.Equals("args", System.StringComparison.OrdinalIgnoreCase) && kvp.Value is IEnumerable<object?> argsEnum) {
                int i = 0;
                foreach (object? arg in argsEnum) {
                    Shared.IO.Diagnostics.Trace($"[Single.cs::ResolveExecutionPayload()] Pre-resolution arg[{i}]: {arg}");
                    i++;
                }
            }
        }

        if (Core.Utils.Placeholders.Resolve(executionPayload, context) is IDictionary<string, object?> resolvedPayload) {
            Shared.IO.Diagnostics.Trace("[Single.cs::ResolveExecutionPayload()] Successfully resolved execution payload");

            // tmp debug log each resolved field in diagnostics trace
            foreach (var kvp in resolvedPayload) {
                Shared.IO.Diagnostics.Trace($"[Single.cs::ResolveExecutionPayload()] Resolved payload field: {kvp.Key} = {kvp.Value}");
                // if value is args array, log each arg separately
                if (kvp.Key.Equals("args", System.StringComparison.OrdinalIgnoreCase) && kvp.Value is IEnumerable<object?> argsEnum) {
                    int i = 0;
                    foreach (object? arg in argsEnum) {
                        Shared.IO.Diagnostics.Trace($"[Single.cs::ResolveExecutionPayload()] Resolved arg[{i}]: {arg}");
                        i++;
                    }
                }
            }

            return resolvedPayload;
        }

        Shared.IO.Diagnostics.Trace("[Single.cs::ResolveExecutionPayload()] Failed to resolve execution payload, using raw payload");

        return executionPayload;
    }

}
