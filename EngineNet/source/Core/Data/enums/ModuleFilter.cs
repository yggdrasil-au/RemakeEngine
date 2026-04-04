
namespace EngineNet.Core.Data;

/// <summary>
/// Category / filter selector for Modules().
/// </summary>
public enum ModuleFilter {
    /// <summary>
    /// Everything we know about (installed, registered only, etc).
    /// </summary>
    All,

    /// <summary>
    /// Only modules that physically exist on disk and have an operations file.
    /// </summary>
    Installed,

    /// <summary>
    /// Modules that are in the registry but not installed (i.e., missing ops file).
    /// </summary>
    Uninstalled,

    /// <summary>
    /// Modules that are installed but not registered.
    /// </summary>
    Unverified,

    /// <summary>
    /// Only modules present in registry (whether or not they exist on disk).
    /// </summary>
    Registered,

    /// <summary>
    /// Modules that are "built" (per Registries.DiscoverBuiltGames: ops file, game.toml, valid exe key, and exe exists).
    /// </summary>
    Built,

    /// <summary>
    /// internal operations modules. these do not appear in any other filter including All.
    /// </summary>
    Internal
}
