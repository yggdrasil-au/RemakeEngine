using EngineNet.Core.Abstractions;
using EngineNet.Core.Utils;

namespace EngineNet.Core.Services;

public class GitService : IGitService {
    private readonly GitTools _gitTools;

    public GitService(string rootPath) {
        _gitTools = new GitTools(System.IO.Path.Combine(rootPath, "EngineApps", "Games"));
    }

    public bool CloneModule(string url) {
        return _gitTools.CloneModule(url);
    }
}
