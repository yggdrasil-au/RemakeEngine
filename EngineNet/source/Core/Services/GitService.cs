using EngineNet.Core.Abstractions;
using EngineNet.Core.Utils;

namespace EngineNet.Core.Services;

public class GitService : IGitService {
    private readonly GitTools _gitTools;

    public GitService() {
        _gitTools = new GitTools();
    }

    public bool CloneModule(string url) {
        return _gitTools.CloneModule(url);
    }
}
