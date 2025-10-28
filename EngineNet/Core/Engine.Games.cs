namespace EngineNet.Core;

internal sealed partial class OperationsEngine {
    /// <summary>
    /// Lists all discovered games (both installed and not) and enriches them with install information (like exe and title) if available.
    /// </summary>
    /// <returns>
    /// A <code>Dictionary&lt;string, object?&gt;</code> where the key is the game's module name (string) and the value is another <code>Dictionary&lt;string, object?&gt;</code> containing game properties like 'game_root', 'ops_file', 'exe', and 'title'.
    /// </returns>
    public Dictionary<string, object?> ListGames() {
        Dictionary<string, object?> games = new Dictionary<string, object?>();
        // Also look up installed games to enrich entries with exe/title when available
        Dictionary<string, Sys.GameInfo> installed = _registries.DiscoverBuiltGames();
        foreach (KeyValuePair<string, Sys.GameInfo> kv in _registries.DiscoverGames()) {
            Dictionary<string, object?> info = new Dictionary<string, object?> {
                ["game_root"] = kv.Value.GameRoot,
                ["ops_file"] = kv.Value.OpsFile
            };
            if (installed.TryGetValue(kv.Key, out Sys.GameInfo? gi)) {
                if (!string.IsNullOrWhiteSpace(gi.ExePath))
                    info["exe"] = gi.ExePath;
                if (!string.IsNullOrWhiteSpace(gi.Title))
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
    public Dictionary<string, object?> GetBuiltGames() {
        Dictionary<string, object?> games = new Dictionary<string, object?>();
        foreach (KeyValuePair<string, Sys.GameInfo> kv in _registries.DiscoverBuiltGames()) {
            Dictionary<string, object?> info = new Dictionary<string, object?> {
                ["game_root"] = kv.Value.GameRoot,
                ["ops_file"] = kv.Value.OpsFile
            };
            if (!string.IsNullOrWhiteSpace(kv.Value.ExePath))
                info["exe"] = kv.Value.ExePath;
            if (!string.IsNullOrWhiteSpace(kv.Value.Title))
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
    public IReadOnlyDictionary<string, object?> GetRegisteredModules() {
        return _registries.GetRegisteredModules();
    }

    /// <summary>
    /// Checks if a specific module is currently installed by querying the game registries.
    /// </summary>
    /// <param name="name">The module name (string) to check.</param>
    /// <returns>A <code>bool</code> (true) if the module is found in the list of installed games; otherwise, <code>false</code>.</returns>
    public bool IsModuleInstalled(string name) {
        Dictionary<string, Sys.GameInfo> games = _registries.DiscoverBuiltGames();
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
    public string? GetGameExecutable(string name) {
        Dictionary<string, Sys.GameInfo> games = _registries.DiscoverBuiltGames();
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
    public string? GetGamePath(string name) {
        // Prefer installed location first, then fall back to downloaded location
        Dictionary<string, Sys.GameInfo> games = _registries.DiscoverBuiltGames();
        if (games.TryGetValue(name, out Sys.GameInfo? gi))
            return gi.GameRoot;
        string dir = System.IO.Path.Combine(_rootPath, "RemakeRegistry", "Games", name);
        return System.IO.Directory.Exists(dir) ? dir : null;
    }

    /// <summary>
    /// Attempts to launch an installed game using its registered executable and game path as the working directory.
    /// </summary>
    /// <param name="name">The module name (string) of the game to launch.</param>
    /// <returns>
    /// A <code>bool</code> (true) if the game process was started successfully; 
    /// otherwise, <code>false</code> (e.g., if the executable is not found or an error occurs).
    /// </returns>
    public bool LaunchGame(string name) {
        string? exe = GetGameExecutable(name);
        string root = GetGamePath(name) ?? _rootPath;
        if (string.IsNullOrWhiteSpace(exe) || !System.IO.File.Exists(exe))
            return false;

        string? launchOverride = System.Environment.GetEnvironmentVariable("ENGINE_NET_TEST_LAUNCH_OVERRIDE");
        if (!string.IsNullOrEmpty(launchOverride))
            return string.Equals(launchOverride, "success", System.StringComparison.OrdinalIgnoreCase);

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
    public string GetModuleState(string name) {
        string dir = System.IO.Path.Combine(_rootPath, "RemakeRegistry", "Games", name);
        return !System.IO.Directory.Exists(dir) ? "not_downloaded" : IsModuleInstalled(name) ? "installed" : "downloaded";
    }
}