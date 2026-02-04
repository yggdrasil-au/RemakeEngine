namespace EngineNet.Core.Utils;

/// <summary>
/// Describes a discovered game/module entry in the local registry.
/// </summary>
internal sealed class GameInfo {
    /// <summary>
    /// Path to the operations file (toml or json) that describes actions for this game.
    /// </summary>
    internal string OpsFile {
        get;
    }
    /// <summary>
    /// Root directory of the game/module on disk.
    /// </summary>
    internal string GameRoot {
        get;
    }
    /// <summary>
    /// Optional absolute path to the game's executable, if known.
    /// </summary>
    internal string? ExePath {
        get;
    }
    /// <summary>
    /// Optional human-friendly title.
    /// </summary>
    internal string? Title {
        get;
    }

    /// <summary>
    /// Create a new <see cref="GameInfo"/>.
    /// </summary>
    /// <param name="opsFile">Path to the operations manifest.</param>
    /// <param name="gameRoot">Root directory for game assets.</param>
    /// <param name="exePath">Optional executable path.</param>
    /// <param name="title">Optional title.</param>
    internal GameInfo(string opsFile, string gameRoot, string? exePath = null, string? title = null) {
        OpsFile = opsFile;
        GameRoot = gameRoot;
        ExePath = exePath;
        Title = title;
    }
}
