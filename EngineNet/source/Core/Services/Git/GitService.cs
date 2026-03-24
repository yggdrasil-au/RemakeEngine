
using EngineNet.Core.Utils;

namespace EngineNet.Core.Services;

internal class GitService {
    private readonly GitTools _gitTools;

    internal GitService() {
        _gitTools = new GitTools();
    }

    internal bool CloneModule(string url) {
        return _gitTools.CloneModule(url);
    }
}
