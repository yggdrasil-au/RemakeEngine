
using EngineNet.Core.Data;

namespace EngineNet.Core.Utils;


/// <summary>
/// The Registries class is responsible for managing the discovery and retrieval of game and module information from the file system.
/// It provides methods to discover games based on the presence of operations files and to retrieve registered modules from a JSON registry.
/// The class is designed to allow dynamic refreshing of the modules registry at runtime, while the games registry is read from disk on each access to ensure it reflects the current state of installed/downloaded games without requiring manual refreshing.
/// This design allows for flexible and up-to-date access to game and module information for use in various engine functionalities such as game launching, operations execution, and UI display.
/// </summary>
public sealed partial class Registries {

    private readonly string _gamesRegistryPath;
    private readonly string _modulesRegistryPath;

    private Dictionary<string, object?> _modules = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);

    private Registries(string gamesRel, string modulesRel) {
        _gamesRegistryPath = gamesRel;
        _modulesRegistryPath = modulesRel;
    }

    /// <summary>
    /// creates and initializes a new instance of the <see cref="Registries"/> class, ensuring that the modules registry file is present and loaded.
    /// </summary>
    /// <returns></returns>
    public static async Task<Registries> CreateAsync() {
        string Module_registry = System.IO.Path.Combine("EngineApps", "Registries", "Modules", "Main.json");

        // Preferred locations (relative to working root)
        string gamesRel = System.IO.Path.Combine(EngineNet.Core.Main.RootPath, "EngineApps", "Games");
        string modulesRel = System.IO.Path.Combine(EngineNet.Core.Main.RootPath, Module_registry);

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(modulesRel) ?? EngineNet.Core.Main.RootPath);

        if (!System.IO.File.Exists(modulesRel)) {
            await Core.ExternalTools.RemoteFallbacks.EnsureRepoFileAsync(Module_registry, modulesRel);
        }

        var instance = new Registries(gamesRel, modulesRel);
        instance._modules = Core.Serialization.Json.JsonHelpers.LoadJsonFile(instance._modulesRegistryPath);
        return instance;
    }

    /// <summary>
    /// Refreshes the modules registry by reloading the JSON file from disk. This allows dynamic updates to the available modules without restarting the engine.
    /// The games registry is not cached in memory, so it does not require refreshing - it is read from disk on each access to ensure it reflects the current state of installed/downloaded games.
    /// This design choice prioritizes accuracy for game discovery while allowing efficient caching for module information that is less likely to change frequently.
    /// </summary>
    public void RefreshModules() => _modules = Core.Serialization.Json.JsonHelpers.LoadJsonFile(_modulesRegistryPath);

    /// <summary>
    /// Gets the registered modules from the modules registry. The returned dictionary is case-insensitive for module names.
    /// The modules registry is expected to have a top-level "modules" key containing a dictionary of module information.
    /// If the "modules" key is missing or not a dictionary, an empty dictionary is returned.
    /// This method provides a centralized way to access module information for use in operations, game launching, and other engine functionalities.
    /// The modules registry can be refreshed at runtime using the RefreshModules method to reflect any changes made to the registry file on disk.
    /// </summary>
    /// <returns></returns>
    public IReadOnlyDictionary<string, object?> GetRegisteredModules() {
        return _modules.TryGetValue("modules", out object? m) && m is Dictionary<string, object?> dict ? dict : new Dictionary<string, object?>();
    }

    /// <summary>
    /// Discovers games by scanning the games registry directory for valid game entries. A valid game entry must contain an operations.toml or operations.json file.
    /// This method does not require a game.toml or executable entry point - it is used for discovering both installed games (with game.toml) and downloaded game assets (without game.toml).
    /// For installed games, the DiscoverBuiltGames method should be used instead, which requires a valid game.toml with an executable entry point.
    /// The returned dictionary is case-insensitive for game names. The GameInfo objects contain the path to the operations file and the game root directory, which can be used for further processing and resolution of entry points.
    /// This method is designed to provide a comprehensive view of all available games in the registry, regardless of their installation status, while DiscoverBuiltGames focuses on fully installed games with known executables.
    /// The games registry is read from disk on each call to ensure it reflects the current state of installed/downloaded games without requiring manual refreshing.
    /// This allows for dynamic discovery of new games added to the registry directory at runtime.
    /// Note: This method does not validate the contents of the operations files or the presence of executable entry points - it only checks for the existence of the operations.toml or operations.json file to consider a directory as a valid game entry.
    /// </summary>
    /// <returns></returns>
    public Dictionary<string, GameInfo> DiscoverGames() {
        Dictionary<string, GameInfo> games = new Dictionary<string, GameInfo>(System.StringComparer.OrdinalIgnoreCase);
        if (!System.IO.Directory.Exists(_gamesRegistryPath)) {
            return games;
        }

        foreach (string dir in System.IO.Directory.EnumerateDirectories(_gamesRegistryPath)) {
            string opsToml = System.IO.Path.Combine(dir, "operations.toml");
            string opsJson = System.IO.Path.Combine(dir, "operations.json");
            string? ops = null;
            if (System.IO.File.Exists(opsToml)) {
                ops = opsToml;
            } else if (System.IO.File.Exists(opsJson)) {
                ops = opsJson;
            }

            if (ops is null) {
                continue;
            }

            string name = new System.IO.DirectoryInfo(dir).Name;
            games[name] = new GameInfo(
                opsFile: System.IO.Path.GetFullPath(ops),
                gameRoot: System.IO.Path.GetFullPath(dir)
            );
        }
        return games;
    }

    /// <summary>
    /// Discovers built games by scanning the games registry directory for valid game entries.
    /// A valid game entry must contain a game.toml with at least one valid executable entry (exe, lua script, or godot project).
    /// </summary>
    /// <returns></returns>
    public Dictionary<string, GameInfo> DiscoverBuiltGames() {
        Dictionary<string, GameInfo> games = new Dictionary<string, GameInfo>(System.StringComparer.OrdinalIgnoreCase);
        if (!System.IO.Directory.Exists(_gamesRegistryPath)) {
            return games;
        }

        foreach (string dir in System.IO.Directory.EnumerateDirectories(_gamesRegistryPath)) {
            string opsToml = System.IO.Path.Combine(dir, "operations.toml");
            string opsJson = System.IO.Path.Combine(dir, "operations.json");
            string? ops = null;
            if (System.IO.File.Exists(opsToml)) {
                ops = opsToml;
            } else if (System.IO.File.Exists(opsJson)) {
                ops = opsJson;
            }

            if (ops is null) {
                continue;
            }

            string gameToml = System.IO.Path.Combine(dir, "game.toml");
            if (!System.IO.File.Exists(gameToml)) {
                Core.Diagnostics.Trace($"[GameRegistry] warning: game '{new System.IO.DirectoryInfo(dir).Name}' is missing game.toml - skipping");
                continue; // not installed - requires a valid game.toml
            }

            // Parse a minimal subset of TOML: top-level key = "value" pairs
            string? exePath = null;
            string? luaPath = null;
            string? godotPath = null;
            string? title = null;
            try {
                foreach (string raw in System.IO.File.ReadAllLines(gameToml)) {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) {
                        continue;
                    }
                    // ignore tables/arrays
                    if (line.StartsWith("[") && line.EndsWith("]")) {
                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (eq <= 0) {
                        continue;
                    }

                    string key = line.Substring(0, eq).Trim();
                    string valRaw = line.Substring(eq + 1).Trim();
                    string? val = valRaw.StartsWith("\"") && valRaw.EndsWith("\"") ? valRaw.Substring(1, valRaw.Length - 2) : valRaw;

                    if (key.Equals("exe", System.StringComparison.OrdinalIgnoreCase) || key.Equals("executable", System.StringComparison.OrdinalIgnoreCase)) {
                        exePath = val;
                    } else if (key.Equals("lua", System.StringComparison.OrdinalIgnoreCase) || key.Equals("lua_script", System.StringComparison.OrdinalIgnoreCase) || key.Equals("script", System.StringComparison.OrdinalIgnoreCase)) {
                        luaPath = val;
                    } else if (key.Equals("godot", System.StringComparison.OrdinalIgnoreCase) || key.Equals("godot_project", System.StringComparison.OrdinalIgnoreCase) || key.Equals("project", System.StringComparison.OrdinalIgnoreCase)) {
                        godotPath = val;
                    } else if (key.Equals("title", System.StringComparison.OrdinalIgnoreCase) || key.Equals("name", System.StringComparison.OrdinalIgnoreCase)) {
                        title = val;
                    }
                }
            } catch {
                Core.Diagnostics.Bug($"[GameRegistry] err parsing game.toml for game '{new System.IO.DirectoryInfo(dir).Name}' - skipping");
                // malformed game.toml - reject
                continue;
            }

            // Prepare resolution context
            var ctx = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase) {
                ["Game_Root"] = dir,
                ["Project_Root"] = EngineNet.Core.Main.RootPath
            };

            // Resolve and validate entry points (Priority: EXE > Lua > Godot)
            string? finalEntryPoint = null;
            if (!string.IsNullOrWhiteSpace(exePath)) {
                string resolved = Placeholders.Resolve(exePath, ctx)?.ToString() ?? exePath;
                string full = PathHelper.ResolveRelativePath(dir, resolved);
                if (System.IO.File.Exists(full)) finalEntryPoint = full;
            }
            if (finalEntryPoint == null && !string.IsNullOrWhiteSpace(luaPath)) {
                string resolved = Placeholders.Resolve(luaPath, ctx)?.ToString() ?? luaPath;
                string full = PathHelper.ResolveRelativePath(dir, resolved);
                if (System.IO.File.Exists(full)) finalEntryPoint = full;
            }
            if (finalEntryPoint == null && !string.IsNullOrWhiteSpace(godotPath)) {
                string resolved = Placeholders.Resolve(godotPath, ctx)?.ToString() ?? godotPath;
                string full = PathHelper.ResolveRelativePath(dir, resolved);
                // Godot project can be a file or a directory (if it contains project.godot)
                if (System.IO.File.Exists(full) || System.IO.Directory.Exists(full)) finalEntryPoint = full;
            }

            if (string.IsNullOrWhiteSpace(finalEntryPoint)) {
                continue; // No valid runnable entry point found
            }

            title = Placeholders.Resolve(title, ctx)?.ToString();

            string name = new System.IO.DirectoryInfo(dir).Name;
            games[name] = new GameInfo(
                opsFile: System.IO.Path.GetFullPath(ops),
                gameRoot: System.IO.Path.GetFullPath(dir),
                exePath: System.IO.Path.GetFullPath(finalEntryPoint),
                title: title
            );
        }

        return games;
    }
}
