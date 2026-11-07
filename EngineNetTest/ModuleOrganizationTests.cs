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
}

