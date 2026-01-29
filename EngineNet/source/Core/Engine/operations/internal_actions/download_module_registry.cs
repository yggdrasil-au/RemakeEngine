using System.Collections.Generic;

namespace EngineNet.Core;
internal partial class OperationExecution {

    internal bool DownloadModuleRegistry(IDictionary<string, object?> promptAnswers, Abstractions.IGitService GitService, Abstractions.IGameRegistry GameRegistry) {

        string? input = null;
        if (promptAnswers.TryGetValue("url", out object? u)) {
            input = u?.ToString();
        }
        if (string.IsNullOrWhiteSpace(input)) {
            Core.Utils.EngineSdk.Error("No input provided.");
            return false;
        }

        var knownModules = GameRegistry.GetRegisteredModules();
        string? url = input;

        if (knownModules.TryGetValue(input!, out object? modObj) && modObj is Dictionary<string, object?> modData) {
            if (modData.TryGetValue("url", out object? uObj)) {
                url = uObj?.ToString();
            }
        }

        if (string.IsNullOrWhiteSpace(url)) {
            Core.Utils.EngineSdk.Error($"Could not resolve URL for '{input}'.");
            return false;
        }

        return GitService.CloneModule(url);
    }
}
