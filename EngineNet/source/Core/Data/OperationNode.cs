
namespace EngineNet.Core.Data;

/// <summary>
/// Represents a single operation in the dependency graph.
/// </summary>
internal class OperationNode {
    internal string Id { get; set; } = string.Empty;
    internal string Name { get; set; } = string.Empty;
    internal Dictionary<string, object?> Operation { get; set; } = new();
    internal List<string> Dependencies { get; set; } = new();
    internal List<OperationNode> DependentNodes { get; set; } = new();
}