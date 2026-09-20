using System.Collections.Generic;
using EngineNet.Core.Abstractions;

namespace EngineNet.ScriptEngines;

/// <summary>
/// Wrapper for IJsonToolResolver that injects module-specific tool versions
/// </summary>
internal sealed class ContextualToolResolver : IJsonToolResolver {
    private readonly IJsonToolResolver _base;
    private readonly Dictionary<string, string> _contextVersions;

    internal ContextualToolResolver(IJsonToolResolver baseResolver, Dictionary<string, string> contextVersions) {
        _base = baseResolver;
        _contextVersions = contextVersions;
    }

    public string ResolveToolPath(string toolId, string? version = null) {
        if (version == null && _contextVersions.TryGetValue(key: toolId, out string? v)) {
            version = v;
        }

        return _base.ResolveToolPath(toolId: toolId, version: version);
    }
}