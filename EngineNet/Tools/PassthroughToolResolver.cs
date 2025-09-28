using System;

namespace EngineNet.Tools;

/// <summary>
/// Fallback tool resolver that simply returns the tool id as the path.
/// Useful when tools are expected to be on PATH.
/// </summary>
public sealed class PassthroughToolResolver:IToolResolver {
    /// <summary>
    /// Returns <paramref name="toolId"/> unchanged.
    /// </summary>
    /// <param name="toolId">Logical identifier of the tool, typically also its executable name.</param>
    /// <returns>The original <paramref name="toolId"/>.</returns>
    public String ResolveToolPath(String toolId) => toolId;
}

