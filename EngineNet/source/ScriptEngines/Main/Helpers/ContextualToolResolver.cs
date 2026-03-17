namespace EngineNet.ScriptEngines;


/// <summary>
/// Wrapper for IToolResolver that injects module-specific tool versions
/// </summary>
public class ContextualToolResolver : Core.Abstractions.IToolResolver {
    public readonly Core.Abstractions.IToolResolver _base;
    public readonly Dictionary<string, string> _contextVersions;
    public ContextualToolResolver(Core.Abstractions.IToolResolver baseResolver, Dictionary<string, string> contextVersions) {
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