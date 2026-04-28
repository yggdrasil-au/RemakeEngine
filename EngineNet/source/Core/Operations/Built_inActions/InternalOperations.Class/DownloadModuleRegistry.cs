
namespace EngineNet.Core.Operations.Built_inActions;
internal partial class InternalOperations {

    internal bool DownloadModuleRegistry(Core.Data.PromptAnswers promptAnswers, Engine.EngineContext context) {

        string? input = null;
        if (promptAnswers.TryGetValue("url", out object? u)) {
            input = u?.ToString();
        }

        if (string.IsNullOrWhiteSpace(input)) {
            IO.Error("No input provided.");
            Shared.IO.Diagnostics.Trace("[Engine.private.cs :: InternalOperations()]] download_module_registry: no input provided");
            return false;
        }

        var knownModules = context.GameRegistry.GetRegisteredModules();
        string? url = input;

        if (knownModules.TryGetValue(input, out object? modObj) && modObj is Dictionary<string, object?> modData) {
            if (modData.TryGetValue("url", out object? uObj)) {
                url = uObj?.ToString();
            }
        }

        if (string.IsNullOrWhiteSpace(url)) {
            IO.Error($"Could not resolve URL for '{input}'.");
            return false;
        }

        return Core.Utils.GitTools.CloneModule(url, context.CommandService);
    }
}
