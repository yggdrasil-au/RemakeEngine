namespace EngineNet.Core;

public sealed partial class OperationsEngine {
    /// <summary>
    /// Lists all discovered games (both installed and not) and enriches them with install information (like exe and title) if available.
    /// </summary>
    /// <returns>
    /// A <code>Dictionary&lt;string, object?&gt;</code> where the key is the game's module name (string) and the value is another <code>Dictionary&lt;string, object?&gt;</code> containing game properties like 'game_root', 'ops_file', 'exe', and 'title'.
    /// </returns>
    public Dictionary<String, Object?> ListGames() {
        Dictionary<String, Object?> games = new Dictionary<String, Object?>();
        // Also look up installed games to enrich entries with exe/title when available
        Dictionary<String, Sys.GameInfo> installed = _registries.DiscoverBuiltGames();
        foreach (KeyValuePair<String, Sys.GameInfo> kv in _registries.DiscoverGames()) {
            Dictionary<String, Object?> info = new Dictionary<String, Object?> {
                ["game_root"] = kv.Value.GameRoot,
                ["ops_file"] = kv.Value.OpsFile
            };
            if (installed.TryGetValue(kv.Key, out Sys.GameInfo? gi)) {
                if (!String.IsNullOrWhiteSpace(gi.ExePath))
                    info["exe"] = gi.ExePath;
                if (!String.IsNullOrWhiteSpace(gi.Title))
                    info["title"] = gi.Title;
            }
            games[kv.Key] = info;
        }
        return games;
    }

    /// <summary>
    /// Gets a list of *only* the games that are currently installed/Built.
    /// </summary>
    /// <returns>
    /// A <code>Dictionary&lt;string, object?&gt;</code> mapping module names (string) to a property dictionary (<code>Dictionary&lt;string, object?&gt;</code>). The property dictionary contains details for the installed/built game, such as 'game_root', 'ops_file', 'exe', and 'title'.
    /// </returns>
    public Dictionary<String, Object?> GetBuiltGames() {
        Dictionary<String, Object?> games = new Dictionary<String, Object?>();
        foreach (KeyValuePair<String, Sys.GameInfo> kv in _registries.DiscoverBuiltGames()) {
            Dictionary<String, Object?> info = new Dictionary<String, Object?> {
                ["game_root"] = kv.Value.GameRoot,
                ["ops_file"] = kv.Value.OpsFile
            };
            if (!String.IsNullOrWhiteSpace(kv.Value.ExePath))
                info["exe"] = kv.Value.ExePath;
            if (!String.IsNullOrWhiteSpace(kv.Value.Title))
                info["title"] = kv.Value.Title;
            games[kv.Key] = info;
        }
        return games;
    }

    /// <summary>
    /// Gets a read-only dictionary of all modules registered with the engine's registries.
    /// </summary>
    /// <returns>
    /// An <code>IReadOnlyDictionary&lt;string, object?&gt;</code> where the key is the module name and the value is an object containing module metadata.
    /// </returns>
    public IReadOnlyDictionary<String, Object?> GetRegisteredModules() {
        return _registries.GetRegisteredModules();
    }

    /// <summary>
    /// Checks if a specific module is currently installed by querying the game registries.
    /// </summary>
    /// <param name="name">The module name (string) to check.</param>
    /// <returns>A <code>bool</code> (true) if the module is found in the list of installed games; otherwise, <code>false</code>.</returns>
    public Boolean IsModuleInstalled(String name) {
        Dictionary<String, Sys.GameInfo> games = _registries.DiscoverBuiltGames();
        return games.ContainsKey(name);
    }

    /// <summary>
    /// Gets the full file path to the executable for an installed game.
    /// </summary>
    /// <param name="name">The module name (string) of the game.</param>
    /// <returns>
    /// A <code>string?</code> representing the full path to the game's executable. 
    /// Returns <code>null</code> if the game is not found or has no executable path defined.
    /// </returns>
    public String? GetGameExecutable(String name) {
        Dictionary<String, Sys.GameInfo> games = _registries.DiscoverBuiltGames();
        return games.TryGetValue(name, out Sys.GameInfo? gi) ? gi.ExePath : null;
    }

    /// <summary>
    /// Gets the root directory path for a game.
    /// It prioritizes the installed game's location first, then falls back to the downloaded (but not yet installed) game directory.
    /// </summary>
    /// <param name="name">The module name (string) of the game.</param>
    /// <returns>
    /// A <code>string?</code> representing the path to the game's root directory. 
    /// Returns <code>null</code> if the game cannot be found in either the installed or downloaded locations.
    /// </returns>
    public String? GetGamePath(String name) {
        // Prefer installed location first, then fall back to downloaded location
        Dictionary<String, Sys.GameInfo> games = _registries.DiscoverBuiltGames();
        if (games.TryGetValue(name, out Sys.GameInfo? gi))
            return gi.GameRoot;
        String dir = Path.Combine(_rootPath, "RemakeRegistry", "Games", name);
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>
    /// Attempts to launch an installed game using its registered executable and game path as the working directory.
    /// </summary>
    /// <param name="name">The module name (string) of the game to launch.</param>
    /// <returns>
    /// A <code>bool</code> (true) if the game process was started successfully; 
    /// otherwise, <code>false</code> (e.g., if the executable is not found or an error occurs).
    /// </returns>
    public Boolean LaunchGame(String name) {
        String? exe = GetGameExecutable(name);
        String root = GetGamePath(name) ?? _rootPath;
        if (String.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            return false;

        String? launchOverride = Environment.GetEnvironmentVariable("ENGINE_NET_TEST_LAUNCH_OVERRIDE");
        if (!String.IsNullOrEmpty(launchOverride))
            return String.Equals(launchOverride, "success", StringComparison.OrdinalIgnoreCase);

        try {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
                FileName = exe!,
                WorkingDirectory = root!,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            return true;
        } catch {
            return false;
        }
    }

    /// <summary>
    /// Determines the installation state of a module based on its directory presence and installation status.
    /// </summary>
    /// <param name="name">The module name (string) to check.</param>
    /// <returns>
    /// A <code>string</code> indicating the state: "installed", "downloaded" (but not installed), or "not_downloaded".
    /// </returns>
    public String GetModuleState(String name) {
        String dir = Path.Combine(_rootPath, "RemakeRegistry", "Games", name);
        return !Directory.Exists(dir) ? "not_downloaded" : IsModuleInstalled(name) ? "installed" : "downloaded";
    }
}