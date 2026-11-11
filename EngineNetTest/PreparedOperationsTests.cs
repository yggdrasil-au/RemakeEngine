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

    /// <summary>
    /// Verifies that operations.jsonc manifests with comments, trailing commas, and grouped formats load cleanly.
    /// </summary>
    [TestMethod]
    public void LoadAndPrepare_SupportsOperationsJsoncWithCommentsAndTrailingCommas() {
        string operationsFile = Path.Combine(path1: Path.GetTempPath(), path2: $"remake-engine-{Guid.NewGuid():N}.jsonc");
        File.WriteAllText(path: operationsFile, contents: """
                                                          // JSONC operations manifest
                                                          {
                                                            "setup": [
                                                              { "id": 1, "name": "Initialize", "script": "init.lua", "script_type": "lua", "init": true, },
                                                            ],
                                                            /* Build steps */
                                                            "build": [
                                                              { "id": 2, "name": "Compile", "script": "compile.lua", "script_type": "lua", "run_all": true, },
                                                            ],
                                                          }
                                                          """);

        try {
            EngineNet.Core.Services.OperationsService.OperationsService service = new EngineNet.Core.Services.OperationsService.OperationsService(loader: new EngineNet.Core.Services.OperationsService.OperationsLoader(), gameRegistry: null!);
            PreparedOperations prepared = service.LoadAndPrepare(opsFile: operationsFile);

            Assert.IsTrue(condition: prepared.IsLoaded);
            Assert.HasCount(expected: 1, collection: prepared.InitOperations);
            Assert.HasCount(expected: 1, collection: prepared.RegularOperations);
            Assert.HasCount(expected: 1, collection: prepared.RunAllOperations);
            Assert.AreEqual(expected: "Compile", actual: prepared.RunAllOperations[index: 0].DisplayName);
        } finally {
            File.Delete(path: operationsFile);
        }
    }

    /// <summary>
    /// Verifies that comment-only operations.jsonc manifests load cleanly as empty operation lists.
    /// </summary>
    [TestMethod]
    public void LoadAndPrepare_HandlesCommentOnlyOperationsJsoncAsEmpty() {
        string operationsFile = Path.Combine(path1: Path.GetTempPath(), path2: $"remake-engine-empty-{Guid.NewGuid():N}.jsonc");
        File.WriteAllText(path: operationsFile, contents: "// No operations configured yet\n/* Placeholder */");

        try {
            EngineNet.Core.Services.OperationsService.OperationsService service = new EngineNet.Core.Services.OperationsService.OperationsService(loader: new EngineNet.Core.Services.OperationsService.OperationsLoader(), gameRegistry: null!);
            PreparedOperations prepared = service.LoadAndPrepare(opsFile: operationsFile);

            Assert.IsTrue(condition: prepared.IsLoaded);
            Assert.IsEmpty(collection: prepared.InitOperations);
            Assert.IsEmpty(collection: prepared.RegularOperations);
            Assert.IsEmpty(collection: prepared.RunAllOperations);
            Assert.IsFalse(condition: prepared.HasRunAll);
        } finally {
            File.Delete(path: operationsFile);
        }
    }
}