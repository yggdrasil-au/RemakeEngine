
namespace EngineNet.Core.Engine.Operations.helpers;

internal class OpDispatcher {

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
    internal async System.Threading.Tasks.Task<bool> DispatchAsync(
        string currentGame,
        Dictionary<string, EngineNet.Core.Data.GameModuleInfo> games,
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers,
        EngineContext context,
        System.Threading.CancellationToken cancellationToken = default
    ) {
        if (!op.TryGetValue("script", out object? s) || s is null) {
            Core.Diagnostics.Log("[Engine.private.cs :: Operations()] Missing 'script' value in engine operation");
            return false;
        }

        // 'op' is already fully resolved from Runner.RunSingleOperationAsync!
        IDictionary<string, object?> resolvedOp = op;

        Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] engine operation script: {s}");

        // ensure internal ops are from allowed dirs
        string? type = resolvedOp.TryGetValue("script_type", out object? st) ? st?.ToString()?.ToLowerInvariant() : null;
        if (type == "internal") {
            string? sourceFile = resolvedOp.TryGetValue("_source_file", out object? sf) ? sf?.ToString() : null;
            if (string.IsNullOrWhiteSpace(sourceFile)) {
                Core.UI.EngineSdk.Error("Internal operation blocked: Missing source file context.");
                return false;
            }
            string allowedDir = System.IO.Path.Combine(EngineNet.Core.Main.RootPath, "EngineApps", "Registries", "ops");
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
                return new operations.Built_inActions.InternalOperations().DownloadModuleGit(promptAnswers, context);
            }
            case "download_module_registry": {
                return new operations.Built_inActions.InternalOperations().DownloadModuleRegistry(promptAnswers, context);
            }

            // Built-in actions
            case "config": {
                return new operations.Built_inActions.BuiltInOperations().config(resolvedOp, currentGame, games);
            }
            case "download-tools": {
                return await new operations.Built_inActions.BuiltInOperations().DownloadTools(resolvedOp, promptAnswers, currentGame, games, context, cancellationToken);
            }
            case "format-extract": {
                return new operations.Built_inActions.BuiltInOperations().format_extract(resolvedOp, promptAnswers, currentGame, games, context, cancellationToken);
            }
            case "format-convert": {
                return new operations.Built_inActions.BuiltInOperations().format_convert(resolvedOp, promptAnswers, currentGame, games, context, cancellationToken);
            }
            case "validate-files": {
                return new operations.Built_inActions.BuiltInOperations().validate_files(resolvedOp, promptAnswers, currentGame, games, context, cancellationToken);
            }
            case "rename-folders": {
                return new operations.Built_inActions.BuiltInOperations().rename_folders(resolvedOp, promptAnswers, currentGame, games, context, cancellationToken);
            }
            default: {
                Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] Unknown engine action: {action}");
                return false;
            }
        }
    }


}
