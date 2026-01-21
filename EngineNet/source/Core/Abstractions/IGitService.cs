namespace EngineNet.Core.Abstractions;

internal interface IGitService {
    bool CloneModule(string url);
}
