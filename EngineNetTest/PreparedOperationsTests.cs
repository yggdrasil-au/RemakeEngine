using EngineNet.Core.Data;
using EngineNet.Core.Services;

namespace EngineNetTest;

using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class PreparedOperationsTests {
    /// <summary>
    /// Verifies that the shared prepared representation owns init, regular, and run-all categories.
    /// </summary>
    [TestMethod]
    public void LoadAndPrepare_CategorizesInitRegularAndRunAllOperations() {
        string operationsFile = Path.Combine(path1: Path.GetTempPath(), path2: $"remake-engine-{Guid.NewGuid():N}.json");
        File.WriteAllText(path: operationsFile, contents: """
                                                          [
                                                            { "id": 1, "name": "Initialize", "script": "init.lua", "script_type": "lua", "init": true },
                                                            { "id": 2, "name": "Build", "script": "build.lua", "script_type": "lua", "run_all": true },
                                                            { "id": 3, "name": "Optional", "script": "optional.lua", "script_type": "lua" }
                                                          ]
                                                          """);

        try {
            EngineNet.Core.Services.OperationsService.OperationsService service = new EngineNet.Core.Services.OperationsService.OperationsService(loader: new EngineNet.Core.Services.OperationsService.OperationsLoader(), gameRegistry: null!);
            PreparedOperations prepared = service.LoadAndPrepare(opsFile: operationsFile);

            Assert.IsTrue(condition: prepared.IsLoaded);
            Assert.HasCount(expected: 1, collection: prepared.InitOperations);
            Assert.HasCount(expected: 2, collection: prepared.RegularOperations);
            Assert.HasCount(expected: 1, collection: prepared.RunAllOperations);
            Assert.IsTrue(condition: prepared.HasRunAll);
            Assert.AreEqual(expected: "Build", actual: prepared.RunAllOperations[index: 0].DisplayName);
        } finally {
            File.Delete(path: operationsFile);
        }
    }
}