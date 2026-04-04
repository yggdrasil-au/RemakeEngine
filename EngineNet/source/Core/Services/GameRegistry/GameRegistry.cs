using EngineNet.Core.Data;

namespace EngineNet.Core.Services;

/// <summary>
/// Provides a registry for discovering and managing game information within the engine.
/// This class is responsible for locating game modules, built games, and their associated files.
/// </summary>
internal class GameRegistry {
    private readonly Core.Utils.ModuleScanner _scanner;
    internal readonly Core.Utils.Registries _registries;
    private readonly string _rootPath = EngineNet.Core.Main.RootPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameRegistry"/> class.
    /// </summary>
    internal GameRegistry(Core.Utils.Registries registries, Core.Utils.ModuleScanner scanner) {
        _registries = registries;
        _scanner = scanner;
    }

    internal Dictionary<string, Core.Data.GameModuleInfo> GetModules(Core.Data.ModuleFilter filter) {
        return _scanner.Modules(filter);
    }

    /// <summary>
    /// Gets the full path to the executable for a specified game.
    /// </summary>
    /// <param name="name">The name of the game.</param>
    /// <returns>The full path to the game's executable if found; otherwise, null.</returns>
    internal string? GetGameExecutable(string name) {
        return _registries.DiscoverBuiltGames().TryGetValue(name, out GameInfo? gi) ? gi.ExePath : null;
    }

    /// <summary>
    /// Gets the root directory path for a specified game.
    /// It prioritizes the installed game location and falls back to the downloaded game assets location.
    /// </summary>
    /// <param name="name">The name of the game.</param>
    /// <returns>The root directory path of the game if found; otherwise, null.</returns>
    internal string? GetGamePath(string name) {
        // Prefer installed location first, then fall back to downloaded location
        if (_registries.DiscoverBuiltGames().TryGetValue(name, out GameInfo? gi))
            return gi.GameRoot;
        string dir = System.IO.Path.Combine(_rootPath, "EngineApps", "Games", name);
        return System.IO.Directory.Exists(dir) ? dir : null;
    }

    internal IReadOnlyDictionary<string, object?> GetRegisteredModules() {
        return _registries.GetRegisteredModules();
    }

    internal void RefreshModules() {
        _registries.RefreshModules();
    }
}
