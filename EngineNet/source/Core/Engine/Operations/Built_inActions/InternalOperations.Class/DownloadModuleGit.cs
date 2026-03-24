using System.Collections.Generic;

namespace EngineNet.Core.Engine.operations.Built_inActions;
internal partial class InternalOperations {

    internal bool DownloadModuleGit(IDictionary<string, object?> promptAnswers, EngineContext context) {
        string? url = null;
        if (promptAnswers.TryGetValue("url", out object? u)) {
            url = u?.ToString();
        }
        if (string.IsNullOrWhiteSpace(url)) {
            Core.UI.EngineSdk.Error("No URL provided.");
            Core.Diagnostics.Trace("[Engine.private.cs :: InternalOperations()]] download_module_git: no url provided");
            return false;
        }
        return context.GitService.CloneModule(url);
    }
}
