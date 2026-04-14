namespace EngineNet.Core.ExternalTools;

internal sealed class RegistryChecksums {
    public string Source { get; set; } = string.Empty;
}

internal sealed class RegistryPlatformData {
    public string Url { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public string? ExeName { get; set; }
}

internal sealed class RegistryToolVersion {
    public RegistryChecksums? Checksums { get; set; }

    public Dictionary<string, RegistryPlatformData> Platforms { get; } = new Dictionary<string, RegistryPlatformData>(System.StringComparer.OrdinalIgnoreCase);
}
