
namespace EngineNet.Core.Data;

/// <summary>
/// Represents a single operation in the dependency graph.
/// </summary>
public sealed class OperationNode {
    public string Id { get; init; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object?> Operation { get; set; } = new();
    public List<string> Dependencies { get; init; } = new();
    public List<OperationNode> DependentNodes { get; set; } = new();
}