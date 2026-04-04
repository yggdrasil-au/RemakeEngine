
namespace EngineNet.Core.Operations.helpers;

/// <summary>
/// Builds, validates, and visualizes the operation dependency graph.
/// used by the Run-All operation to determine execution order and detect issues before starting execution.
/// </summary>
internal class OpDependencyGraph {
    internal bool IsValid { get; private set; }
    internal List<string> Errors { get; private set; } = new();

    private readonly Dictionary<string, Core.Data.OperationNode> _nodes = new(StringComparer.OrdinalIgnoreCase);

    internal OpDependencyGraph(List<Dictionary<string, object?>> operations) {
        BuildGraph(operations);
    }

    /// <summary>
    /// Prints the graph status and structure to Shared.IO.Diagnostics.Trace.
    /// </summary>
    internal void PrintGraphToTrace() {
        Shared.IO.Diagnostics.Trace("=== [Dependency Graph Builder] ===");

        if (!IsValid) {
            Shared.IO.Diagnostics.Trace("[DependencyGraph] Graph is INVALID. Parallel features would be disabled.");
            Shared.IO.Diagnostics.Trace("[DependencyGraph] Errors:");
            foreach (var err in Errors) {
                Shared.IO.Diagnostics.Trace($"  => {err}");
            }
            Shared.IO.Diagnostics.Trace("==================================");
            return;
        }

        Shared.IO.Diagnostics.Trace("[DependencyGraph] Graph is VALID. Dependency Map:");

        foreach (var node in _nodes.Values) {
            string line = $"  [{node.Id}]";

            if (node.Dependencies.Count > 0) {
                line += $" --> depends on --> [{string.Join(", ", node.Dependencies)}]";
            } else {
                line += " (No dependencies)";
            }

            Shared.IO.Diagnostics.Trace(line);
        }
        Shared.IO.Diagnostics.Trace("==================================");
    }

    private void BuildGraph(List<Dictionary<string, object?>> operations) {
        IsValid = true;
        Errors.Clear();
        _nodes.Clear();

        // Pass 1: Filter relevant operations (Run-All entry points and their transitive dependencies)
        var relevantOps = FilterRelevantOperations(operations);

        // Pass 2: Create nodes and validate IDs for relevant operations
        foreach (var op in relevantOps) {
            string id = GetString(op, "id");
            string name = GetString(op, "Name");
            if (string.IsNullOrWhiteSpace(name)) name = id; // Fallback to ID for display

            if (string.IsNullOrWhiteSpace(id)) {
                IsValid = false;
                Errors.Add($"An operation named '{name}' is missing an 'id'.");
                continue;
            }

            if (_nodes.ContainsKey(id)) {
                IsValid = false;
                Errors.Add($"Duplicate operation ID found: '{id}'. Only relevant operations (marked for run-all or as dependencies) are checked.");
                continue;
            }

            _nodes[id] = new Core.Data.OperationNode {
                Id = id,
                Name = name,
                Operation = op,
                Dependencies = GetStringList(op, "depends_on").Concat(GetStringList(op, "depends-on")).ToList()
            };
        }

        if (!IsValid) return; // Stop if IDs are broken (we can't link safely)

        // Pass 3: Link dependencies and validate references
        foreach (var node in _nodes.Values) {
            foreach (var depId in node.Dependencies) {
                if (!_nodes.TryGetValue(depId, out var depNode)) {
                    IsValid = false;
                    Errors.Add($"Operation '{node.Id}' depends on unknown or irrelevant ID: '{depId}'.");
                } else {
                    node.DependentNodes.Add(depNode);
                }
            }
        }

        if (!IsValid) return; // Stop if references are broken

        // Pass 4: Cycle Detection (e.g., A depends on B, B depends on A)
        if (HasCycles()) {
            IsValid = false;
        }
    }

    private List<Dictionary<string, object?>> FilterRelevantOperations(List<Dictionary<string, object?>> allOps) {
        var relevant = new HashSet<Dictionary<string, object?>>();
        var queue = new Queue<Dictionary<string, object?>>();

        // Start with entry points (init or run-all flag set)
        foreach (var op in allOps) {
            if (IsFlagSet(op, "init") || IsFlagSet(op, "run-all") || IsFlagSet(op, "run_all")) {
                if (relevant.Add(op)) {
                    queue.Enqueue(op);
                }
            }
        }

        // Trace recursive dependencies
        var idToOpMap = allOps
            .Where(o => !string.IsNullOrEmpty(GetString(o, "id")))
            .GroupBy(o => GetString(o, "id"))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        while (queue.Count > 0) {
            var current = queue.Dequeue();
            var deps = GetStringList(current, "depends_on").Concat(GetStringList(current, "depends-on"));

            foreach (var depId in deps) {
                if (idToOpMap.TryGetValue(depId, out var depOp)) {
                    if (relevant.Add(depOp)) {
                        queue.Enqueue(depOp);
                    }
                }
            }
        }

        return relevant.ToList();
    }

    private static bool IsFlagSet(Dictionary<string, object?> op, string key) {
        if (!op.TryGetValue(key, out object? value) || value is null) return false;
        if (value is bool b) return b;
        if (value is string s) return bool.TryParse(s, out bool parsed) && parsed;
        try { return Convert.ToInt32(value) != 0; } catch (System.Exception ex) { Shared.IO.Diagnostics.Bug($"[OpDependencyGraph::IsFlagSet()] Failed to convert flag '{key}' value '{value}' to boolean.", ex); return false; }
    }

    private bool HasCycles() {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recursionStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in _nodes.Values) {
            if (DetectCycle(node, visited, recursionStack)) {
                return true;
            }
        }
        return false;
    }

    private bool DetectCycle(Core.Data.OperationNode node, HashSet<string> visited, HashSet<string> recursionStack) {
        if (recursionStack.Contains(node.Id)) {
            Errors.Add($"Circular dependency detected involving operation '{node.Id}'.");
            return true;
        }

        if (visited.Contains(node.Id)) return false;

        visited.Add(node.Id);
        recursionStack.Add(node.Id);

        foreach (var dep in node.DependentNodes) {
            if (DetectCycle(dep, visited, recursionStack)) return true;
        }

        recursionStack.Remove(node.Id);
        return false;
    }


    // --- Dictionary Extraction Helpers ---

    private static string GetString(Dictionary<string, object?> dict, string key) {
        return dict.TryGetValue(key, out object? value) && value is not null ? value.ToString() ?? string.Empty : string.Empty;
    }

    private static List<string> GetStringList(Dictionary<string, object?> dict, string key) {
        var list = new List<string>();
        if (dict.TryGetValue(key, out object? value) && value is System.Collections.IEnumerable enumerable && value is not string) {
            foreach (var item in enumerable) {
                if (item is not null) list.Add(item.ToString()!);
            }
        }
        return list;
    }
}
