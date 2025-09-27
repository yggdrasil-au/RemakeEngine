
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;


namespace EngineNet;

public sealed class EngineConfig {
    public String Path {
        get;
    }
    public IDictionary<String, Object?> Data => _data;

    private Dictionary<String, Object?> _data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public EngineConfig(String path) {
        Path = path;
        Reload();
    }

    public void Reload() {
        _data = LoadJsonFile(Path);
    }

    public static Dictionary<String, Object?> LoadJsonFile(String filePath) {
        try {
            if (File.Exists(filePath)) {
                using FileStream fs = File.OpenRead(filePath);
                using JsonDocument doc = JsonDocument.Parse(fs);
                if (doc.RootElement.ValueKind == JsonValueKind.Object) {
                    return ToDotNet(doc.RootElement) as Dictionary<String, Object?> ?? new Dictionary<String, Object?>();
                }
                // Fallback to direct deserialize for simple maps
                fs.Position = 0;
                Dictionary<String, Object?>? dict = JsonSerializer.Deserialize<Dictionary<String, Object?>>(fs, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return dict ?? new Dictionary<String, Object?>();
            }
        } catch {
            // fall through to empty map
        }
        return new Dictionary<String, Object?>();
    }

    private static Object? ToDotNet(JsonElement el) {
        switch (el.ValueKind) {
            case JsonValueKind.Object:
                Dictionary<String, Object?> obj = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty prop in el.EnumerateObject()) {
                    obj[prop.Name] = ToDotNet(prop.Value);
                }
                return obj;
            case JsonValueKind.Array:
                List<Object?> list = new List<Object?>();
                foreach (JsonElement item in el.EnumerateArray()) {
                    list.Add(ToDotNet(item));
                }

                return list;
            case JsonValueKind.String:
                return el.GetString();
            case JsonValueKind.Number:
                if (el.TryGetInt64(out Int64 l)) {
                    return l;
                }

                if (el.TryGetDouble(out Double d)) {
                    return d;
                }

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
