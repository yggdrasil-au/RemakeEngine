namespace EngineNet.ScriptEngines;


/// <summary>
/// Wrapper for JsonToolResolver that injects module-specific tool versions
/// </summary>
internal class ContextualToolResolver : Core.ExternalTools.JsonToolResolver {
    internal readonly Core.ExternalTools.JsonToolResolver _base;
    internal readonly Dictionary<string, string> _contextVersions;
    internal ContextualToolResolver(Core.ExternalTools.JsonToolResolver baseResolver, Dictionary<string, string> contextVersions) {
        _base = baseResolver;
        _contextVersions = contextVersions;
    }
    internal override string ResolveToolPath(string toolId, string? version = null) {
        if (version == null && _contextVersions.TryGetValue(toolId, out var v)) {
            version = v;
        }
        return _base.ResolveToolPath(toolId, version);
    }
}
