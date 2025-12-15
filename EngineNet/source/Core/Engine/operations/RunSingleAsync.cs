using System.Collections.Generic;
using System.Linq;

namespace EngineNet.Core;


internal sealed partial class Engine {

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
        System.Threading.CancellationToken cancellationToken = default
    ) {
        string? scriptType = (op.TryGetValue("script_type", out object? st) ? st?.ToString() : null)?.ToLowerInvariant();
        List<string> parts = _builder.Build(currentGame, games, _engineConfig.Data, op, promptAnswers);
        if (parts.Count < 2) {
            return false;
        }

        string scriptPath = parts[1];
        string[] args = parts.Skip(2).ToArray();

        bool result = false;
        try {
            switch (scriptType) {
                case "engine": {
                    try {
                        string? action = op.TryGetValue("script", out object? s) ? s?.ToString() : null;
                        string? title = op.TryGetValue("Name", out object? n) ? n?.ToString() ?? action : action;
                        Core.Diagnostics.Log($"[RunSingleAsync.cs::RunSingleOperationAsync()] Executing engine operation {title} ({action})");
                        Core.Utils.EngineSdk.PrintLine(message: $"\n>>> Engine operation: {title}");
                        // delegate engine type handling to ExecuteEngineOperationAsync
                        result = await ExecuteEngineOperationAsync(currentGame, games, op, promptAnswers, cancellationToken);
                    } catch (System.Exception ex) {
                        Core.Utils.EngineSdk.PrintLine($"engine ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                case "bms": {
                    try {
                        // Build context and resolve input/output/extension placeholders
                        Core.Utils.ExecutionContextBuilder ctxBuilder = new Core.Utils.ExecutionContextBuilder();
                        Dictionary<string, object?> ctx = ctxBuilder.Build(currentGame: currentGame, games: games, engineConfig: _engineConfig.Data);

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

                        ScriptEngines.QuickBmsScriptAction action = new ScriptEngines.QuickBmsScriptAction(
                            scriptPath: scriptPath,
                            moduleRoot: gameRootBms,
                            projectRoot: rootPath,
                            inputDir: resolvedInput,
                            outputDir: resolvedOutput,
                            extension: resolvedExt
                        );
                        await action.ExecuteAsync(_tools, cancellationToken);
                        result = true;
                    } catch (System.Exception ex) {
                        Core.Utils.EngineSdk.PrintLine($"bms engine ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                // embedded script engines, Moonsharp (Lua), Python (IronPython), JavaScript (Jint)
                case "lua":
                case "python":
                case "js": {
                    try {
                        // create the action with the dispatcher
                        IEnumerable<string> argsEnum = args;
                        ScriptEngines.Helpers.IAction? act = ScriptEngines.Helpers.EmbeddedActionDispatcher.TryCreate(
                            scriptType: scriptType,
                            scriptPath: scriptPath,
                            args: argsEnum,
                            currentGame: currentGame,
                            games: games,
                            rootPath: rootPath
                        );
                        // null act means unsupported script type
                        if (act is null) {
                            result = false;
                            Core.Diagnostics.Log($"[RunSingleAsync.cs::RunSingleOperationAsync()] Unsupported embedded script type '{scriptType}'");
                            break;
                        }
                        // execute the action
                        await act.ExecuteAsync(_tools, cancellationToken);
                        result = true;
                    } catch (System.Exception ex) {
                        Core.Utils.EngineSdk.PrintLine($"{scriptType} engine ERROR: {ex.Message}");
                        result = false;
                    }
                    break;
                }
                default: {
                    // not supported
                    scriptType ??= "null";
                    Core.Diagnostics.Log($"[RunSingleAsync.cs::RunSingleOperationAsync()] Unsupported script type '{scriptType}'");
                    break;
                }
            }
        } catch (System.Exception ex) {
            Core.Diagnostics.Bug($"[RunSingleAsync.cs::RunSingleOperationAsync()] err running single op: {ex.Message}");

            Core.Utils.EngineSdk.PrintLine($"operation ERROR: {ex.Message}");
            result = false;
        }

        // If the main operation succeeded, run any nested [[operation.onsuccess]] steps
        if (result && TryGetOnSuccessOperations(op, out List<Dictionary<string, object?>>? followUps) && followUps is not null) {
            foreach (Dictionary<string, object?> childOp in followUps) {
                if (cancellationToken.IsCancellationRequested) break;
                bool ok = await RunSingleOperationAsync(currentGame, games, childOp, promptAnswers, cancellationToken);
                if (!ok) {
                    result = false; // propagate failure from any onsuccess step
                }
            }
        }

        return result;
    }

}
