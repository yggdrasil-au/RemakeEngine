
namespace EngineNet.Core.ExternalTools;

/// <summary>
/// Provides paths to external command-line tools required by actions.
/// </summary>
public interface IToolResolver {
    /// <summary>
    /// Resolve the absolute path to a registered tool.
    /// </summary>
    /// <param name="toolId">Logical identifier of the tool (e.g. "ffmpeg").</param>
    /// <param name="version">Optional version string (e.g. "8.0"). If omitted, uses the best available match.</param>
    /// <returns>Absolute filesystem path to the tool executable.</returns>
    string ResolveToolPath(string toolId, string? version = null);
}
