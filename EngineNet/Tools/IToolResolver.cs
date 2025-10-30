
namespace EngineNet.Tools;

/// <summary>
/// Provides paths to external command-line tools required by actions.
/// </summary>
internal interface IToolResolver {
    /// <summary>
    /// Resolve the absolute path to a registered tool.
    /// </summary>
    /// <param name="toolId">Logical identifier of the tool (e.g. "ffmpeg").</param>
    /// <returns>Absolute filesystem path to the tool executable.</returns>
    string ResolveToolPath(string toolId);
}
