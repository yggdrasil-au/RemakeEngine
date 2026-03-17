using System.Collections.Generic;
using System.Linq;

namespace EngineNet.Core.Engine;

public sealed class Runner {

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
    public async System.Threading.Tasks.Task<bool> RunSingleOperationAsync(
        string currentGame,
        Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games,
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers,
        EngineContext context,
        System.Threading.CancellationToken cancellationToken = default
    ) {
        // 1. Build the execution context once
        Dictionary<string, object?> ctx = Core.Utils.ExecutionContextBuilder.Build(currentGame, games, context.EngineConfig.Data);

        // 2. Recursively resolve ALL placeholders anywhere in the operation definition
        if (Core.Utils.Placeholders.Resolve(op, ctx) is IDictionary<string, object?> fullyResolvedOp) {
            op = fullyResolvedOp;
        }

        string? scriptType = (op.TryGetValue("script_type", out object? st) ? st?.ToString() : null)?.ToLowerInvariant();
        List<string> parts = context.CommandService.BuildCommand(currentGame, games, context.EngineConfig.Data, op, promptAnswers);
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
                case var t when Utils.ScriptConstants.IsBuiltIn(t): {
                    try {
                        string? action = op.TryGetValue("script", out object? s) ? s?.ToString() : null;
                        string? title = op.TryGetValue("Name", out object? n) ? n?.ToString() ?? action : action;
                        Core.Diagnostics.Log($"[RunSingleAsync.cs::RunSingleOperationAsync()] Executing engine operation {title} ({action})");
                        Core.UI.EngineSdk.PrintLine(message: $"\n>>> Engine operation: {title}");
                        // delegate engine type handling to ExecuteEngineOperationAsync
                        result = await context.OperationContext.OperationExecution.ExecuteEngineOperationAsync(currentGame, games, op, promptAnswers, context, cancellationToken);
                    } catch (System.Exception ex) {
                        Core.UI.EngineSdk.PrintLine($"engine ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                case var t when Utils.ScriptConstants.IsExternal(t): {
                    try {
                        string inputDir = op.TryGetValue("input", out object? in0) ? in0?.ToString() ?? string.Empty : string.Empty;
                        string outputDir = op.TryGetValue("output", out object? out0) ? out0?.ToString() ?? string.Empty : string.Empty;
                        string? extension = op.TryGetValue("extension", out object? ext0) ? ext0?.ToString() : null;

                        if (!games.TryGetValue(currentGame, out Core.Utils.GameModuleInfo? gameInfo)) {
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

                        await action.ExecuteAsync(context.ToolResolver, cancellationToken);
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
                        ScriptEngines.Helpers.IAction? act = ScriptEngines.ScriptActionDispatcher.EmbeddedActionDispatcher.TryCreate(
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
                        await act.ExecuteAsync(context.ToolResolver, cancellationToken);
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
        if (result && OperationExecution.TryGetOnSuccessOperations(op, out List<Dictionary<string, object?>>? followUps) && followUps is not null) {
            foreach (Dictionary<string, object?> childOp in followUps) {
                if (cancellationToken.IsCancellationRequested) break;
                bool ok = await RunSingleOperationAsync(currentGame, games, childOp, promptAnswers, context, cancellationToken);
                if (!ok) {
                    result = false; // propagate failure from any onsuccess step
                }
            }
        }

        return result;
    }

}
