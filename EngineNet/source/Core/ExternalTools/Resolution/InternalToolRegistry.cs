using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EngineNet.Core.ExternalTools;

/// <summary>
/// Dynamically assembles the tools registry from fragmented JSON files in the Tools directory.
/// Scans EngineApps/Registries/Tools/ and deep-merges all JSON files found in each subfolder.
/// </summary>
internal static class InternalToolRegistry {
    private static readonly string ToolsRegistryRoot = Path.Combine(Program.rootPath, "EngineApps", "Registries", "Tools");

    /// <summary>
    /// Aggregates all tool definitions from the registry folder into a single result object.
    /// Result structure: { "ToolName": { "Version": { "Platform": { "url": "...", ... } } } }
    /// </summary>
    public static Dictionary<string, object?> Assemble() {
        var registry = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(ToolsRegistryRoot)) {
            Diagnostics.Trace($"[InternalToolRegistry] Tools registry root not found: {ToolsRegistryRoot}");
            return registry;
        }

        foreach (string toolDir in Directory.GetDirectories(ToolsRegistryRoot)) {
            string toolName = Path.GetFileName(toolDir);
            var toolData = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (string jsonFile in Directory.GetFiles(toolDir, "*.json")) {
                try {
                    string content = File.ReadAllText(jsonFile);
                    var fileData = JsonSerializer.Deserialize<Dictionary<string, object?>>(content);
                    if (fileData != null) {
                        MergeDictionaries(toolData, fileData);
                    }
                } catch (Exception ex) {
                    Diagnostics.Log($"[InternalToolRegistry] Error parsing {jsonFile}: {ex.Message}");
                }
            }

            if (toolData.Count > 0) {
                registry[toolName] = toolData;
            }
        }

        return registry;
    }

    private static void MergeDictionaries(IDictionary<string, object?> target, IDictionary<string, object?> source) {
        foreach (var kvp in source) {
            if (target.TryGetValue(kvp.Key, out object? existingValue)) {
                if (existingValue is IDictionary<string, object?> targetDict && kvp.Value is IDictionary<string, object?> sourceDict) {
                    MergeDictionaries(targetDict, sourceDict);
                    continue;
                }
                
                if (existingValue is JsonElement targetElem && targetElem.ValueKind == JsonValueKind.Object &&
                    kvp.Value is JsonElement sourceElem && sourceElem.ValueKind == JsonValueKind.Object) {
                    
                    var merged = MergeJsonElements(targetElem, sourceElem);
                    target[kvp.Key] = merged;
                    continue;
                }
            }
            target[kvp.Key] = kvp.Value;
        }
    }

    private static Dictionary<string, object?> MergeJsonElements(JsonElement target, JsonElement source) {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var prop in target.EnumerateObject()) {
            result[prop.Name] = prop.Value;
        }
        
        foreach (var prop in source.EnumerateObject()) {
            if (result.TryGetValue(prop.Name, out object? existing) && existing is JsonElement targetSub && targetSub.ValueKind == JsonValueKind.Object &&
                prop.Value.ValueKind == JsonValueKind.Object) {
                result[prop.Name] = MergeJsonElements(targetSub, prop.Value);
            } else {
                result[prop.Name] = prop.Value;
            }
        }
        
        return result;
    }
}
