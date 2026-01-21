using System;
using System.Collections.Generic;

namespace EngineNet.Core.Utils;

/// <summary>
/// Category / filter selector for Modules().
/// </summary>
internal enum ModuleFilter {
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
    Built

}

/// <summary>
/// Scans registry and file system to produce a consistent view of modules with status flags.
/// </summary>
internal sealed class ModuleScanner {

    private readonly Registries _registries;

    internal ModuleScanner(Registries registries) {
        _registries = registries;
    }

    /// <summary>
    /// New main entry point.
    ///
    /// Builds a full scan, then filters it by <paramref name="filter"/>.
    /// You pick whether you want a Dictionary&lt;string,GameModuleInfo&gt; (like ListModules did),
    /// or just a flat List&lt;GameModuleInfo&gt;.
    ///
    /// Examples:
    ///   // "only installed"
    ///   var installed = scanner.Modules(ModuleFilter.Installed, asDictionary:false);
    ///
    ///   // "only unverified, but keep dictionary behavior"
    ///   var unverifiedMap = scanner.Modules(ModuleFilter.Unverified, asDictionary:true);
    ///
    /// Why not just always return Dictionary? Because sometimes you might only care
    /// about iteration or serialization and don't want the name->info map.
    /// </summary>
    internal Dictionary<string, GameModuleInfo> Modules(ModuleFilter filter) {
        Core.Diagnostics.Trace($"[Core :: ModuleScanner.cs::Modules()] Scanning modules with filter {filter}");
        Dictionary<string, GameModuleInfo> all = ScanAllModules();
        IEnumerable<GameModuleInfo> filtered = FilterModules(all.Values, filter);

        // Re-key into a new dictionary in case filter removed some.
        Dictionary<string, GameModuleInfo> dict = new Dictionary<string, GameModuleInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (GameModuleInfo info in filtered) {
            dict[info.Name] = info;
            Core.Diagnostics.Trace($"[Core :: ModuleScanner.cs::Modules()] Including module: {info.Name} (State: {info.DescribeState()})");
        }
        return dict;
    }

    /// <summary>
    /// Fully generic overload.
    ///
    /// You provide:
    ///   - which modules you want (filter),
    ///   - how you want them projected/shaped (selector).
    ///
    /// This is how you get "any type to be returned", e.g.:
    ///
    ///   // Return only installed modules as a plain List&lt;GameModuleInfo&gt;
    ///   List<GameModuleInfo> installedList = scanner.Modules(
    ///         ModuleFilter.Installed,
    ///         src => new List<GameModuleInfo>(src));
    ///
    ///   // Return names of unverified modules
    ///   string[] unverifiedNames = scanner.Modules(
    ///         ModuleFilter.Unverified,
    ///         src => src.Where(m => m.IsUnverified).Select(m => m.Name).ToArray());
    ///
    ///   // Return dictionary identical to old ListModules()
    ///   var allDict = scanner.Modules(
    ///         ModuleFilter.All,
    ///         src => {
    ///             var map = new Dictionary<string,GameModuleInfo>(StringComparer.OrdinalIgnoreCase);
    ///             foreach (var m in src) map[m.Name] = m;
    ///             return map;
    ///         });
    ///
    /// No extra allocations unless you want them.
    /// </summary>
    internal T Modules<T>(ModuleFilter filter, Func<IEnumerable<GameModuleInfo>, T> selector) {
        if (selector == null)
            throw new ArgumentNullException(nameof(selector));

        Dictionary<string, GameModuleInfo> all = ScanAllModules();
        IEnumerable<GameModuleInfo> filtered = FilterModules(all.Values, filter);

        return selector(filtered);
    }

    // -------------------------------------------------
    // Core building blocks (reusable by all the above)
    // -------------------------------------------------

