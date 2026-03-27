
namespace EngineNet.Core.Engine.Operations;

internal sealed class Single {

    // used to run a single op by both runallasync and direct operation execution in the GUI/TUI

    /// <summary>
    /// Runs a single operation, which may be any supported script type.
    /// </summary>
    /// <param name="currentGame"></param>
    /// <param name="games"></param>
    /// <param name="op"></param>
    /// <param name="promptAnswers"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    internal async System.Threading.Tasks.Task<bool> RunAsync(
        string currentGame,
        Dictionary<string, EngineNet.Core.Data.GameModuleInfo> games,
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers,
        EngineContext Context,
        System.Threading.CancellationToken cancellationToken = default
    ) {
        // Keep the incoming operation metadata raw so nested on-success operations are
        // resolved only when each child actually executes.
        IDictionary<string, object?> rawOperation = op;

        // 1. Build the execution Context once
        Dictionary<string, object?> ctx = Core.Utils.ExecutionContextBuilder.Build(currentGame, games, Context.EngineConfig.Data);

        // 2. Resolve placeholders for the current operation execution payload.
        //    Nested onsuccess blocks are intentionally excluded from this resolution pass.
        IDictionary<string, object?> executableOperation = ResolveExecutionPayload(rawOperation, ctx);

        string? scriptType = (executableOperation.TryGetValue("script_type", out object? st) ? st?.ToString() : null)?.ToLowerInvariant();
        List<string> parts = Context.CommandService.BuildCommand(currentGame, games, Context.EngineConfig.Data, executableOperation, promptAnswers);
        if (parts.Count < 2) {
            return false;
        }

        string scriptPath = parts[1];
        string[] args = parts.Skip(2).ToArray();

        if (scriptType is null) {
            Core.Diagnostics.Log($"[RunSingleAsync.cs::RunSingleOperationAsync()] Missing script_type for operation with script '{scriptPath}'");
            return false;
        }

        bool result = false;
        try {
            switch (scriptType) {
                // for running built-in engine operations (like download-tools) or internal operations (like download_module_git)
                case var t when Utils.ScriptConstants.IsBuiltIn(t): {
                    try {
                        string? action = executableOperation.TryGetValue("script", out object? s) ? s?.ToString() : null;
                        string? title = executableOperation.TryGetValue("Name", out object? n) ? n?.ToString() ?? action : action;
                        Core.Diagnostics.Log($"[RunSingleAsync.cs::RunSingleOperationAsync()] Executing engine operation {title} ({action})");
                        Core.UI.EngineSdk.PrintLine(message: $"\n>>> Engine operation: {title}");
                        // delegate engine type handling to ExecuteEngineOperationAsync
                        //var op_dispatcher = new helpers.OpDispatcher();
                        result = await helpers.OpDispatcher.DispatchAsync(executableOperation, promptAnswers, currentGame, games, Context, cancellationToken);
                    } catch (System.Exception ex) {
                        Core.UI.EngineSdk.PrintLine($"engine ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                // for running external script types (like bms)
                case var t when Utils.ScriptConstants.IsExternal(t): {
                    try {
                        string inputDir = executableOperation.TryGetValue("input", out object? in0) ? in0?.ToString() ?? string.Empty : string.Empty;
                        string outputDir = executableOperation.TryGetValue("output", out object? out0) ? out0?.ToString() ?? string.Empty : string.Empty;
                        string? extension = executableOperation.TryGetValue("extension", out object? ext0) ? ext0?.ToString() : null;

                        if (!games.TryGetValue(currentGame, out Core.Data.GameModuleInfo? gameInfo)) {
                            throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                        }

                        var action = ScriptEngines.ScriptActionDispatcher.ExternalActionDispatcher.TryCreate(
                            scriptType: scriptType,
                            scriptPath: scriptPath,
                            gameRoot: gameInfo.GameRoot,
                            inputDir: inputDir,
                            outputDir: outputDir,
                            extension: extension
                        );

                        if (action is null) {
                            result = false;
                            Core.Diagnostics.Log($"[RunSingleAsync.cs::RunSingleOperationAsync()] Unsupported external script type '{scriptType}'");
                            break;
                        }

                        await action.ExecuteAsync(Context.ToolResolver, cancellationToken);
                        result = true;
                    } catch (System.Exception ex) {
                        Core.UI.EngineSdk.PrintLine($"bms engine ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                // for running embedded script types (lua, js, python)
                case var t when Core.Utils.ScriptConstants.IsEmbedded(t): {
                    try {
                        // create the action with the dispatcher
                        IEnumerable<string> argsEnum = args;
                        ScriptEngines.IAction? act = ScriptEngines.ScriptActionDispatcher.EmbeddedActionDispatcher.TryCreate(
                            scriptType: scriptType,
                            scriptPath: scriptPath,
                            args: argsEnum,
                            currentGame: currentGame,
                            games: games
                        );
                        // null act means unsupported script type
                        if (act is null) {
                            result = false;
                            Core.Diagnostics.Log($"[RunSingleAsync.cs::RunSingleOperationAsync()] Unsupported embedded script type '{scriptType}'");
                            break;
                        }
                        // execute the action
                        await act.ExecuteAsync(Context.ToolResolver, cancellationToken);
                        result = true;
                    } catch (System.Exception ex) {
                        Core.UI.EngineSdk.PrintLine($"{scriptType} engine ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                default: {
                    // not supported
                    Core.Diagnostics.Log($"[RunSingleAsync.cs::RunSingleOperationAsync()] Unsupported script type '{scriptType}'");
                    break;
                }
            }
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"[RunSingleAsync.cs::RunSingleOperationAsync()] err running single op: {ex.Message}");
            Core.UI.EngineSdk.PrintLine($"operation ERROR: {ex.Message}");
            result = false;
        }

        // If the main operation succeeded, run any nested [[operation.onsuccess]] steps
        if (result && helpers.OpMetadataExtractor.ExtractSuccessActions(rawOperation, out List<Dictionary<string, object?>>? followUps) && followUps is not null) {
            foreach (Dictionary<string, object?> childOp in followUps) {
                if (cancellationToken.IsCancellationRequested) break;
                bool ok = await Context.OperationContext.Single.RunAsync(currentGame, games, childOp, promptAnswers, Context, cancellationToken);
                if (!ok) {
                    result = false; // propagate failure from any onsuccess step
                }
            }
        }

        return result;
    }

    private static IDictionary<string, object?> ResolveExecutionPayload(
        IDictionary<string, object?> rawOperation,
        IDictionary<string, object?> context
    ) {
        Dictionary<string, object?> executionPayload = new Dictionary<string, object?>(rawOperation, System.StringComparer.OrdinalIgnoreCase);
        executionPayload.Remove("onsuccess");
        executionPayload.Remove("on_success");

        if (Core.Utils.Placeholders.Resolve(executionPayload, context) is IDictionary<string, object?> resolvedPayload) {
            return resolvedPayload;
        }

        return executionPayload;
    }

}
