
using EngineNet.Core.Data;

namespace EngineNet.Core.Utils.GameRegistry;


/// <summary>
/// The Registries class is responsible for managing the discovery and retrieval of game and module information from the file system.
/// It provides methods to discover games based on the presence of operations files and to retrieve registered modules from a JSON or JSONC registry.
/// The class is designed to allow dynamic refreshing of the modules registry at runtime, while the games registry is read from disk on each access to ensure it reflects the current state of installed/downloaded games without requiring manual refreshing.
/// This design allows for flexible and up-to-date access to game and module information for use in various engine functionalities such as game launching, operations execution, and UI display.
/// </summary>
public sealed class Registries {

    private readonly string _gamesRegistryPath;
    private readonly string _modulesRegistryPath;

    private Dictionary<string, object?> _modules = new(comparer: System.StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> _prunedDirectoryNames = new(comparer: System.StringComparer.OrdinalIgnoreCase) {
        "Source",
        "GameFiles",
        "Assets",
        "TMP",
        "operations",
        "scripts",
        "reversing",
        "research",
        "GodotProject",
        "docs",
        "tools",
        "bin",
        "obj",
        "node_modules"
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="Registries"/> class with explicit games and modules registry paths.
    /// </summary>
    /// <param name="gamesRel">Path to the games root directory.</param>
    /// <param name="modulesRel">Path to the modules registry JSON or JSONC file.</param>
    public Registries(string gamesRel, string modulesRel) {
        _gamesRegistryPath = gamesRel;
        _modulesRegistryPath = modulesRel;
        RefreshModules();
    }

    /// <summary>
    /// Creates and initializes a new instance of the <see cref="Registries"/> class using <see cref="EngineNet.Shared.State.RootPath"/>,
    /// ensuring that the modules registry file (<c>Main.jsonc</c>, with legacy fallback to <c>Main.json</c>) is present and loaded.
    /// </summary>
    /// <returns>An initialized <see cref="Registries"/> instance.</returns>
    public static Task<Registries> CreateAsync() {
        return CreateAsync(rootPath: EngineNet.Shared.State.RootPath);
    }

    /// <summary>
    /// Creates and initializes a new instance of the <see cref="Registries"/> class under the specified <paramref name="rootPath"/>,
    /// ensuring that the modules registry file (<c>Main.jsonc</c>, with legacy fallback to <c>Main.json</c>) is present and loaded.
    /// </summary>
    /// <param name="rootPath">Engine root directory containing <c>EngineApps</c>.</param>
    /// <returns>An initialized <see cref="Registries"/> instance.</returns>
    public static async Task<Registries> CreateAsync(string rootPath) {
        const string mainJsoncFileName = "Main.jsonc";
        const string mainJsonFileName = "Main.json";

        string effectiveRoot = string.IsNullOrWhiteSpace(rootPath) ? System.IO.Directory.GetCurrentDirectory() : rootPath;
        string mainJsoncRel = System.IO.Path.Combine(path1: "EngineApps", path2: "Registries", path3: "Modules", path4: mainJsoncFileName);
        string mainJsonRel = System.IO.Path.Combine(path1: "EngineApps", path2: "Registries", path3: "Modules", path4: mainJsonFileName);

        // Preferred locations (relative to rootPath)
        string gamesRel = System.IO.Path.Combine(path1: effectiveRoot, path2: "EngineApps", path3: "Games");
        string modulesJsoncPath = System.IO.Path.Combine(path1: effectiveRoot, path2: mainJsoncRel);
        string modulesJsonPath = System.IO.Path.Combine(path1: effectiveRoot, path2: mainJsonRel);

        System.IO.Directory.CreateDirectory(path: System.IO.Path.GetDirectoryName(path: modulesJsoncPath) ?? effectiveRoot);

        string resolvedModulesPath;
        if (Shared.IO.LongPathIO.FileExists(path: modulesJsoncPath)) {
            resolvedModulesPath = modulesJsoncPath;
        } else if (Shared.IO.LongPathIO.FileExists(path: modulesJsonPath)) {
            resolvedModulesPath = modulesJsonPath;
        } else if (await Core.ExternalTools.RemoteFallbacks.EnsureRepoFileAsync(repoRelativePath: mainJsoncRel, localPath: modulesJsoncPath)) {
            resolvedModulesPath = modulesJsoncPath;
        } else if (await Core.ExternalTools.RemoteFallbacks.EnsureRepoFileAsync(repoRelativePath: mainJsonRel, localPath: modulesJsonPath)) {
            resolvedModulesPath = modulesJsonPath;
        } else {
            resolvedModulesPath = modulesJsoncPath;
        }

        Registries instance = new(gamesRel: gamesRel, modulesRel: resolvedModulesPath);
        return instance;
    }

    /// <summary>
    /// Refreshes the modules registry by reloading the JSON or JSONC file from disk. This allows dynamic updates to the available modules without restarting the engine.
    /// When the configured path is absent on disk, sibling <c>.jsonc</c>/<c>.json</c> variants are also checked before returning an empty registry.
    /// The games registry is not cached in memory, so it does not require refreshing - it is read from disk on each access to ensure it reflects the current state of installed/downloaded games.
    /// This design choice prioritizes accuracy for game discovery while allowing efficient caching for module information that is less likely to change frequently.
    /// </summary>
    public void RefreshModules() {
        string? resolvedPath = ResolveModulesRegistryPath(configuredPath: _modulesRegistryPath);
        _modules = resolvedPath is not null
            ? Shared.Serialization.Json.JsonHelpers.LoadJsonFile(filePath: resolvedPath)
            : new Dictionary<string, object?>(comparer: System.StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveModulesRegistryPath(string configuredPath) {
        if (string.IsNullOrWhiteSpace(configuredPath)) {
            return null;
        }

        try {
            if (Shared.IO.LongPathIO.FileExists(path: configuredPath)) {
                return configuredPath;
            }

            string ext = System.IO.Path.GetExtension(path: configuredPath);
            if (ext.Equals(".json", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                string jsoncCandidate = System.IO.Path.ChangeExtension(path: configuredPath, extension: ".jsonc");
                if (Shared.IO.LongPathIO.FileExists(path: jsoncCandidate)) {
                    return jsoncCandidate;
                }
            } else if (ext.Equals(".jsonc", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                string jsonCandidate = System.IO.Path.ChangeExtension(path: configuredPath, extension: ".json");
                if (Shared.IO.LongPathIO.FileExists(path: jsonCandidate)) {
                    return jsonCandidate;
                }
            }
        } catch (System.ArgumentException ex) {
            Shared.IO.Diagnostics.Bug(message: $"[Registries.cs::ResolveModulesRegistryPath()]:: catch: Invalid modules registry path '{configuredPath}': {ex.Message}", ex: ex);
        } catch (System.NotSupportedException ex) {
            Shared.IO.Diagnostics.Bug(message: $"[Registries.cs::ResolveModulesRegistryPath()]:: catch: Unsupported modules registry path '{configuredPath}': {ex.Message}", ex: ex);
        } catch (System.IO.IOException ex) {
            Shared.IO.Diagnostics.Bug(message: $"[Registries.cs::ResolveModulesRegistryPath()]:: catch: IO error resolving modules registry path '{configuredPath}': {ex.Message}", ex: ex);
        }

        return null;
    }

    /// <summary>
    /// Resolves the operations manifest file inside a module directory, checking
    /// <c>operations.toml</c>, <c>operations.jsonc</c>, and <c>operations.json</c> in priority order.
    /// </summary>
    /// <param name="moduleDirectory">Path to the candidate module directory.</param>
    /// <returns>The path to the discovered operations file, or <see langword="null"/> when none exists.</returns>
    internal static string? FindOperationsFile(string moduleDirectory) {
        if (string.IsNullOrWhiteSpace(moduleDirectory)) {
            return null;
        }

        string opsToml = System.IO.Path.Combine(path1: moduleDirectory, path2: "operations.toml");
        if (Shared.IO.LongPathIO.FileExists(path: opsToml)) {
            return opsToml;
        }

        string opsJsonc = System.IO.Path.Combine(path1: moduleDirectory, path2: "operations.jsonc");
        if (Shared.IO.LongPathIO.FileExists(path: opsJsonc)) {
            return opsJsonc;
        }

        string opsJson = System.IO.Path.Combine(path1: moduleDirectory, path2: "operations.json");
        if (Shared.IO.LongPathIO.FileExists(path: opsJson)) {
            return opsJson;
        }

        return null;
    }

    /// <summary>
    /// Gets the registered modules from the modules registry. The returned dictionary is case-insensitive for module names.
    /// Supports both a flat dictionary of modules under "modules" and a categorized hierarchy where top-level keys under "modules" represent category names.
    /// Each returned module dictionary includes a "category" key indicating its resolved category.
    /// </summary>
    /// <returns>A case-insensitive dictionary mapping module names to their metadata dictionaries.</returns>
    public IReadOnlyDictionary<string, object?> GetRegisteredModules() {
        Dictionary<string, object?> flattened = new(comparer: System.StringComparer.OrdinalIgnoreCase);
        if (!_modules.TryGetValue(key: "modules", out object? m) || m is not Dictionary<string, object?> rootDict) {
            return flattened;
        }

        FlattenModulesHierarchy(source: rootDict, currentCategory: string.Empty, destination: flattened);
        return flattened;
    }

    private static void FlattenModulesHierarchy(
        Dictionary<string, object?> source,
        string currentCategory,
        Dictionary<string, object?> destination
    ) {
        foreach (KeyValuePair<string, object?> kv in source) {
            if (kv.Value is not Dictionary<string, object?> childDict) {
                continue;
            }

            if (IsModuleDefinition(candidate: childDict)) {
                Dictionary<string, object?> cloned = new(dictionary: childDict, comparer: System.StringComparer.OrdinalIgnoreCase);
                if (!cloned.TryGetValue(key: "category", out object? existingCat) || string.IsNullOrWhiteSpace(existingCat?.ToString())) {
                    cloned[key: "category"] = currentCategory;
                }
                destination[key: kv.Key] = cloned;

                // Support optional nested "submodules" or "modules" inside a parent module entry
                string subCategory = !string.IsNullOrWhiteSpace(currentCategory) ? currentCategory : kv.Key;
                if (childDict.TryGetValue(key: "submodules", out object? subObj) && subObj is Dictionary<string, object?> subDict) {
                    FlattenModulesHierarchy(source: subDict, currentCategory: subCategory, destination: destination);
                }
                if (childDict.TryGetValue(key: "modules", out object? nestedObj) && nestedObj is Dictionary<string, object?> nestedDict) {
                    FlattenModulesHierarchy(source: nestedDict, currentCategory: subCategory, destination: destination);
                }
            } else {
                // kv.Key is a category container (e.g. "SimpsonsGames", "Lego", "Uncategorized")
                FlattenModulesHierarchy(source: childDict, currentCategory: kv.Key, destination: destination);
            }
        }
    }

    private static bool IsModuleDefinition(Dictionary<string, object?> candidate) {
        return candidate.ContainsKey(key: "id")
            || candidate.ContainsKey(key: "path")
            || candidate.ContainsKey(key: "url");
    }

    /// <summary>
    /// Recursively enumerates candidate module directories under <paramref name="gamesRoot"/> up to <paramref name="maxDepth"/>,
    /// pruning dot-directories, hidden/system folders, and known module-internal asset/source folders.
    /// Continues into non-pruned subdirectories even when a parent directory is itself a valid module, enabling nested submodules.
    /// </summary>
    private static IEnumerable<(string DirPath, string OpsFile, string Category)> EnumerateModuleDirectories(string gamesRoot, int maxDepth = 4) {
        if (!System.IO.Directory.Exists(path: gamesRoot)) {
            yield break;
        }

        Queue<(string Dir, int Depth, string TopCategory)> queue = new();
        queue.Enqueue(item: (gamesRoot, 0, string.Empty));

        while (queue.Count > 0) {
            (string currentDir, int currentDepth, string topCategory) = queue.Dequeue();
            if (currentDepth >= maxDepth) {
                continue;
            }

            IEnumerable<string> subDirs;
            try {
                subDirs = System.IO.Directory.EnumerateDirectories(path: currentDir);
            } catch (System.IO.IOException ex) {
                Shared.IO.Diagnostics.Bug($"IO error enumerating directories in '{currentDir}': {ex.Message}");
                continue;
            } catch (System.UnauthorizedAccessException ex) {
                Shared.IO.Diagnostics.Bug($"Access denied enumerating directories in '{currentDir}': {ex.Message}");
                continue;
            }

            foreach (string subDir in subDirs) {
                System.IO.DirectoryInfo dirInfo = new(path: subDir);
                string dirName = dirInfo.Name;

                if (dirName.StartsWith('.')) {
                    continue;
                }

                try {
                    if ((dirInfo.Attributes & (System.IO.FileAttributes.Hidden | System.IO.FileAttributes.System)) != 0) {
                        continue;
                    }
                } catch (System.IO.IOException ex) {
                    Shared.IO.Diagnostics.Bug($"IO error reading attributes for '{subDir}': {ex.Message}");
                    continue;
                } catch (System.UnauthorizedAccessException ex) {
                    Shared.IO.Diagnostics.Bug($"Access denied reading attributes for '{subDir}': {ex.Message}");
                    continue;
                }

                if (_prunedDirectoryNames.Contains(item: dirName)) {
                    continue;
                }

                string? ops = FindOperationsFile(moduleDirectory: subDir);

                string inferredCategory = currentDepth == 0 ? string.Empty : topCategory;
                if (ops is not null) {
                    yield return (subDir, ops, inferredCategory);
                }

                string nextTopCategory = currentDepth == 0 ? dirName : topCategory;
                queue.Enqueue(item: (subDir, currentDepth + 1, nextTopCategory));
            }
        }
    }

    /// <summary>
    /// Discovers games by scanning the games registry directory for valid game entries. A valid game entry must contain an <c>operations.toml</c>, <c>operations.jsonc</c>, or <c>operations.json</c> file.
    /// This method does not require a game.toml or executable entry point - it is used for discovering both installed games (with game.toml) and downloaded game assets (without game.toml).
    /// For installed games, the DiscoverBuiltGames method should be used instead, which requires a valid game.toml with an executable entry point.
    /// The returned dictionary is case-insensitive for game names. The GameInfo objects contain the path to the operations file and the game root directory, which can be used for further processing and resolution of entry points.
    /// This method is designed to provide a comprehensive view of all available games in the registry, regardless of their installation status, while DiscoverBuiltGames focuses on fully installed games with known executables.
    /// The games registry is read from disk on each call to ensure it reflects the current state of installed/downloaded games without requiring manual refreshing.
    /// This allows for dynamic discovery of new games added to the registry directory at runtime.
    /// Note: This method does not validate the contents of the operations files or the presence of executable entry points - it only checks for the existence of the <c>operations.toml</c>, <c>operations.jsonc</c>, or <c>operations.json</c> file to consider a directory as a valid game entry.
    /// </summary>
    /// <returns></returns>
    public Dictionary<string, Core.Data.GameInfo> DiscoverGames() {
        Dictionary<string, Core.Data.GameInfo> games = new(comparer: System.StringComparer.OrdinalIgnoreCase);
        if (!System.IO.Directory.Exists(path: _gamesRegistryPath)) {
            return games;
        }

        foreach ((string dir, string ops, string category) in EnumerateModuleDirectories(gamesRoot: _gamesRegistryPath)) {
            string name = new System.IO.DirectoryInfo(path: dir).Name;
            games[key: name] = new GameInfo(
                opsFile: System.IO.Path.GetFullPath(path: ops),
                gameRoot: System.IO.Path.GetFullPath(path: dir),
                category: category
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
        Dictionary<string, GameInfo> games = new(comparer: System.StringComparer.OrdinalIgnoreCase);
        if (!System.IO.Directory.Exists(path: _gamesRegistryPath)) {
            return games;
        }

        foreach ((string dir, string ops, string category) in EnumerateModuleDirectories(gamesRoot: _gamesRegistryPath)) {
            string gameToml = System.IO.Path.Join(path1: dir, path2: "game.toml");
            if (!System.IO.File.Exists(path: gameToml)) {
                Shared.IO.Diagnostics.Trace($"warning: game '{new System.IO.DirectoryInfo(path: dir).Name}' is missing game.toml - skipping");
                continue; // not installed - requires a valid game.toml
            }

            // Parse a minimal subset of TOML: top-level key = "value" pairs
            string? exePath = null;
            string? luaPath = null;
            string? godotPath = null;
            string? title = null;
            try {
                foreach (string line in System.IO.File.ReadAllLines(path: gameToml).Select(selector: raw => raw.Trim())) {
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

                    string key = line.Substring(startIndex: 0, length: eq).Trim();
                    string valRaw = line.Substring(startIndex: eq + 1).Trim();

                    // Note: valRaw.Length >= 2 prevents an ArgumentOutOfRangeException if valRaw is exactly "\""
                    string val = valRaw.Length >= 2 && valRaw.StartsWith("\"") && valRaw.EndsWith("\"")
                        ? valRaw.Substring(startIndex: 1, length: valRaw.Length - 2)
                        : valRaw;

                    if (key.Equals("exe", comparisonType: System.StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("executable", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                        exePath = val;
                    } else if (key.Equals("lua", comparisonType: System.StringComparison.OrdinalIgnoreCase) ||
                            key.Equals("lua_script", comparisonType: System.StringComparison.OrdinalIgnoreCase) ||
                            key.Equals("script", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                        luaPath = val;
                    } else if (key.Equals("godot", comparisonType: System.StringComparison.OrdinalIgnoreCase) ||
                            key.Equals("godot_project", comparisonType: System.StringComparison.OrdinalIgnoreCase) ||
                            key.Equals("project", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                        godotPath = val;
                    } else if (key.Equals("title", comparisonType: System.StringComparison.OrdinalIgnoreCase) ||
                            key.Equals("name", comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                        title = val;
                    }
                }
            }
            catch (System.IO.IOException ex) {
                Shared.IO.Diagnostics.Bug($"IO error reading game.toml for '{new System.IO.DirectoryInfo(path: dir).Name}': {ex.Message} - skipping");
                continue;
            }
            catch (System.UnauthorizedAccessException ex) {
                Shared.IO.Diagnostics.Bug($"Access denied reading game.toml for '{new System.IO.DirectoryInfo(path: dir).Name}': {ex.Message} - skipping");
                continue;
            }
            catch (System.ArgumentOutOfRangeException ex) {
                Shared.IO.Diagnostics.Bug($"Malformed syntax in game.toml for '{new System.IO.DirectoryInfo(path: dir).Name}': {ex.Message} - skipping");
                continue;
            }

            // Prepare resolution context
            Dictionary<string, object?> ctx = new(comparer: System.StringComparer.OrdinalIgnoreCase) {
                [key: "Game_Root"] = dir,
                [key: "Project_Root"] = EngineNet.Shared.State.RootPath
            };

            // Resolve and validate entry points (Priority: EXE > Lua > Godot)
            string? finalEntryPoint = null;
            if (!string.IsNullOrWhiteSpace(exePath)) {
                string resolved = Placeholders.Resolve(exePath, context: ctx)?.ToString() ?? exePath;
                string full = PathHelper.ResolveRelativePath(root: dir, path: resolved);
                if (System.IO.File.Exists(path: full)) finalEntryPoint = full;
            }

            if (finalEntryPoint == null && !string.IsNullOrWhiteSpace(luaPath)) {
                string resolved = Placeholders.Resolve(luaPath, context: ctx)?.ToString() ?? luaPath;
                string full = PathHelper.ResolveRelativePath(root: dir, path: resolved);
                if (System.IO.File.Exists(path: full)) finalEntryPoint = full;
            }

            if (finalEntryPoint == null && !string.IsNullOrWhiteSpace(godotPath)) {
                string resolved = Placeholders.Resolve(godotPath, context: ctx)?.ToString() ?? godotPath;
                string full = PathHelper.ResolveRelativePath(root: dir, path: resolved);
                // Godot project can be a file or a directory (if it contains project.godot)
                if (System.IO.File.Exists(path: full) || System.IO.Directory.Exists(path: full)) finalEntryPoint = full;
            }

            if (string.IsNullOrWhiteSpace(finalEntryPoint)) {
                continue; // No valid runnable entry point found
            }

            title = Placeholders.Resolve(title, context: ctx)?.ToString();

            string name = new System.IO.DirectoryInfo(path: dir).Name;
            games[key: name] = new GameInfo(
                opsFile: System.IO.Path.GetFullPath(path: ops),
                gameRoot: System.IO.Path.GetFullPath(path: dir),
                exePath: System.IO.Path.GetFullPath(path: finalEntryPoint),
                title: title,
                category: category
            );
        }

        return games;
    }
}
