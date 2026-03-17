namespace EngineNet.ScriptEngines;


/// <summary>
/// Wrapper for JsonToolResolver that injects module-specific tool versions
/// </summary>
public class ContextualToolResolver : Core.ExternalTools.JsonToolResolver {
    public readonly Core.ExternalTools.JsonToolResolver _base;
    public readonly Dictionary<string, string> _contextVersions;
    public ContextualToolResolver(Core.ExternalTools.JsonToolResolver baseResolver, Dictionary<string, string> contextVersions) {
        _base = baseResolver;
        _contextVersions = contextVersions;
    }
    public override string ResolveToolPath(string toolId, string? version = null) {
        if (version == null && _contextVersions.TryGetValue(toolId, out var v)) {
            version = v;
        }
        return _base.ResolveToolPath(toolId, version);
    }
}
