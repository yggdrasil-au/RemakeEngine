
namespace EngineNet.Core.Operations.Built_inActions;
internal sealed partial class InternalOperations {

    internal bool DownloadModuleRegistry(Core.Data.PromptAnswers promptAnswers, Engine.EngineContext context) {

        string? input = null;
        if (promptAnswers.TryGetValue(key: "url", out object? u)) {
            input = u?.ToString();
        }

        if (string.IsNullOrWhiteSpace(input)) {
            IO.Error("No input provided.");
            Shared.IO.Diagnostics.Trace("] download_module_registry: no input provided");
            return false;
        }

        IReadOnlyDictionary<string, object?> knownModules = context.GameRegistry.GetRegisteredModules();
        string? url = input;

        if (knownModules.TryGetValue(key: input, out object? modObj) && modObj is Dictionary<string, object?> modData) {
            if (modData.TryGetValue(key: "url", out object? uObj)) {
                url = uObj?.ToString();
            }
        }

        if (string.IsNullOrWhiteSpace(url)) {
            IO.Error($"Could not resolve URL for '{input}'.");
            return false;
        }

        return Core.Utils.GitTools.CloneModule(url: url, commandService: context.CommandService);
    }
}
