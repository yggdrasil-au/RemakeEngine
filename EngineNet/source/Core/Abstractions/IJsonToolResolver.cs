namespace EngineNet.Core.Abstractions;

/// <summary>
/// Defines a resolver for locating external tool paths.
/// </summary>
public interface IJsonToolResolver {
    /// <summary>
    /// Determines whether an executable path exactly matches a tool tracked in the installed-tools lockfile.
    /// </summary>
    /// <param name="executablePath">The executable path to validate.</param>
    /// <returns><see langword="true"/> when the path is tracked; otherwise, <see langword="false"/>.</returns>
    bool IsTrackedTool(string executablePath);

    /// <summary>
    /// Resolves the executable path and installed version for a tracked tool.
    /// </summary>
    /// <param name="toolId">The registered tool identifier.</param>
    /// <returns>The executable path and version, or <see langword="null"/> values when the tool is not tracked.</returns>
    (string? exe, string? version) ResolveExeAndVersion(string toolId);

    /// <summary>
    /// Resolves an executable path for a tool identifier, optionally selecting an installed version.
    /// </summary>
    /// <param name="toolId">The registered tool identifier.</param>
    /// <param name="version">The optional installed version to resolve.</param>
    /// <returns>The resolved executable path, or the original tool identifier when it is not tracked.</returns>
    string ResolveToolPath(string toolId, string? version = null);
}
