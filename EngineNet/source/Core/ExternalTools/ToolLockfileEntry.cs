using System.Text.Json.Serialization;

namespace EngineNet.Core.ExternalTools;

internal sealed class ToolLockfileEntry {
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;

    [JsonPropertyName("install_path")]
    public string InstallPath { get; set; } = string.Empty;

    [JsonPropertyName("exe")]
    public string? Exe { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("source_url")]
    public string SourceUrl { get; set; } = string.Empty;
}
