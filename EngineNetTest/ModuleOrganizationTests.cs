using EngineNet.Core.Data;
using EngineNet.Core.Utils;
using EngineNet.Core.Utils.GameRegistry;

namespace EngineNetTest;

[TestClass]
public sealed class ModuleOrganizationTests {

    [TestMethod]
    public void GetRegisteredModules_FlattensCategorizedAndFlatEntries() {
        string tempRoot = System.IO.Path.Combine(path1: System.IO.Path.GetTempPath(), path2: $"RemakeEngine_OrgTest_{System.Guid.NewGuid():N}");
        try {
            string gamesDir = System.IO.Path.Combine(path1: tempRoot, path2: "Games");
            System.IO.Directory.CreateDirectory(path: gamesDir);

            string mainJson = System.IO.Path.Combine(path1: tempRoot, path2: "Main.json");
            System.IO.File.WriteAllText(path: mainJson, contents: """
            {
              "modules": {
                "Uncategorized": {
                  "demo": {
                    "id": "480",
                    "path": "EngineApps/Games/demo",
                    "url": ""
                  }
                },
                "SimpsonsGames": {
                  "SimpsonsHitAndRun": {
                    "id": "2",
                    "path": "EngineApps/Games/SimpsonsHitAndRun",
                    "url": "https://github.com/example/SimpsonsHitAndRun.git"
                  }
                },
                "Lego": {
                  "Lego": {
                    "id": "10",
                    "path": "EngineApps/Games/Lego",
                    "url": "https://github.com/example/Lego.git"
                  },
                  "Lego_Harry_Potter": {
                    "id": "11",
                    "path": "EngineApps/Games/Lego/Games/Lego_Harry_Potter",
                    "url": ""
                  }
                },
                "LegacyFlatMod": {
                  "id": "999",
                  "path": "EngineApps/Games/LegacyFlatMod",
                  "url": ""
                }
              }
            }
            """);

            Registries registries = new(gamesRel: gamesDir, modulesRel: mainJson);
            IReadOnlyDictionary<string, object?> registered = registries.GetRegisteredModules();

            Assert.AreEqual(expected: 5, actual: registered.Count);
            Assert.IsTrue(condition: registered.ContainsKey(key: "demo"));
            Assert.IsTrue(condition: registered.ContainsKey(key: "SimpsonsHitAndRun"));
            Assert.IsTrue(condition: registered.ContainsKey(key: "Lego"));
            Assert.IsTrue(condition: registered.ContainsKey(key: "Lego_Harry_Potter"));
            Assert.IsTrue(condition: registered.ContainsKey(key: "LegacyFlatMod"));

            Dictionary<string, object?> shnr = (Dictionary<string, object?>)registered[key: "SimpsonsHitAndRun"]!;
            Assert.AreEqual(expected: "SimpsonsGames", actual: shnr[key: "category"]?.ToString());

            Dictionary<string, object?> lhp = (Dictionary<string, object?>)registered[key: "Lego_Harry_Potter"]!;
            Assert.AreEqual(expected: "Lego", actual: lhp[key: "category"]?.ToString());
        } finally {
            if (System.IO.Directory.Exists(path: tempRoot)) {
                System.IO.Directory.Delete(path: tempRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public void DiscoverGames_FindsFlatCategorizedAndSubmodules_WhilePruningInternalAndDotFolders() {
        string tempRoot = System.IO.Path.Combine(path1: System.IO.Path.GetTempPath(), path2: $"RemakeEngine_DiscTest_{System.Guid.NewGuid():N}");
        try {
            string gamesDir = System.IO.Path.Combine(path1: tempRoot, path2: "Games");
            System.IO.Directory.CreateDirectory(path: gamesDir);
            string mainJson = System.IO.Path.Combine(path1: tempRoot, path2: "Main.json");
            System.IO.File.WriteAllText(path: mainJson, contents: """{ "modules": {} }""");

            // 1. Flat module: Games/demo
            string demoDir = System.IO.Path.Combine(path1: gamesDir, path2: "demo");
            System.IO.Directory.CreateDirectory(path: demoDir);
            System.IO.File.WriteAllText(path: System.IO.Path.Combine(path1: demoDir, path2: "operations.toml"), contents: "title = \"demo\"");

            // 2. Category folder module: Games/SimpsonsGames/SimpsonsHitAndRun
            string shnrDir = System.IO.Path.Combine(path1: gamesDir, path2: "SimpsonsGames", path3: "SimpsonsHitAndRun");
            System.IO.Directory.CreateDirectory(path: shnrDir);
            System.IO.File.WriteAllText(path: System.IO.Path.Combine(path1: shnrDir, path2: "operations.toml"), contents: "title = \"SHAR\"");

            // 3. Parent module + nested submodule: Games/Lego and Games/Lego/Games/Lego_Harry_Potter
            string legoDir = System.IO.Path.Combine(path1: gamesDir, path2: "Lego");
            string lhpDir = System.IO.Path.Combine(path1: legoDir, path2: "Games", path3: "Lego_Harry_Potter");
            System.IO.Directory.CreateDirectory(path: lhpDir);
            System.IO.File.WriteAllText(path: System.IO.Path.Combine(path1: legoDir, path2: "operations.toml"), contents: "title = \"Lego\"");
            System.IO.File.WriteAllText(path: System.IO.Path.Combine(path1: lhpDir, path2: "operations.toml"), contents: "title = \"LHP\"");

            // 4. Pruned directories that must NOT be discovered: .backup and Source
            string backupDir = System.IO.Path.Combine(path1: demoDir, path2: ".backup");
            string sourceDir = System.IO.Path.Combine(path1: lhpDir, path2: "Source", path3: "FakeMod");
            System.IO.Directory.CreateDirectory(path: backupDir);
            System.IO.Directory.CreateDirectory(path: sourceDir);
            System.IO.File.WriteAllText(path: System.IO.Path.Combine(path1: backupDir, path2: "operations.toml"), contents: "title = \"backup\"");
            System.IO.File.WriteAllText(path: System.IO.Path.Combine(path1: sourceDir, path2: "operations.toml"), contents: "title = \"fake\"");

            Registries registries = new(gamesRel: gamesDir, modulesRel: mainJson);
            Dictionary<string, GameInfo> discovered = registries.DiscoverGames();

            Assert.AreEqual(expected: 4, actual: discovered.Count);
            Assert.IsTrue(condition: discovered.ContainsKey(key: "demo"));
            Assert.IsTrue(condition: discovered.ContainsKey(key: "SimpsonsHitAndRun"));
            Assert.IsTrue(condition: discovered.ContainsKey(key: "Lego"));
            Assert.IsTrue(condition: discovered.ContainsKey(key: "Lego_Harry_Potter"));
            Assert.IsFalse(condition: discovered.ContainsKey(key: ".backup"));
            Assert.IsFalse(condition: discovered.ContainsKey(key: "FakeMod"));

            Assert.AreEqual(expected: string.Empty, actual: discovered[key: "demo"].Category);
            Assert.AreEqual(expected: "SimpsonsGames", actual: discovered[key: "SimpsonsHitAndRun"].Category);
            Assert.AreEqual(expected: "Lego", actual: discovered[key: "Lego_Harry_Potter"].Category);
        } finally {
            if (System.IO.Directory.Exists(path: tempRoot)) {
                System.IO.Directory.Delete(path: tempRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public void ModuleScanner_MatchesMovedRegisteredModuleToActualDiskLocation() {
        string tempRoot = System.IO.Path.Combine(path1: System.IO.Path.GetTempPath(), path2: $"RemakeEngine_MoveTest_{System.Guid.NewGuid():N}");
        try {
            string gamesDir = System.IO.Path.Combine(path1: tempRoot, path2: "Games");
            System.IO.Directory.CreateDirectory(path: gamesDir);
            string mainJson = System.IO.Path.Combine(path1: tempRoot, path2: "Main.json");
            System.IO.File.WriteAllText(path: mainJson, contents: """
            {
              "modules": {
                "SimpsonsGames": {
                  "SimpsonsHitAndRun": {
                    "id": "2",
                    "path": "EngineApps/Games/SimpsonsHitAndRun",
                    "url": "https://github.com/example/SimpsonsHitAndRun.git"
                  }
                }
              }
            }
            """);

            // Module was manually moved into Games/SimpsonsGames/SimpsonsHitAndRun
            string movedDir = System.IO.Path.Combine(path1: gamesDir, path2: "SimpsonsGames", path3: "SimpsonsHitAndRun");
            System.IO.Directory.CreateDirectory(path: movedDir);
            string movedOps = System.IO.Path.Combine(path1: movedDir, path2: "operations.toml");
            System.IO.File.WriteAllText(path: movedOps, contents: "title = \"SHAR\"");

            Registries registries = new(gamesRel: gamesDir, modulesRel: mainJson);
            ModuleScanner scanner = new(registries: registries);
            GameRegistry gameRegistry = new(registries: registries, scanner: scanner);

            GameModules allModules = scanner.Modules(filter: ModuleFilter.All);
            Assert.IsTrue(condition: allModules.TryGetValue(key: "SimpsonsHitAndRun", out GameModuleInfo? info));
            Assert.IsNotNull(value: info);
            Assert.IsTrue(condition: info.IsRegistered);
            Assert.IsTrue(condition: info.IsInstalled);
            Assert.IsFalse(condition: info.IsUnverified);
            Assert.AreEqual(expected: "2", actual: info.Id);
            Assert.AreEqual(expected: "SimpsonsGames", actual: info.Category);
            Assert.AreEqual(expected: System.IO.Path.GetFullPath(path: movedDir), actual: info.GameRoot);
            Assert.AreEqual(expected: System.IO.Path.GetFullPath(path: movedOps), actual: info.OpsFile);
            Assert.AreEqual(expected: System.IO.Path.GetFullPath(path: movedDir), actual: gameRegistry.GetGamePath(name: "SimpsonsHitAndRun"));
        } finally {
            if (System.IO.Directory.Exists(path: tempRoot)) {
                System.IO.Directory.Delete(path: tempRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public void GetRegisteredModules_ParsesMainJsoncWithCommentsAndTrailingCommas() {
        string tempRoot = System.IO.Path.Combine(path1: System.IO.Path.GetTempPath(), path2: $"RemakeEngine_JsoncReg_{System.Guid.NewGuid():N}");
        try {
            string gamesDir = System.IO.Path.Combine(path1: tempRoot, path2: "Games");
            System.IO.Directory.CreateDirectory(path: gamesDir);

            string mainJsonc = System.IO.Path.Combine(path1: tempRoot, path2: "Main.jsonc");
            System.IO.File.WriteAllText(path: mainJsonc, contents: """
            {
              // Categorized modules catalog
              "modules": {
                "Demo": {
                  "demo": {
                    "id": "480",
                    "path": "EngineApps/Games/demo",
                  },
                },
                "Lego": {
                  "Lego_Batman": {
                    "id": "6",
                    "path": "EngineApps/Games/Lego/Games/Lego_Batman",
                    //"url": "https://github.com/Superposition28/Lego_Batman.git"
                  },
                  /* Active module */
                  "Lego_Harry_Potter": {
                    "id": "8",
                    "path": "EngineApps/Games/Lego/Games/Lego_Harry_Potter",
                    "url": "https://github.com/Superposition28/Lego_Harry_Potter.git",
                  },
                },
              },
            }
            """);

            Registries registries = new(gamesRel: gamesDir, modulesRel: mainJsonc);
            IReadOnlyDictionary<string, object?> registered = registries.GetRegisteredModules();

            Assert.AreEqual(expected: 3, actual: registered.Count);
            Assert.IsTrue(condition: registered.ContainsKey(key: "demo"));
            Assert.IsTrue(condition: registered.ContainsKey(key: "lego_batman"));
            Assert.IsTrue(condition: registered.ContainsKey(key: "Lego_Harry_Potter"));

            Dictionary<string, object?> batman = (Dictionary<string, object?>)registered[key: "Lego_Batman"]!;
            Assert.AreEqual(expected: "Lego", actual: batman[key: "category"]?.ToString());
            Assert.IsFalse(condition: batman.ContainsKey(key: "url"));
        } finally {
            if (System.IO.Directory.Exists(path: tempRoot)) {
                System.IO.Directory.Delete(path: tempRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task CreateAsync_PrefersMainJsoncAndFallsBackToLegacyMainJson() {
        string tempRootJsonc = System.IO.Path.Combine(path1: System.IO.Path.GetTempPath(), path2: $"RemakeEngine_CreateJsonc_{System.Guid.NewGuid():N}");
        string tempRootLegacy = System.IO.Path.Combine(path1: System.IO.Path.GetTempPath(), path2: $"RemakeEngine_CreateJson_{System.Guid.NewGuid():N}");

        try {
            // Case 1: Both Main.jsonc and Main.json exist -> Main.jsonc takes precedence
            string modulesDirBoth = System.IO.Path.Combine(path1: tempRootJsonc, path2: "EngineApps", path3: "Registries", path4: "Modules");
            System.IO.Directory.CreateDirectory(path: modulesDirBoth);
            System.IO.File.WriteAllText(path: System.IO.Path.Combine(path1: modulesDirBoth, path2: "Main.jsonc"), contents: """
            {
              "modules": {
                "Demo": {
                  "FromJsonc": { "id": "10", "path": "EngineApps/Games/FromJsonc", } // JSONC winner
                },
              }
            }
            """);
            System.IO.File.WriteAllText(path: System.IO.Path.Combine(path1: modulesDirBoth, path2: "Main.json"), contents: """
            {
              "modules": {
                "Demo": {
                  "FromLegacyJson": { "id": "99", "path": "EngineApps/Games/FromLegacyJson" }
                }
              }
            }
            """);

            Registries preferred = await Registries.CreateAsync(rootPath: tempRootJsonc);
            IReadOnlyDictionary<string, object?> preferredModules = preferred.GetRegisteredModules();
            Assert.IsTrue(condition: preferredModules.ContainsKey(key: "FromJsonc"));
            Assert.IsFalse(condition: preferredModules.ContainsKey(key: "FromLegacyJson"));

            // Case 2: Only legacy Main.json exists -> falls back cleanly
            string modulesDirLegacy = System.IO.Path.Combine(path1: tempRootLegacy, path2: "EngineApps", path3: "Registries", path4: "Modules");
            System.IO.Directory.CreateDirectory(path: modulesDirLegacy);
            System.IO.File.WriteAllText(path: System.IO.Path.Combine(path1: modulesDirLegacy, path2: "Main.json"), contents: """
            {
              "modules": {
                "LegacyOnly": { "id": "11", "path": "EngineApps/Games/LegacyOnly" }
              }
            }
            """);

            Registries fallback = await Registries.CreateAsync(rootPath: tempRootLegacy);
            Assert.IsTrue(condition: fallback.GetRegisteredModules().ContainsKey(key: "LegacyOnly"));
        } finally {
            if (System.IO.Directory.Exists(path: tempRootJsonc)) {
                System.IO.Directory.Delete(path: tempRootJsonc, recursive: true);
            }
            if (System.IO.Directory.Exists(path: tempRootLegacy)) {
                System.IO.Directory.Delete(path: tempRootLegacy, recursive: true);
            }
        }
    }

    [TestMethod]
    public void RefreshModules_ResolvesSiblingJsoncAndDiscoverGamesSupportsOperationsJsonc() {
        string tempRoot = System.IO.Path.Combine(path1: System.IO.Path.GetTempPath(), path2: $"RemakeEngine_OpsJsonc_{System.Guid.NewGuid():N}");
        try {
            string gamesDir = System.IO.Path.Combine(path1: tempRoot, path2: "Games");
            System.IO.Directory.CreateDirectory(path: gamesDir);

            // Pass Main.json path even though only Main.jsonc exists on disk
            string configuredMainJson = System.IO.Path.Combine(path1: tempRoot, path2: "Main.json");
            string siblingMainJsonc = System.IO.Path.Combine(path1: tempRoot, path2: "Main.jsonc");
            System.IO.File.WriteAllText(path: siblingMainJsonc, contents: """
            {
              "modules": {
                "GameJsonc": { "id": "1", "path": "Games/GameJsonc", },
                "GameJson": { "id": "2", "path": "Games/GameJson", },
              },
            }
            """);

            string modJsoncDir = System.IO.Path.Combine(path1: gamesDir, path2: "GameJsonc");
            string modJsonDir = System.IO.Path.Combine(path1: gamesDir, path2: "GameJson");
            System.IO.Directory.CreateDirectory(path: modJsoncDir);
            System.IO.Directory.CreateDirectory(path: modJsonDir);

            // Both operations.jsonc and operations.json -> operations.jsonc takes precedence over operations.json
            string expectedJsoncOps = System.IO.Path.Combine(path1: modJsoncDir, path2: "operations.jsonc");
            System.IO.File.WriteAllText(path: expectedJsoncOps, contents: "[]");
            System.IO.File.WriteAllText(path: System.IO.Path.Combine(path1: modJsoncDir, path2: "operations.json"), contents: "[]");

            string expectedJsonOps = System.IO.Path.Combine(path1: modJsonDir, path2: "operations.json");
            System.IO.File.WriteAllText(path: expectedJsonOps, contents: "[]");

            Registries registries = new(gamesRel: gamesDir, modulesRel: configuredMainJson);
            Assert.AreEqual(expected: 2, actual: registries.GetRegisteredModules().Count);

            Dictionary<string, GameInfo> discovered = registries.DiscoverGames();
            Assert.AreEqual(expected: System.IO.Path.GetFullPath(path: expectedJsoncOps), actual: discovered[key: "GameJsonc"].OpsFile);
            Assert.AreEqual(expected: System.IO.Path.GetFullPath(path: expectedJsonOps), actual: discovered[key: "GameJson"].OpsFile);
        } finally {
            if (System.IO.Directory.Exists(path: tempRoot)) {
                System.IO.Directory.Delete(path: tempRoot, recursive: true);
            }
        }
    }
}

