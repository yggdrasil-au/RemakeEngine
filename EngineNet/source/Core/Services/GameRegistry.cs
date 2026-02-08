using System.Collections.Generic;
using EngineNet.Core.Abstractions;
using EngineNet.Core.Utils;

namespace EngineNet.Core.Services;

/// <summary>
/// Provides a registry for discovering and managing game information within the engine.
/// This class is responsible for locating game modules, built games, and their associated files.
/// </summary>
public class GameRegistry : IGameRegistry {
    private readonly ModuleScanner _scanner;
    private readonly Registries _registries;
    private readonly string _rootPath = Program.rootPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameRegistry"/> class.
    /// </summary>
    /// <param name="rootPath">The root path of the engine, used for resolving game and module locations.</param>
    public GameRegistry() {
        //_rootPath = rootPath;
        _registries = new Registries();
        _scanner = new ModuleScanner(_registries);
    }

    Dictionary<string, GameModuleInfo> IGameRegistry.GetModules(ModuleFilter filter) {
        return _scanner.Modules(filter);
    }

    Dictionary<string, GameInfo> IGameRegistry.GetBuiltGames() {
        return _registries.DiscoverBuiltGames();
    }

    /// <summary>
    /// Gets the full path to the executable for a specified game.
    /// </summary>
    /// <param name="name">The name of the game.</param>
    /// <returns>The full path to the game's executable if found; otherwise, null.</returns>
    public string? GetGameExecutable(string name) {
        return _registries.DiscoverBuiltGames().TryGetValue(name, out GameInfo? gi) ? gi.ExePath : null;
    }

    /// <summary>
    /// Gets the root directory path for a specified game.
    /// It prioritizes the installed game location and falls back to the downloaded game assets location.
    /// </summary>
    /// <param name="name">The name of the game.</param>
    /// <returns>The root directory path of the game if found; otherwise, null.</returns>
    public string? GetGamePath(string name) {
        // Prefer installed location first, then fall back to downloaded location
        if (_registries.DiscoverBuiltGames().TryGetValue(name, out GameInfo? gi))
            return gi.GameRoot;
        string dir = System.IO.Path.Combine(_rootPath, "EngineApps", "Games", name);
        return System.IO.Directory.Exists(dir) ? dir : null;
    }

    /// <inheritdoc />
    IReadOnlyDictionary<string, object?> IGameRegistry.GetRegisteredModules() {
        return _registries.GetRegisteredModules();
    }

    /// <inheritdoc />
    void IGameRegistry.RefreshModules() {
        _registries.RefreshModules();
    }
}
