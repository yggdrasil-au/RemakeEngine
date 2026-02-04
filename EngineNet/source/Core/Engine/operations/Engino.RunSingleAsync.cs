using System.Collections.Generic;
using System.Linq;

namespace EngineNet.Core.Engine;

internal sealed class Engino {

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
    internal async System.Threading.Tasks.Task<bool> RunSingleOperationAsync(
        string currentGame,
        Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games,
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers,
        string RootPath,
        Core.EngineConfig EngineConfig,
        Core.ExternalTools.IToolResolver ToolResolver,
        Core.Abstractions.IGitService GitService,
        Core.Abstractions.IGameRegistry GameRegistry,
        Core.Abstractions.ICommandService CommandService,
        OperationExecution OperationExecution,
        System.Threading.CancellationToken cancellationToken = default
    ) {
        string? scriptType = (op.TryGetValue("script_type", out object? st) ? st?.ToString() : null)?.ToLowerInvariant();
        List<string> parts = CommandService.BuildCommand(currentGame, games, EngineConfig.Data, op, promptAnswers);
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
                case Utils.ScriptConstants.TypeInternal:
                case "engine": {
                    try {
                        string? action = op.TryGetValue("script", out object? s) ? s?.ToString() : null;
                        string? title = op.TryGetValue("Name", out object? n) ? n?.ToString() ?? action : action;
                        Core.Diagnostics.Log($"[RunSingleAsync.cs::RunSingleOperationAsync()] Executing engine operation {title} ({action})");
                        Core.UI.EngineSdk.PrintLine(message: $"\n>>> Engine operation: {title}");
                        // delegate engine type handling to ExecuteEngineOperationAsync
                        result = await OperationExecution.ExecuteEngineOperationAsync(currentGame, games, op, promptAnswers, RootPath, EngineConfig, ToolResolver, GitService, GameRegistry, cancellationToken);
                    } catch (System.Exception ex) {
                        Core.UI.EngineSdk.PrintLine($"engine ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                case "bms": {
                    try {
                        // Build context and resolve input/output/extension placeholders
                        Core.Utils.ExecutionContextBuilder ctxBuilder = new Core.Utils.ExecutionContextBuilder();
                        Dictionary<string, object?> ctx = ctxBuilder.Build(currentGame: currentGame, games: games, engineConfig: EngineConfig.Data);

                        string inputDir = op.TryGetValue("input", out object? in0) ? in0?.ToString() ?? string.Empty : string.Empty;
                        string outputDir = op.TryGetValue("output", out object? out0) ? out0?.ToString() ?? string.Empty : string.Empty;
                        string? extension = op.TryGetValue("extension", out object? ext0) ? ext0?.ToString() : null;
                        string resolvedInput = Core.Utils.Placeholders.Resolve(inputDir, ctx)?.ToString() ?? inputDir;
                        string resolvedOutput = Core.Utils.Placeholders.Resolve(outputDir, ctx)?.ToString() ?? outputDir;
                        string? resolvedExt = extension is null ? null : Core.Utils.Placeholders.Resolve(extension, ctx)?.ToString() ?? extension;

                        if (!games.TryGetValue(currentGame, out EngineNet.Core.Utils.GameModuleInfo? gobjBms)) {
                            throw new KeyNotFoundException($"Unknown game '{currentGame}'.");
                        }
                        string gameRootBms = gobjBms.GameRoot;

                        ScriptEngines.qbms.QuickBmsScriptAction action = new ScriptEngines.qbms.QuickBmsScriptAction(
                            scriptPath: scriptPath,
                            moduleRoot: gameRootBms,
                            projectRoot: RootPath,
                            inputDir: resolvedInput,
                            outputDir: resolvedOutput,
                            extension: resolvedExt
                        );
                        await action.ExecuteAsync(ToolResolver, cancellationToken);
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
                        ScriptEngines.Helpers.IAction? act = ScriptEngines.Helpers.EmbeddedActionDispatcher.TryCreate(
                            scriptType: scriptType,
                            scriptPath: scriptPath,
                            args: argsEnum,
                            currentGame: currentGame,
                            games: games,
                            rootPath: RootPath
                        );
                        // null act means unsupported script type
                        if (act is null) {
                            result = false;
                            Core.Diagnostics.Log($"[RunSingleAsync.cs::RunSingleOperationAsync()] Unsupported embedded script type '{scriptType}'");
                            break;
                        }
                        // execute the action
                        await act.ExecuteAsync(ToolResolver, cancellationToken);
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
                bool ok = await RunSingleOperationAsync(currentGame, games, childOp, promptAnswers, RootPath, EngineConfig, ToolResolver, GitService, GameRegistry, CommandService, OperationExecution, cancellationToken);
                if (!ok) {
                    result = false; // propagate failure from any onsuccess step
                }
            }
        }

        return result;
    }

}
