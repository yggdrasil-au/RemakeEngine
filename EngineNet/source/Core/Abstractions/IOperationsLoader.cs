
namespace EngineNet.Core.Abstractions;

public interface IOperationsLoader {
    List<Dictionary<string, object?>>? LoadOperations(string filePath);
}