    /// <summary>
    /// Scans registry and file system (by delegating to Registries)
    /// and returns the superset of modules.
    /// </summary>
    private Dictionary<string, GameModuleInfo> ScanAllModules() {
        Dictionary<string, GameModuleInfo> result = new Dictionary<string, GameModuleInfo>(StringComparer.OrdinalIgnoreCase);

        // 1. Get all modules known to the central registry
        IReadOnlyDictionary<string, object?> registered = _registries.GetRegisteredModules();
        foreach (KeyValuePair<string, object?> kv in registered) {
            string name = kv.Key;
            object? value = kv.Value;

            // Extract metadata from registry entry
            string id = string.Empty;
            string path = string.Empty;
            string url = string.Empty;

            if (value is Dictionary<string, object?> moduleData) {
                if (moduleData.TryGetValue("id", out object? idObj) && idObj != null) {
                    id = idObj.ToString() ?? string.Empty;
                }
                if (moduleData.TryGetValue("path", out object? pathObj) && pathObj != null) {
                    path = pathObj.ToString() ?? string.Empty;
                }
                if (moduleData.TryGetValue("url", out object? urlObj) && urlObj != null) {
                    url = urlObj.ToString() ?? string.Empty;
                }
            }

            string GameRoot = !string.IsNullOrWhiteSpace(path) ? System.IO.Path.Combine(Program.rootPath, path) : string.Empty;

            // opts file could be .toml or .json;
            string opsFile = System.IO.Path.Combine(GameRoot, "operations.toml");
            if (!System.IO.File.Exists(opsFile)) {
                opsFile = System.IO.Path.Combine(GameRoot, "operations.json");
            } else if (!System.IO.File.Exists(opsFile)) {
                opsFile = string.Empty;
            }

            GameModuleInfo info = new GameModuleInfo {
                Id = id,
                Name = name,
                IsRegistered = true,
                GameRoot = GameRoot,
                OpsFile = opsFile,
                ExePath = string.Empty, // needs to be resolved from game.toml if exists (built only)
                Title = string.Empty, // needs to be resolved from game.toml if exists (built only)
                Url = url
            };
            result[name] = info;
        }

        // 2. Get all "Installed" modules (must have an ops file)
        Dictionary<string, GameInfo> installed = _registries.DiscoverGames();
        foreach (KeyValuePair<string, GameInfo> kv in installed) {
            string name = kv.Key;
            GameInfo gameInfo = kv.Value;
            GameModuleInfo info;
            // if not already present (i.e. registered), create a new entry
            if (!result.TryGetValue(name, out info!)) {
                Core.Diagnostics.Trace($"[ModuleScanner.cs::ScanAllModules()] Found unregistered but installed module: {name}");
                info = new GameModuleInfo {
                    Id = string.Empty, // unknown
                    Name = name, // from directory name
                    GameRoot = gameInfo.GameRoot,
                    OpsFile = gameInfo.OpsFile,
                    ExePath = string.Empty,     // to be filled below if (built) game.toml exists
                    Title = string.Empty,       // to be filled below if (built) game.toml exists
                    IsRegistered = false,       // default for unregistered
                    IsInstalled = true,         // default for unregistered
                    IsUnverified = true,        // default for unregistered
                    Url = string.Empty          // unknown
                };
                result[name] = info;
            }

            info.IsInstalled = true;
            info.GameRoot = gameInfo.GameRoot;
            info.OpsFile = gameInfo.OpsFile;
        }

        // 3. Get all "Built" modules (strict check: ops, game.toml, exe key, exe exists)
        Dictionary<string, GameInfo> built = _registries.DiscoverBuiltGames();
        foreach (KeyValuePair<string, GameInfo> kv in built) {
            string name = kv.Key;
            GameInfo gameInfo = kv.Value;

            // A "Built" module must also be "Installed", so it must be in the dictionary.
            // We just need to update it.
            if (result.TryGetValue(name, out GameModuleInfo? info)) {
                info.IsBuilt = true;
                info.ExePath = gameInfo.ExePath ?? string.Empty;
                info.Title = gameInfo.Title ?? string.Empty;
            }
            // If it's somehow not in the dictionary, that implies a logic error
            // in Registries (built should be a subset of installed),
            // but we'll be safe and just ignore it rather than crashing.
        }

        // 4. Final pass to set Unverified flag
        foreach (GameModuleInfo info in result.Values) {
            info.IsUnverified = info.IsInstalled && !info.IsRegistered;
        }

        // 5. Scan standalone operations in EngineApps/Registries/ops/
        ScanStandaloneOperations(result);

        return result;
    }

    private void ScanStandaloneOperations(Dictionary<string, GameModuleInfo> result) {
        try {
            string opsDir = System.IO.Path.Combine(Program.rootPath, "EngineApps", "Registries", "ops");
            if (!System.IO.Directory.Exists(opsDir)) return;

            string[] files = System.IO.Directory.GetFiles(opsDir, "*.toml");
            foreach (string file in files) {
                string name = System.IO.Path.GetFileNameWithoutExtension(file);

                if (!result.ContainsKey(name)) {
                    GameModuleInfo info = new GameModuleInfo {
                        Id = name,
                        Name = name,
                        IsRegistered = true,
                        IsInstalled = true,
                        IsInternal = true,
                        GameRoot = opsDir,
                        OpsFile = file,
                        ExePath = string.Empty,
                        Title = name,
                        Url = string.Empty
                    };
                    result[name] = info;
                    Core.Diagnostics.Trace($"[ModuleScanner.cs::ScanStandaloneOperations()] Found standalone module: {name}");
                }
            }
        } catch (Exception ex) {
            Core.Diagnostics.Bug($"[ModuleScanner.cs] Error scanning standalone ops: {ex.Message}");
        }
    }

    /// <summary>
    /// Filters a sequence of GameModuleInfo according to a ModuleFilter.
    /// </summary>
    private static IEnumerable<GameModuleInfo> FilterModules(IEnumerable<GameModuleInfo> source, ModuleFilter filter) {
        switch (filter) {
            case ModuleFilter.All: {
                return source;
            }
            case ModuleFilter.Installed: {
                return Only(source, m => m.IsInstalled);
            }
            case ModuleFilter.Unverified: {
                return Only(source, m => m.IsUnverified);
            }
            case ModuleFilter.Registered: {
                return Only(source, m => m.IsRegistered);
            }
            case ModuleFilter.Uninstalled: {
                return Only(source, m => m.IsRegistered && !m.IsInstalled);
            }
            case ModuleFilter.Built: {
                return Only(source, m => m.IsBuilt);
            }
            default: {
                // fallback to "All" if future enum values slip through
                return source;
            }
        }
    }

    /// <summary>
    /// Tiny helper so we don't re-enumerate too much / keep lambdas inline.
    /// Creates a lazy iterator with the predicate.
    /// </summary>
    private static IEnumerable<GameModuleInfo> Only(IEnumerable<GameModuleInfo> src, Func<GameModuleInfo, bool> pred) {
        foreach (GameModuleInfo m in src) {
            if (pred(m)) {
                yield return m;
            }
        }
    }

}