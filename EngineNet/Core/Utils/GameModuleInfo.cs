namespace EngineNet.Core.Utils;

/// <summary>
/// Represents a game module and its discovery/verification status.
/// </summary>
internal sealed class GameModuleInfo {
    public string Name { get; set; } = string.Empty;
    public string GameRoot { get; set; } = string.Empty;
    public string? OpsFile { get; set; }
    public string? ExePath { get; set; }
    public string? Title { get; set; }

    public bool IsRegistered { get; set; }
    public bool IsInstalled { get; set; }
    public bool IsBuilt { get; set; }
    public bool IsUnverified { get; set; }

    public string DescribeState() {
        System.Collections.Generic.List<string> states = new System.Collections.Generic.List<string>();
        if (IsRegistered) states.Add("registered");
        if (IsInstalled) states.Add("installed");
        if (IsBuilt) states.Add("built");
        if (IsUnverified) states.Add("unverified");
        if (!IsInstalled && IsRegistered) states.Add("uninstalled");
        if (IsInstalled && !IsBuilt) states.Add("unbuilt");
        return string.Join(", ", states);
    }
}

