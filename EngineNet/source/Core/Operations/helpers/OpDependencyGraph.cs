
namespace EngineNet.Core.Operations.helpers;

using Data;

/// <summary>
/// Builds, validates, and visualizes the operation dependency graph.
/// used by the Run-All operation to determine execution order and detect issues before starting execution.
/// </summary>
internal sealed class OpDependencyGraph {
    internal bool IsValid { get; private set; }
    private List<string> Errors { get; set; } = new();

    private readonly Dictionary<string, Core.Data.OperationNode> _nodes = new(comparer: StringComparer.OrdinalIgnoreCase);

    internal OpDependencyGraph(List<Dictionary<string, object?>> operations) {
        BuildGraph(operations: operations);
    }

    /// <summary>
    /// Prints the graph status and structure to Shared.IO.Diagnostics.Trace.
    /// </summary>
    internal void PrintGraphToTrace() {
        Shared.IO.Diagnostics.Trace("=== [Dependency Graph Builder] ===");

        if (!IsValid) {
            Shared.IO.Diagnostics.Trace("[DependencyGraph] Graph is INVALID. Parallel features would be disabled.");
            Shared.IO.Diagnostics.Trace("[DependencyGraph] Errors:");
            foreach (string err in Errors) {
                Shared.IO.Diagnostics.Trace($"  => {err}");
            }
            Shared.IO.Diagnostics.Trace("==================================");
            return;
        }

        Shared.IO.Diagnostics.Trace("[DependencyGraph] Graph is VALID. Dependency Map:");

        foreach (OperationNode node in _nodes.Values) {
            string line = $"  [{node.Id}]";

            if (node.Dependencies.Count > 0) {
                line += $" --> depends on --> [{string.Join(separator: ", ", values: node.Dependencies)}]";
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
        List<Dictionary<string, object?>> relevantOps = FilterRelevantOperations(allOps: operations);

        // Pass 2: Create nodes and validate IDs for relevant operations
        foreach (Dictionary<string, object?> op in relevantOps) {
            string id = GetString(dict: op, key: "id");
            string name = GetString(dict: op, key: "Name");
            if (string.IsNullOrWhiteSpace(name)) name = id; // Fallback to ID for display

            if (string.IsNullOrWhiteSpace(id)) {
                IsValid = false;
                Errors.Add(item: $"An operation named '{name}' is missing an 'id'.");
                continue;
            }

            if (_nodes.ContainsKey(key: id)) {
                IsValid = false;
                Errors.Add(item: $"Duplicate operation ID found: '{id}'. Only relevant operations (marked for run-all or as dependencies) are checked.");
                continue;
            }

            _nodes[key: id] = new Core.Data.OperationNode {
                Id = id,
                Name = name,
                Operation = op,
                Dependencies = GetStringList(dict: op, key: "depends_on").Concat(second: GetStringList(dict: op, key: "depends-on")).ToList()
            };
        }

        if (!IsValid) return; // Stop if IDs are broken (we can't link safely)

        // Pass 3: Link dependencies and validate references
        foreach (OperationNode node in _nodes.Values) {
            foreach (string depId in node.Dependencies) {
                if (!_nodes.TryGetValue(key: depId, out OperationNode? depNode)) {
                    IsValid = false;
                    Errors.Add(item: $"Operation '{node.Id}' depends on unknown or irrelevant ID: '{depId}'.");
                } else {
                    node.DependentNodes.Add(item: depNode);
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
        HashSet<Dictionary<string, object?>> relevant = new HashSet<Dictionary<string, object?>>();
        Queue<Dictionary<string, object?>> queue = new Queue<Dictionary<string, object?>>();

        // Start with entry points (init or run-all flag set)
        foreach (Dictionary<string, object?> op in allOps) {
            if (IsFlagSet(op: op, key: "init") || IsFlagSet(op: op, key: "run-all") || IsFlagSet(op: op, key: "run_all")) {
                if (relevant.Add(item: op)) {
                    queue.Enqueue(item: op);
                }
            }
        }

        // Trace recursive dependencies
        Dictionary<string, Dictionary<string, object?>> idToOpMap = allOps
            .Where(predicate: o => !string.IsNullOrEmpty(GetString(dict: o, key: "id")))
            .GroupBy(keySelector: o => GetString(dict: o, key: "id"))
            .ToDictionary(keySelector: g => g.Key, elementSelector: g => g.First(), comparer: StringComparer.OrdinalIgnoreCase);

        while (queue.Count > 0) {
            Dictionary<string, object?> current = queue.Dequeue();
            IEnumerable<string> deps = GetStringList(dict: current, key: "depends_on").Concat(second: GetStringList(dict: current, key: "depends-on"));

            foreach (string depId in deps) {
                if (!idToOpMap.TryGetValue(key: depId, out Dictionary<string, object?>? depOp)) continue;
                if (relevant.Add(item: depOp)) {
                    queue.Enqueue(item: depOp);
                }
            }
        }

        return relevant.ToList();
    }

    private static bool IsFlagSet(Dictionary<string, object?> op, string key) {
        if (!op.TryGetValue(key: key, out object? value) || value is null) return false;
        if (value is bool b) return b;
        if (value is string s) return bool.TryParse(s, result: out bool parsed) && parsed;
        try { return Convert.ToInt32(value) != 0; } catch (System.Exception ex) { Shared.IO.Diagnostics.Bug($"[OpDependencyGraph::IsFlagSet()] Failed to convert flag '{key}' value '{value}' to boolean.", ex: ex); return false; }
    }

    private bool HasCycles() {
        HashSet<string> visited = new HashSet<string>(comparer: StringComparer.OrdinalIgnoreCase);
        HashSet<string> recursionStack = new HashSet<string>(comparer: StringComparer.OrdinalIgnoreCase);

        foreach (OperationNode node in _nodes.Values) {
            if (DetectCycle(node: node, visited: visited, recursionStack: recursionStack)) {
                return true;
            }
        }
        return false;
    }

    private bool DetectCycle(Core.Data.OperationNode node, HashSet<string> visited, HashSet<string> recursionStack) {
        if (recursionStack.Contains(item: node.Id)) {
            Errors.Add(item: $"Circular dependency detected involving operation '{node.Id}'.");
            return true;
        }

        if (visited.Contains(item: node.Id)) return false;

        visited.Add(item: node.Id);
        recursionStack.Add(item: node.Id);

        foreach (OperationNode dep in node.DependentNodes) {
            if (DetectCycle(node: dep, visited: visited, recursionStack: recursionStack)) return true;
        }

        recursionStack.Remove(item: node.Id);
        return false;
    }


    // --- Dictionary Extraction Helpers ---

    private static string GetString(Dictionary<string, object?> dict, string key) {
        return dict.TryGetValue(key: key, out object? value) && value is not null ? value.ToString() ?? string.Empty : string.Empty;
    }

    private static List<string> GetStringList(Dictionary<string, object?> dict, string key) {
        List<string> list = new List<string>();
        if (dict.TryGetValue(key: key, out object? value) && value is System.Collections.IEnumerable enumerable && value is not string) {
            foreach (object? item in enumerable) {
                if (item is not null) list.Add(item: item.ToString()!);
            }
        }
        return list;
    }
}
