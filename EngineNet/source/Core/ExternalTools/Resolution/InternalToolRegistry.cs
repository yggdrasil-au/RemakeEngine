using System.Text.Json;

namespace EngineNet.Core.ExternalTools;

/// <summary>
/// Dynamically assembles the tools registry from fragmented JSON and JSONC files in the Tools directory.
/// Scans EngineApps/Registries/Tools/ and deep-merges all JSON and JSONC files found in each subfolder.
/// </summary>
internal static class InternalToolRegistry {
    private static readonly string ToolsRegistryRoot = Path.Combine(path1: EngineNet.Shared.State.RootPath, path2: "EngineApps", path3: "Registries", path4: "Tools");

    /// <summary>
    /// Aggregates all tool definitions from the registry folder into a single result object.
    /// Result structure: { "ToolName": { "Version": { "Platform": { "url": "...", ... } } } }
    /// </summary>
    internal static Dictionary<string, object?> Assemble() {
        return Assemble(toolsRegistryRoot: ToolsRegistryRoot);
    }

    internal static Dictionary<string, object?> Assemble(string toolsRegistryRoot) {
        Dictionary<string, object?> registry = new(comparer: StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(toolsRegistryRoot) || !Directory.Exists(path: toolsRegistryRoot)) {
            Shared.IO.Diagnostics.Trace($"Tools registry root not found: {toolsRegistryRoot}");
            return registry;
        }

        foreach (string toolDir in Directory.GetDirectories(path: toolsRegistryRoot)) {
            string toolName = Path.GetFileName(path: toolDir);
            Dictionary<string, object?> toolData = new(comparer: StringComparer.OrdinalIgnoreCase);

            IEnumerable<string> manifestFiles = Directory.EnumerateFiles(path: toolDir)
                .Where(predicate: file =>
                    file.EndsWith(value: ".json", comparisonType: StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(value: ".jsonc", comparisonType: StringComparison.OrdinalIgnoreCase))
                .OrderBy(keySelector: file => file, comparer: StringComparer.OrdinalIgnoreCase);

            foreach (string jsonFile in manifestFiles) {
                try {
                    string content = Shared.IO.LongPathIO.ReadAllText(path: jsonFile);
                    Dictionary<string, object?>? fileData = Shared.Serialization.Json.JsonHelpers.Deserialize<Dictionary<string, object?>>(json: content);
                    if (fileData != null) {
                        MergeDictionaries(target: toolData, source: fileData);
                    }
                } catch (JsonException ex) {
                    Shared.IO.Diagnostics.Bug(message: $"[InternalToolRegistry.cs::Assemble()]:: catch: Error parsing registry fragment '{jsonFile}': {ex.Message}", ex: ex);
                    Shared.IO.Diagnostics.Log($"Error parsing {jsonFile}: {ex.Message}");
                } catch (IOException ex) {
                    Shared.IO.Diagnostics.Bug(message: $"[InternalToolRegistry.cs::Assemble()]:: catch: IO error reading registry fragment '{jsonFile}': {ex.Message}", ex: ex);
                    Shared.IO.Diagnostics.Log($"Error reading {jsonFile}: {ex.Message}");
                } catch (UnauthorizedAccessException ex) {
                    Shared.IO.Diagnostics.Bug(message: $"[InternalToolRegistry.cs::Assemble()]:: catch: Access denied reading registry fragment '{jsonFile}': {ex.Message}", ex: ex);
                    Shared.IO.Diagnostics.Log($"Access denied reading {jsonFile}: {ex.Message}");
                }
            }

            if (toolData.Count > 0) {
                registry[key: toolName] = toolData;
            }
        }

        return registry;
    }

    private static void MergeDictionaries(IDictionary<string, object?> target, IDictionary<string, object?> source) {
        foreach (KeyValuePair<string, object?> kvp in source) {
            if (target.TryGetValue(key: kvp.Key, out object? existingValue)) {
                if (existingValue is IDictionary<string, object?> targetDict && kvp.Value is IDictionary<string, object?> sourceDict) {
                    MergeDictionaries(target: targetDict, source: sourceDict);
                    continue;
                }

                if (existingValue is JsonElement targetElem && targetElem.ValueKind == JsonValueKind.Object &&
                    kvp.Value is JsonElement sourceElem && sourceElem.ValueKind == JsonValueKind.Object) {

                    Dictionary<string, object?> merged = MergeJsonElements(target: targetElem, source: sourceElem);
                    target[key: kvp.Key] = merged;
                    continue;
                }
            }
            target[key: kvp.Key] = kvp.Value;
        }
    }

    private static Dictionary<string, object?> MergeJsonElements(JsonElement target, JsonElement source) {
        Dictionary<string, object?> result = new(comparer: StringComparer.OrdinalIgnoreCase);

        foreach (JsonProperty prop in target.EnumerateObject()) {
            result[key: prop.Name] = prop.Value;
        }

        foreach (JsonProperty prop in source.EnumerateObject()) {
            if (result.TryGetValue(key: prop.Name, out object? existing) && existing is JsonElement targetSub && targetSub.ValueKind == JsonValueKind.Object &&
                prop.Value.ValueKind == JsonValueKind.Object) {
                result[key: prop.Name] = MergeJsonElements(target: targetSub, source: prop.Value);
            } else {
                result[key: prop.Name] = prop.Value;
            }
        }

        return result;
    }
}
