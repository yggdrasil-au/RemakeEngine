using System;

namespace EngineNet.Core.Sys;

/// <summary>
/// Shared constants and simple types used across the engine runtime.
/// </summary>
public static class Types {
    /// <summary>
    /// Prefix used for structured JSON events emitted by child processes and in-process SDKs.
    /// Lines starting with this prefix are parsed as a single-line JSON payload.
    /// </summary>
    public const String RemakePrefix = "@@REMAKE@@ ";
}

/// <summary>
/// Describes a discovered game/module entry in the local registry.
/// </summary>
public sealed class GameInfo {
    /// <summary>
    /// Path to the operations file (toml or json) that describes actions for this game.
    /// </summary>
    public String OpsFile {
        get;
    }
    /// <summary>
    /// Root directory of the game/module on disk.
    /// </summary>
    public String GameRoot {
        get;
    }
    /// <summary>
    /// Optional absolute path to the game's executable, if known.
    /// </summary>
    public String? ExePath {
        get;
    }
    /// <summary>
    /// Optional human-friendly title.
    /// </summary>
    public String? Title {
        get;
    }

    /// <summary>
    /// Create a new <see cref="GameInfo"/>.
    /// </summary>
    /// <param name="opsFile">Path to the operations manifest.</param>
    /// <param name="gameRoot">Root directory for game assets.</param>
    /// <param name="exePath">Optional executable path.</param>
    /// <param name="title">Optional title.</param>
    public GameInfo(String opsFile, String gameRoot, String? exePath = null, String? title = null) {
        OpsFile = opsFile;
        GameRoot = gameRoot;
        ExePath = exePath;
        Title = title;
    }
}
