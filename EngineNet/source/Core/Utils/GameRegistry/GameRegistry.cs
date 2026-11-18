using EngineNet.Core.Data;

namespace EngineNet.Core.Utils.GameRegistry;

/// <summary>
/// Provides a registry for discovering and managing game information within the engine.
/// This class is responsible for locating game modules, built games, and their associated files.
/// </summary>
public sealed class GameRegistry : Core.Abstractions.IGameRegistry {
    private readonly Core.Utils.ModuleScanner _scanner;
    private readonly Core.Utils.GameRegistry.Registries _registries;
    private readonly string _rootPath = EngineNet.Shared.State.RootPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameRegistry"/> class.
    /// </summary>
    public GameRegistry(Core.Utils.GameRegistry.Registries registries, Core.Utils.ModuleScanner scanner) {
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
    public string? GetGameExecutable(string name) {
        return _registries.DiscoverBuiltGames().TryGetValue(key: name, out GameInfo? gi) ? gi.ExePath : null;
    }

    /// <summary>
    /// Gets the root directory path for a specified game.
    /// It prioritizes the installed game location and falls back to the downloaded game assets location.
    /// </summary>
    /// <param name="name">The name of the game.</param>
    /// <returns>The root directory path of the game if found; otherwise, null.</returns>
    public string? GetGamePath(string name) {
        // Prefer built game location first, then discovered/installed location, then registered path
        if (_registries.DiscoverBuiltGames().TryGetValue(key: name, out GameInfo? gi)) {
            return gi.GameRoot;
        }
        if (_registries.DiscoverGames().TryGetValue(key: name, out GameInfo? installedGi)) {
            return installedGi.GameRoot;
        }
        if (_scanner.Modules(filter: ModuleFilter.All).TryGetValue(key: name, out Data.GameModuleInfo? modInfo)
            && !string.IsNullOrWhiteSpace(modInfo.GameRoot)
            && System.IO.Directory.Exists(path: modInfo.GameRoot)) {
            return modInfo.GameRoot;
        }
        string dir = System.IO.Path.Combine(path1: _rootPath, path2: "EngineApps", path3: "Games", path4: name);
        return System.IO.Directory.Exists(path: dir) ? dir : null;
    }

    public IReadOnlyDictionary<string, object?> GetRegisteredModules() {
        return _registries.GetRegisteredModules();
    }

    public void RefreshModules() {
        _registries.RefreshModules();
    }

    /// <summary>
    /// Resolves target modules relative to <paramref name="currentModuleRoot"/> within the requested <paramref name="scope"/>
    /// (<c>submodules</c> or <c>all</c>). Resolves explicit module selectors by Registry Name, Registry Id, or Module Folder Name
    /// for unregistered modules, while enforcing duplicate valid module folder name detection.
    /// </summary>
    public IReadOnlyList<Core.Data.GameModuleInfo> ResolveModules(
        string currentModuleRoot,
        string scope = "submodules",
        IReadOnlyList<string>? requestedModules = null
    ) {
        string normalizedScope = string.IsNullOrWhiteSpace(value: scope)
            ? "submodules"
            : scope.Trim().ToLowerInvariant();
        if (!string.Equals(a: normalizedScope, b: "submodules", comparisonType: System.StringComparison.Ordinal)
            && !string.Equals(a: normalizedScope, b: "all", comparisonType: System.StringComparison.Ordinal)) {
            throw new System.ArgumentException(
                message: $"Invalid module scope '{scope}'. Expected 'submodules' or 'all'.",
                paramName: nameof(scope)
            );
        }

        Core.Data.GameModules scanned = _scanner.Modules(filter: ModuleFilter.All);
        List<Core.Data.GameModuleInfo> candidates = new(collection: scanned.Values.Where(predicate: m => !m.IsInternal));

        // Also scan directly under currentModuleRoot so nested or external module trees are checked for duplicate
        // folder names and discovered even if deeper than 5 levels under the global Games root.
        if (!string.IsNullOrWhiteSpace(value: currentModuleRoot) && System.IO.Directory.Exists(path: currentModuleRoot)) {
            Dictionary<string, Core.Data.GameInfo> localDiscovered = Registries.DiscoverGamesAt(gamesRoot: currentModuleRoot);

            string? currentOpsFile = Registries.FindOperationsFile(moduleDirectory: currentModuleRoot);
            if (currentOpsFile is not null) {
                string currentFolderName = new System.IO.DirectoryInfo(
                    path: Registries.NormalizeDirectoryPath(path: currentModuleRoot)
                ).Name;
                if (localDiscovered.TryGetValue(key: currentFolderName, out Core.Data.GameInfo? nestedSameName)
                    && !Registries.PathsEqual(left: nestedSameName.GameRoot, right: currentModuleRoot)) {
                    throw new System.InvalidOperationException(
                        $"Duplicate valid module folder name '{currentFolderName}' detected at '{currentModuleRoot}' and '{nestedSameName.GameRoot}'. Valid module directories must have unique folder names."
                    );
                }
            }

            foreach (KeyValuePair<string, Core.Data.GameInfo> kv in localDiscovered) {
                bool alreadyPresent = candidates.Any(
                    predicate: existing => Registries.PathsEqual(left: existing.GameRoot, right: kv.Value.GameRoot)
                );
                if (!alreadyPresent) {
                    candidates.Add(item: new Core.Data.GameModuleInfo {
                        Id = string.Empty,
                        Name = kv.Key,
                        Category = kv.Value.Category,
                        GameRoot = kv.Value.GameRoot,
                        OpsFile = kv.Value.OpsFile,
                        ExePath = string.Empty,
                        Title = string.Empty,
                        Url = string.Empty,
                        IsRegistered = false,
                        IsInstalled = true,
                        IsUnverified = true
                    });
                }
            }
        }

        // Verify no duplicate valid module folder names exist across the combined candidate pool.
        Dictionary<string, Core.Data.GameModuleInfo> folderIndex = new(comparer: System.StringComparer.OrdinalIgnoreCase);
        foreach (Core.Data.GameModuleInfo module in candidates) {
            if (string.IsNullOrWhiteSpace(value: module.GameRoot)
                || string.IsNullOrWhiteSpace(value: module.OpsFile)
                || !System.IO.File.Exists(path: module.OpsFile)) {
                continue;
            }

            string folderName = new System.IO.DirectoryInfo(path: Registries.NormalizeDirectoryPath(path: module.GameRoot)).Name;
            if (folderIndex.TryGetValue(key: folderName, out Core.Data.GameModuleInfo? existing)
                && !Registries.PathsEqual(left: existing.GameRoot, right: module.GameRoot)) {
                throw new System.InvalidOperationException(
                    $"Duplicate valid module folder name '{folderName}' detected at '{existing.GameRoot}' and '{module.GameRoot}'. Valid module directories must have unique folder names."
                );
            }
            folderIndex[key: folderName] = module;
        }

        List<Core.Data.GameModuleInfo> allOtherModules = candidates
            .Where(predicate: m => !Registries.PathsEqual(left: m.GameRoot, right: currentModuleRoot))
            .ToList();

        List<Core.Data.GameModuleInfo> scopedModules = string.Equals(
            a: normalizedScope,
            b: "submodules",
            comparisonType: System.StringComparison.Ordinal
        )
            ? allOtherModules.Where(predicate: m => Registries.IsSubPathOf(childDir: m.GameRoot, parentDir: currentModuleRoot)).ToList()
            : allOtherModules;

        if (requestedModules is null || requestedModules.Count == 0) {
            return scopedModules
                .Where(predicate: m => m.IsInstalled && !string.IsNullOrWhiteSpace(value: m.OpsFile) && System.IO.File.Exists(path: m.OpsFile))
                .OrderBy(keySelector: m => m.GameRoot, comparer: System.StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        List<Core.Data.GameModuleInfo> resolved = [];
        HashSet<string> seenRoots = new(
            comparer: System.OperatingSystem.IsWindows() ? System.StringComparer.OrdinalIgnoreCase : System.StringComparer.Ordinal
        );

        foreach (string rawSelector in requestedModules) {
            string selector = rawSelector.Trim();
            if (selector.Length == 0) {
                continue;
            }

            Core.Data.GameModuleInfo? match = FindModuleMatch(modules: scopedModules, selector: selector);
            if (match is null) {
                Core.Data.GameModuleInfo? outOfScopeMatch = FindModuleMatch(modules: allOtherModules, selector: selector);
                if (outOfScopeMatch is not null) {
                    throw new System.InvalidOperationException(
                        $"Module '{selector}' resolved to '{outOfScopeMatch.GameRoot}', which is outside scope '{normalizedScope}' for '{currentModuleRoot}'."
                    );
                }
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"Module '{selector}' could not be resolved by registry name, registry id, or module folder name within scope '{normalizedScope}'."
                );
            }

            if (string.IsNullOrWhiteSpace(value: match.OpsFile) || !System.IO.File.Exists(path: match.OpsFile)) {
                throw new System.InvalidOperationException(
                    $"Module '{selector}' ('{match.Name}') resolved to '{match.GameRoot}', which does not have a valid operations file."
                );
            }

            string normalizedRoot = Registries.NormalizeDirectoryPath(path: match.GameRoot);
            if (seenRoots.Add(item: normalizedRoot)) {
                resolved.Add(item: match);
            }
        }

        return resolved;
    }

    private static Core.Data.GameModuleInfo? FindModuleMatch(
        IReadOnlyList<Core.Data.GameModuleInfo> modules,
        string selector
    ) {
        // 1. Registered module name
        Core.Data.GameModuleInfo? byRegisteredName = modules.FirstOrDefault(
            predicate: m => m.IsRegistered && string.Equals(a: m.Name, b: selector, comparisonType: System.StringComparison.OrdinalIgnoreCase)
        );
        if (byRegisteredName is not null) {
            return byRegisteredName;
        }

        // 2. Registered module id (if present in registry)
        Core.Data.GameModuleInfo? byRegisteredId = modules.FirstOrDefault(
            predicate: m => m.IsRegistered
                && !string.IsNullOrWhiteSpace(value: m.Id)
                && string.Equals(a: m.Id, b: selector, comparisonType: System.StringComparison.OrdinalIgnoreCase)
        );
        if (byRegisteredId is not null) {
            return byRegisteredId;
        }

        // 3. Fallback to module folder name for unregistered/unknown modules (or exact Name match)
        return modules.FirstOrDefault(predicate: m => {
            if (string.Equals(a: m.Name, b: selector, comparisonType: System.StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
            if (string.IsNullOrWhiteSpace(value: m.GameRoot)) {
                return false;
            }
            string folderName = new System.IO.DirectoryInfo(path: Registries.NormalizeDirectoryPath(path: m.GameRoot)).Name;
            return string.Equals(a: folderName, b: selector, comparisonType: System.StringComparison.OrdinalIgnoreCase);
        });
    }
}
