
namespace EngineNet.Core.Operations.Built_inActions;
internal partial class InternalOperations {

    internal bool DownloadModuleGit(Core.Data.PromptAnswers promptAnswers, Engine.EngineContext context) {
        string? url = null;
        if (promptAnswers.TryGetValue("url", out object? u)) {
            url = u?.ToString();
        }
        if (string.IsNullOrWhiteSpace(url)) {
            Core.UI.EngineSdk.Error("No URL provided.");
            Shared.Diagnostics.Trace("[Engine.private.cs :: InternalOperations()]] download_module_git: no url provided");
            return false;
        }
        return Utils.GitTools.CloneModule(url, context.CommandService);
    }
}
