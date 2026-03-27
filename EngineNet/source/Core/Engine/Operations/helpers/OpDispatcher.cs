
namespace EngineNet.Core.Engine.Operations.helpers;

internal class OpDispatcher {

    // used by run single operation to execute engine operations of type "engine"

    /// <summary>
    /// Executes an engine operation of type "engine".
    /// </summary>
    /// <param name="currentGame"></param>
    /// <param name="games"></param>
    /// <param name="executableOperation"></param>
    /// <param name="promptAnswers"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="KeyNotFoundException"></exception>
    internal async System.Threading.Tasks.Task<bool> DispatchAsync(
        IDictionary<string, object?> executableOperation,
        IDictionary<string, object?> promptAnswers,
        string currentGame,
        Dictionary<string, Core.Data.GameModuleInfo> games,
        EngineContext context,
        System.Threading.CancellationToken cancellationToken = default
    ) {
        if (!executableOperation.TryGetValue("script", out object? s) || s is null) {
            Core.Diagnostics.Log("[Engine.private.cs :: Operations()] Missing 'script' value in engine operation");
            return false;
        }

        // 'executableOperation' is already fully resolved from Runner.RunSingleOperationAsync!
        //IDictionary<string, object?> executableOperation = executableOperation;

        Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] engine operation script: {s}");

        // ensure internal ops are from allowed dirs
        string? type = executableOperation.TryGetValue("script_type", out object? st) ? st?.ToString()?.ToLowerInvariant() : null;
        if (type == "internal") {
            string? sourceFile = executableOperation.TryGetValue("_source_file", out object? sf) ? sf?.ToString() : null;
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

        // create built-in object for passing to built-in actions
        var operationArgs = new OperationArgs(
            executableOperation,
            promptAnswers,
            currentGame,
            games,
            context,
            cancellationToken
        );

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
                return operations.Built_inActions.BuiltInOperations.config(executableOperation, currentGame, games);
            }


            case "download-tools": {
                return await operations.Built_inActions.BuiltInOperations.DownloadTools(operationArgs);
            }
            case "format-extract": {
                return operations.Built_inActions.BuiltInOperations.format_extract(operationArgs);
            }
            case "format-convert": {
                return operations.Built_inActions.BuiltInOperations.format_convert(operationArgs);
            }
            case "validate-files": {
                return operations.Built_inActions.BuiltInOperations.validate_files(operationArgs);
            }
            case "rename-folders": {
                return operations.Built_inActions.BuiltInOperations.rename_folders(operationArgs);
            }
            default: {
                Core.Diagnostics.Log($"[Engine.private.cs :: Operations()]] Unknown engine action: {action}");
                return false;
            }
        }
    }


}

internal class OperationArgs {
    internal IDictionary<string, object?> op;
    internal IDictionary<string, object?> promptAnswers;
    internal string currentGame;
    internal Dictionary<string, Core.Data.GameModuleInfo> games;
    internal EngineContext context;
    internal System.Threading.CancellationToken cancellationToken;

    internal OperationArgs(
        IDictionary<string, object?> op,
        IDictionary<string, object?> promptAnswers,
        string currentGame,
        Dictionary<string, Core.Data.GameModuleInfo> games,
        EngineContext context,
        System.Threading.CancellationToken cancellationToken
    ) {
        this.op = op;
        this.promptAnswers = promptAnswers;
        this.currentGame = currentGame;
        this.games = games;
        this.context = context;
        this.cancellationToken = cancellationToken;
    }
}