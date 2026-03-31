
namespace EngineNet.Core.Engine.operations.Built_inActions;
internal partial class InternalOperations {

    internal bool DownloadModuleRegistry(Core.Data.PromptAnswers promptAnswers, EngineContext context) {

        string? input = null;
        if (promptAnswers.TryGetValue("url", out object? u)) {
            input = u?.ToString();
        }

        if (string.IsNullOrWhiteSpace(input)) {
            Core.UI.EngineSdk.Error("No input provided.");
            Core.Diagnostics.Trace("[Engine.private.cs :: InternalOperations()]] download_module_registry: no input provided");
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
            Core.UI.EngineSdk.Error($"Could not resolve URL for '{input}'.");
            return false;
        }

        return Utils.GitTools.CloneModule(url, context.CommandService);
    }
}
