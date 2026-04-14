namespace EngineNet.Core.ExternalTools;

internal sealed record ToolManifestEntry(
    string Name,
    string Version,
    bool Unpack,
    bool HasDeprecatedDestination
);
