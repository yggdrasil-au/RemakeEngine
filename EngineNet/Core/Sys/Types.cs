
namespace EngineNet.Core.Sys;

/// <summary>
/// Shared constants and simple types used across the engine runtime.
/// </summary>
internal static class Types {
    /// <summary>
    /// Prefix used for structured JSON events emitted by child processes and in-process SDKs.
    /// Lines starting with this prefix are parsed as a single-line JSON payload.
    /// </summary>
    public const string RemakePrefix = "@@REMAKE@@ ";
}
