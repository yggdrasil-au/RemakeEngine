using EngineNet.Core.Data;

namespace EngineNet.Core.Utils.GameRegistry;

/// <summary>
/// Provides a registry for discovering and managing game information within the engine.
/// This class is responsible for locating game modules, built games, and their associated files.
/// </summary>
public sealed class GameRegistry {
    private readonly Core.Utils.ModuleScanner _scanner;
    private readonly Core.Utils.GameRegistry.Registries _registries;
    private readonly string _rootPath = EngineNet.Shared.State.RootPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameRegistry"/> class.
    /// </summary>
    internal GameRegistry(Core.Utils.GameRegistry.Registries registries, Core.Utils.ModuleScanner scanner) {
        _registries = registries;
        _scanner = scanner;
    }

    public Core.Data.GameModules GetModules(Core.Data.ModuleFilter filter) {
        return _scanner.Modules(filter: filter);
    }

    /// <summary>
    /// Gets the full path to the executable for a specified game.
    /// </summary>
    /// <param name="name">The name of the game.</param>
    /// <returns>The full path to the game's executable if found; otherwise, null.</returns>
    internal string? GetGameExecutable(string name) {
        return _registries.DiscoverBuiltGames().TryGetValue(key: name, out GameInfo? gi) ? gi.ExePath : null;
    }

    /// <summary>
    /// Gets the root directory path for a specified game.
    /// It prioritizes the installed game location and falls back to the downloaded game assets location.
    /// </summary>
    /// <param name="name">The name of the game.</param>
    /// <returns>The root directory path of the game if found; otherwise, null.</returns>
    public string? GetGamePath(string name) {
        // Prefer installed location first, then fall back to downloaded location
        if (_registries.DiscoverBuiltGames().TryGetValue(key: name, out GameInfo? gi))
            return gi.GameRoot;
        string dir = System.IO.Path.Combine(path1: _rootPath, path2: "EngineApps", path3: "Games", path4: name);
        return System.IO.Directory.Exists(path: dir) ? dir : null;
    }

    public IReadOnlyDictionary<string, object?> GetRegisteredModules() {
        return _registries.GetRegisteredModules();
    }

    public void RefreshModules() {
        _registries.RefreshModules();
    }
}
