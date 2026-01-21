using System.Collections.Generic;

namespace EngineNet.Core.Abstractions;

internal interface IOperationsLoader {
    List<Dictionary<string, object?>>? LoadOperations(string filePath);
}
