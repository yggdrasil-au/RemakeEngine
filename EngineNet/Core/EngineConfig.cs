using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemakeEngine.Core;

public sealed class EngineConfig {
    public string Path {
        get;
    }
    public IDictionary<string, object?> Data => _data;

    private Dictionary<string, object?> _data = new(StringComparer.OrdinalIgnoreCase);

    public EngineConfig(string path) {
        Path = path;
        Reload();
    }

    public void Reload() {
        _data = LoadJsonFile(Path);
    }

    public static Dictionary<string, object?> LoadJsonFile(string filePath) {
        try {
            if (File.Exists(filePath)) {
                using var fs = File.OpenRead(filePath);
                using var doc = JsonDocument.Parse(fs);
                if (doc.RootElement.ValueKind == JsonValueKind.Object) {
                    return (ToDotNet(doc.RootElement) as Dictionary<string, object?>) ?? new Dictionary<string, object?>();
                }
                // Fallback to direct deserialize for simple maps
                fs.Position = 0;
                var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(fs, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return dict ?? new Dictionary<string, object?>();
            }
        } catch {
            // fall through to empty map
        }
        return new Dictionary<string, object?>();
    }

    private static object? ToDotNet(JsonElement el) {
        switch (el.ValueKind) {
            case JsonValueKind.Object:
                var obj = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in el.EnumerateObject()) {
                    obj[prop.Name] = ToDotNet(prop.Value);
                }
                return obj;
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in el.EnumerateArray())
                    list.Add(ToDotNet(item));
                return list;
            case JsonValueKind.String:
                return el.GetString();
            case JsonValueKind.Number:
                if (el.TryGetInt64(out var l))
                    return l;
                if (el.TryGetDouble(out var d))
                    return d;
                return el.GetRawText();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                return null;
        }
    }
}
