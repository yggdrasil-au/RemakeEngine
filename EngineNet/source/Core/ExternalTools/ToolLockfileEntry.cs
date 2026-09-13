using System.Text.Json.Serialization;

namespace EngineNet.Core.ExternalTools;

internal sealed class ToolLockfileEntry {
    [JsonPropertyName(name: "version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName(name: "platform")]
    public string Platform { get; set; } = string.Empty;

    [JsonPropertyName(name: "install_path")]
    public string InstallPath { get; set; } = string.Empty;

    [JsonPropertyName(name: "exe")]
    public string? Exe { get; set; }

    [JsonPropertyName(name: "sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName(name: "source_url")]
    public string SourceUrl { get; set; } = string.Empty;
}
