namespace EngineNet.ScriptEngines;


/// <summary>
/// Wrapper for IToolResolver that injects module-specific tool versions
/// </summary>
public class ContextualToolResolver : Core.ExternalTools.IToolResolver {
    public readonly Core.ExternalTools.IToolResolver _base;
    public readonly Dictionary<string, string> _contextVersions;
    public ContextualToolResolver(Core.ExternalTools.IToolResolver baseResolver, Dictionary<string, string> contextVersions) {
        _base = baseResolver;
        _contextVersions = contextVersions;
    }
    public string ResolveToolPath(string toolId, string? version = null) {
        if (version == null && _contextVersions.TryGetValue(toolId, out var v)) {
            version = v;
        }
        return _base.ResolveToolPath(toolId, version);
    }
}