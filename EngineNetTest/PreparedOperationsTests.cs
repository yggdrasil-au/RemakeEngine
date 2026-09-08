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
        string operationsFile = Path.Combine(Path.GetTempPath(), $"remake-engine-{Guid.NewGuid():N}.json");
        File.WriteAllText(operationsFile, """
            [
              { "id": 1, "name": "Initialize", "script": "init.lua", "script_type": "lua", "init": true },
              { "id": 2, "name": "Build", "script": "build.lua", "script_type": "lua", "run_all": true },
              { "id": 3, "name": "Optional", "script": "optional.lua", "script_type": "lua" }
            ]
            """);

        try {
            OperationsService service = new OperationsService(new OperationsLoader(), null!);
            PreparedOperations prepared = service.LoadAndPrepare(operationsFile);

            Assert.IsTrue(prepared.IsLoaded);
            Assert.HasCount(1, prepared.InitOperations);
            Assert.HasCount(2, prepared.RegularOperations);
            Assert.HasCount(1, prepared.RunAllOperations);
            Assert.IsTrue(prepared.HasRunAll);
            Assert.AreEqual("Build", prepared.RunAllOperations[0].DisplayName);
        } finally {
            File.Delete(operationsFile);
        }
    }
}