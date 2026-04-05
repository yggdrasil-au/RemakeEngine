namespace EngineNet.Core.Data;

/// <summary>
/// Represents a game module and its discovery/verification status.
/// </summary>
public sealed class GameModuleInfo {
    public required string Id { get; set; }
    public required string Name { get; init; }
    public required string GameRoot { get; set; }
    public required string OpsFile { get; set; }
    public required string ExePath { get; set; }
    public required string Title { get; set; }
    public required string Url { get; set; }
    public bool IsRegistered { get; init; }
    public bool IsInstalled { get; set; }
    public bool IsBuilt { get; set; }
    public bool IsUnverified { get; set; }
    public bool IsInternal { get; init; }

    public string DescribeState() {
        if (this.IsInternal) return "internal";
        System.Collections.Generic.List<string> states = new System.Collections.Generic.List<string>();
        if (this.IsRegistered) states.Add("registered");
        if (this.IsInstalled) states.Add("installed");
        if (this.IsBuilt) states.Add("built");
        if (this.IsUnverified) states.Add("unverified");
        if (!this.IsInstalled && this.IsRegistered) states.Add("uninstalled");
        if (this.IsInstalled && !this.IsBuilt) states.Add("unbuilt");
        return string.Join(", ", states);
    }
}

/// <summary>
/// A case-insensitive collection of game modules.
/// to replace the previous Dictionary*string, GameModuleInfo> with a more specific type that enforces case-insensitivity and provides better semantics for handling game modules.
/// </summary>
public sealed class GameModules : Dictionary<string, GameModuleInfo> {
    // Default constructor now automatically handles the Case-Insensitivity
    // used in moduleScanner
    public GameModules() : base(StringComparer.OrdinalIgnoreCase) { }

    // Allow passing an existing collection, used in TUI
    public GameModules(IDictionary<string, GameModuleInfo> dictionary) : base(dictionary, StringComparer.OrdinalIgnoreCase) { }
}

