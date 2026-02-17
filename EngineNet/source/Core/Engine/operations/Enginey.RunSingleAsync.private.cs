using System.Collections.Generic;
using EngineNet.Core.Utils;

namespace EngineNet.Core.Engine;

internal sealed class OperationExecution {

    // used by run single operation to execute engine operations of type "engine"

    /// <summary>
    /// Executes an engine operation of type "engine".
    /// </summary>
    /// <param name="currentGame"></param>
    /// <param name="games"></param>
    /// <param name="op"></param>
    /// <param name="promptAnswers"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="KeyNotFoundException"></exception>
    internal async System.Threading.Tasks.Task<bool> ExecuteEngineOperationAsync(
        string currentGame,
        Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games,
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers,
        Core.EngineConfig EngineConfig,
        Core.ExternalTools.IToolResolver ToolResolver,
        Core.Services.GitService GitService,
        Core.Abstractions.IGameRegistry GameRegistry,
        System.Threading.CancellationToken cancellationToken = default
    ) {
        if (!op.TryGetValue("script", out object? s) || s is null) {
            Core.Diagnostics.Log("[Engine.private.cs :: Operations()] Missing 'script' value in engine operation");
            return false;
        }

        // Resolve placeholders in the operation dictionary before execution
        IDictionary<string, object?> resolvedOp = op;
        if (!string.IsNullOrWhiteSpace(currentGame)) {
            try {
                ExecutionContextBuilder ctxBuilder = new ExecutionContextBuilder();
                Dictionary<string, object?> ctx = ctxBuilder.Build(
                    currentGame: currentGame,
                    games: games,
                    engineConfig: EngineConfig.Data
                );
                object? resolved = Placeholders.Resolve(op, ctx);
                if (resolved is IDictionary<string, object?> resolvedDict) {
                    resolvedOp = resolvedDict;
                    // Update the local script variable 's' to the resolved version if available
                    if (resolvedOp.TryGetValue("script", out object? rs) && rs is not null) {
                        s = rs;
                    }
                }
            } catch (System.Exception ex) {
                Core.Diagnostics.Log($"[Engine.private.cs :: Operations()] Warning: Placeholder resolution failed: {ex.Message}");
            }
        }

        Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] engine operation script: {s}");

        // ensure internal ops are from allowed dirs
        string? type = resolvedOp.TryGetValue("script_type", out object? st) ? st?.ToString()?.ToLowerInvariant() : null;
        if (type == "internal") {
            string? sourceFile = resolvedOp.TryGetValue("_source_file", out object? sf) ? sf?.ToString() : null;
            if (string.IsNullOrWhiteSpace(sourceFile)) {
                Core.UI.EngineSdk.Error("Internal operation blocked: Missing source file context.");
                return false;
            }
            string allowedDir = System.IO.Path.Combine(Program.rootPath, "EngineApps", "Registries", "ops");
            string fullSource = System.IO.Path.GetFullPath(sourceFile);
            string fullAllowed = System.IO.Path.GetFullPath(allowedDir);

            if (!fullSource.StartsWith(fullAllowed, System.StringComparison.OrdinalIgnoreCase)) {
                Core.UI.EngineSdk.Error($"Internal operation blocked: Source '{sourceFile}' is not in allowed directory '{allowedDir}'.");
                return false;
            }
        }

        // Determine action
        string? action = s.ToString()?.ToLowerInvariant();

        Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] Executing engine action: {action}");
        switch (action) {
            // internal modules
            case "download_module_git": {
                return new operations.Built_inActions.InternalOperations().DownloadModuleGit(promptAnswers, GitService);
            }
            case "download_module_registry": {
                return new operations.Built_inActions.InternalOperations().DownloadModuleRegistry(promptAnswers, GitService, GameRegistry);
            }

            // Built-in actions
            case "config": {
                return new operations.Built_inActions.InternalOperations().config(resolvedOp, promptAnswers, currentGame, games, Program.rootPath, EngineConfig);
            }
            case "download-tools": {
                return await new operations.Built_inActions.InternalOperations().DownloadTools(resolvedOp, promptAnswers, currentGame, games, Program.rootPath, EngineConfig);
            }
            case "format-extract": {
                return new operations.Built_inActions.InternalOperations().format_extract(resolvedOp, promptAnswers, currentGame, games, Program.rootPath, EngineConfig);
            }
            case "format-convert": {
                return new operations.Built_inActions.InternalOperations().format_convert(resolvedOp, promptAnswers, currentGame, games, Program.rootPath, EngineConfig, ToolResolver);
            }
            case "validate-files": {
                return new operations.Built_inActions.InternalOperations().validate_files(resolvedOp, promptAnswers, currentGame, games, Program.rootPath, EngineConfig);
            }
            case "rename-folders": {
                return new operations.Built_inActions.InternalOperations().rename_folders(resolvedOp, promptAnswers, currentGame, games, Program.rootPath, EngineConfig);
            }
            default: {
                Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] Unknown engine action: {action}");
                return false;
            }
        }
    }


    /// <summary>
    /// Try to get the list of operations defined in the "onsuccess" or "on_success" field of the given operation.
    /// </summary>
    /// <param name="op"></param>
    /// <param name="ops"></param>
    /// <returns></returns>
    internal static bool TryGetOnSuccessOperations(
        IDictionary<string, object?> op,
        out List<Dictionary<string, object?>>? ops
    ) {
        ops = null;
        if (op is null) return false;

        static List<Dictionary<string, object?>>? Coerce(object? value) {
            if (value is null) return null;
            List<Dictionary<string, object?>> list = new List<Dictionary<string, object?>>();
            if (value is IList<object?> arr) {
                foreach (object? item in arr) {
                    if (item is IDictionary<string, object?> map) {
                        list.Add(new Dictionary<string, object?>(map, System.StringComparer.OrdinalIgnoreCase));
                    }
                }
            } else if (value is IDictionary<string, object?> single) {
                list.Add(new Dictionary<string, object?>(single, System.StringComparer.OrdinalIgnoreCase));
            }
            return list.Count > 0 ? list : null;
        }

        if (op.TryGetValue("onsuccess", out object? v1)) {
            ops = Coerce(v1);
            if (ops is not null) return true;
        }
        if (op.TryGetValue("on_success", out object? v2)) {
            ops = Coerce(v2);
            if (ops is not null) return true;
        }
        return false;
    }
}