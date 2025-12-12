
using System.IO;

namespace EngineNet.Core.Utils;

/// <summary>
/// Represents a game module and its discovery/verification status.
/// </summary>
internal sealed class GameModuleInfo {
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string GameRoot { get; set; }
    public required string OpsFile { get; set; }
    public required string ExePath { get; set; }
    public required string Title { get; set; }
    public required string Url { get; set; }
    public bool IsRegistered { get; set; }
    public bool IsInstalled { get; set; }
    public bool IsBuilt { get; set; }
    public bool IsUnverified { get; set; }
    public bool IsInternal { get; set; }

    public string DescribeState() {
        if (IsInternal) return "internal";
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

