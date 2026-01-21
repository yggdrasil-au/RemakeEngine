using System.Collections.Generic;
using EngineNet.Core.Abstractions;
using EngineNet.Core.Utils;

namespace EngineNet.Core.Services;

public class GameRegistry : IGameRegistry {
    private readonly ModuleScanner _scanner;
    private readonly Registries _registries;
    private readonly string _rootPath;

    public GameRegistry(string rootPath) {
        _rootPath = rootPath;
        _registries = new Registries();
        _scanner = new ModuleScanner(_registries);
    }

    Dictionary<string, GameModuleInfo> IGameRegistry.GetModules(ModuleFilter filter) {
        return _scanner.Modules(filter);
    }

    Dictionary<string, GameInfo> IGameRegistry.GetBuiltGames() {
        return _registries.DiscoverBuiltGames();
    }

    public string? GetGameExecutable(string name) {
        return _registries.DiscoverBuiltGames().TryGetValue(name, out GameInfo? gi) ? gi.ExePath : null;
    }

    public string? GetGamePath(string name) {
        // Prefer installed location first, then fall back to downloaded location
        if (_registries.DiscoverBuiltGames().TryGetValue(name, out GameInfo? gi))
            return gi.GameRoot;
        string dir = System.IO.Path.Combine(_rootPath, "EngineApps", "Games", name);
        return System.IO.Directory.Exists(dir) ? dir : null;
    }

    IReadOnlyDictionary<string, object?> IGameRegistry.GetRegisteredModules() {
        return _registries.GetRegisteredModules();
    }

    void IGameRegistry.RefreshModules() {
        _registries.RefreshModules();
    }
}
