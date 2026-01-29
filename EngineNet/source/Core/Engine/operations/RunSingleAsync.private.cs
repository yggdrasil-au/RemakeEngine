using System.Collections.Generic;

namespace EngineNet.Core;

internal sealed partial class Engine {

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
    private async System.Threading.Tasks.Task<bool> ExecuteEngineOperationAsync(string currentGame, Dictionary<string, EngineNet.Core.Utils.GameModuleInfo> games, IDictionary<string, object?> op, IDictionary<string, object?> promptAnswers, System.Threading.CancellationToken cancellationToken = default) {
        if (!op.TryGetValue("script", out object? s) || s is null) {
            Core.Diagnostics.Log("[Engine.private.cs :: OperationExecution()] Missing 'script' value in engine operation");
            return false;
        } else {
            Core.Diagnostics.Log($"[Engine.private.cs :: OperationExecution()]] engine operation script: {s}");
        }

        // ensure internal ops are from allowed dirs
        string? type = op.TryGetValue("script_type", out object? st) ? st?.ToString()?.ToLowerInvariant() : null;
        if (type == "internal") {
            string? sourceFile = op.TryGetValue("_source_file", out object? sf) ? sf?.ToString() : null;
            if (string.IsNullOrWhiteSpace(sourceFile)) {
                Core.Utils.EngineSdk.Error("Internal operation blocked: Missing source file context.");
                return false;
            }
            string allowedDir = System.IO.Path.Combine(RootPath, "EngineApps", "Registries", "ops");
            string fullSource = System.IO.Path.GetFullPath(sourceFile);
            string fullAllowed = System.IO.Path.GetFullPath(allowedDir);

            if (!fullSource.StartsWith(fullAllowed, System.StringComparison.OrdinalIgnoreCase)) {
                Core.Utils.EngineSdk.Error($"Internal operation blocked: Source '{sourceFile}' is not in allowed directory '{allowedDir}'.");
                return false;
            }
        }

        // Determine action
        string? action = s.ToString()?.ToLowerInvariant();

        Core.Diagnostics.Log($"[Engine.private.cs :: OperationExecution()]] Executing engine action: {action}");
        switch (action) {
            case "download_module_git": {
                return new OperationExecution().DownloadModuleGit(promptAnswers, GitService);
            }
            case "download_module_registry": {
                return new OperationExecution().DownloadModuleRegistry(promptAnswers, GitService, GameRegistry);
            }
            case "download-tools": {
                return await new OperationExecution().DownloadTools(op, promptAnswers, currentGame, games, RootPath, EngineConfig);
            }
            case "format-extract": {
                return new OperationExecution().format_extract(op, promptAnswers, currentGame, games, RootPath, EngineConfig);
            }
            case "format-convert": {
                return new OperationExecution().format_convert(op, promptAnswers, currentGame, games, RootPath, EngineConfig, ToolResolver);
            }
            case "validate-files": {
                return new OperationExecution().validate_files(op, promptAnswers, currentGame, games, RootPath, EngineConfig);
            }
            case "rename-folders": {
                return new OperationExecution().rename_folders(op, promptAnswers, currentGame, games, RootPath, EngineConfig);
            }
            default: {
                Core.Diagnostics.Log($"[Engine.private.cs :: OperationExecution()]] Unknown engine action: {action}");
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
    private static bool TryGetOnSuccessOperations(
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