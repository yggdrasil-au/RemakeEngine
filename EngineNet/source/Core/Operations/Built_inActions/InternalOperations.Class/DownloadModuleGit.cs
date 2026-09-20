
namespace EngineNet.Core.Operations.Built_inActions;
internal sealed partial class InternalOperations {

    internal bool DownloadModuleGit(Core.Data.PromptAnswers promptAnswers, Core.Abstractions.IEngineContext context) {
        string? url = null;
        if (promptAnswers.TryGetValue(key: "url", out object? u)) {
            url = u?.ToString();
        }
        if (string.IsNullOrWhiteSpace(url)) {
            IO.Error("No URL provided.");
            Shared.IO.Diagnostics.Trace("] download_module_git: no url provided");
            return false;
        }
        return Core.Services.Git.GitTools.CloneModule(url: url, commandService: context.CommandService);
    }
}
