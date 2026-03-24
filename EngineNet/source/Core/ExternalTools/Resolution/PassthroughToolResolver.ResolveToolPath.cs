


namespace EngineNet.Core.ExternalTools;

/// <summary>
/// Fallback tool resolver that simply returns the tool id as the path.
/// Useful when tools are expected to be on PATH.
/// </summary>
internal sealed class PassthroughToolResolver {
    /// <summary>
    /// Returns <paramref name="toolId"/> unchanged.
    /// </summary>
    /// <param name="toolId">Logical identifier of the tool, typically also its executable name.</param>
    /// <param name="version">Ignored in this implementation.</param>
    /// <returns>The original <paramref name="toolId"/>.</returns>
    internal string ResolveToolPath(string toolId, string? version = null) => toolId;
}

