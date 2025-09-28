using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;
namespace EngineNet.Core;

public sealed partial class OperationsEngine {
    /// <summary>
    /// Loads a flat list of operations from a TOML or JSON file.
    /// </summary>
    /// <param name="opsFile">Path to operations.toml or operations.json.</param>
    /// <returns>List of operation maps (dictionary of string to object).</returns>
    public List<Dictionary<String, Object?>> LoadOperationsList(String opsFile) {
        String ext = Path.GetExtension(opsFile);
        if (ext.Equals(".toml", StringComparison.OrdinalIgnoreCase)) {
            Tomlyn.Syntax.DocumentSyntax tdoc = Toml.Parse(File.ReadAllText(opsFile));
            TomlTable model = tdoc.ToModel();
            List<Dictionary<String, Object?>> list = new List<Dictionary<String, Object?>>();
            if (model is TomlTable table) {
                foreach (KeyValuePair<String, Object> kv in table) {
                    if (kv.Value is TomlTableArray arr) {
                        foreach (TomlTable item in arr) {
                            if (item is TomlTable tt) {
                                list.Add(ToMap(tt));
                            }
                        }
                    }
                }
            }
            return list;
        }
		using FileStream fs = File.OpenRead(opsFile);
        using JsonDocument jdoc = JsonDocument.Parse(fs);
        if (jdoc.RootElement.ValueKind == JsonValueKind.Array) {
            List<Dictionary<String, Object?>> list = new List<Dictionary<String, Object?>>();
            foreach (JsonElement item in jdoc.RootElement.EnumerateArray()) {
                if (item.ValueKind == JsonValueKind.Object) {
                    list.Add(ToMap(item));
                }
            }
            return list;
        }
        if (jdoc.RootElement.ValueKind == JsonValueKind.Object) {
            // Fallback: flatten grouped format into a single list (preserving group order)
            List<Dictionary<String, Object?>> flat = new List<Dictionary<String, Object?>>();
            foreach (JsonProperty prop in jdoc.RootElement.EnumerateObject()) {
                if (prop.Value.ValueKind == JsonValueKind.Array) {
                    foreach (JsonElement item in prop.Value.EnumerateArray()) {
                        if (item.ValueKind == JsonValueKind.Object) {
                            flat.Add(ToMap(item));
                        }
                    }
                }
            }
            return flat;
        }
        return new();
    }

    /// <summary>
    /// Loads grouped operations from a TOML or JSON file.
    /// </summary>
    /// <param name="opsFile">Path to operations.toml or operations.json.</param>
    /// <returns>Dictionary mapping group name to a list of operations.</returns>
    public Dictionary<String, List<Dictionary<String, Object?>>> LoadOperations(String opsFile) {
        String ext = Path.GetExtension(opsFile);
        if (ext.Equals(".toml", StringComparison.OrdinalIgnoreCase)) {
            Tomlyn.Syntax.DocumentSyntax tdoc = Toml.Parse(File.ReadAllText(opsFile));
            TomlTable model = tdoc.ToModel();
            Dictionary<String, List<Dictionary<String, Object?>>> result = new Dictionary<String, List<Dictionary<String, Object?>>>(StringComparer.OrdinalIgnoreCase);
            if (model is TomlTable table) {
                foreach (KeyValuePair<String, Object> kv in table) {
                    if (kv.Value is TomlTableArray arr) {
                        List<Dictionary<String, Object?>> list = new List<Dictionary<String, Object?>>();
                        foreach (TomlTable item in arr) {
                            if (item is TomlTable tt) {
                                list.Add(ToMap(tt));
                            }
                        }
                        result[kv.Key] = list;
                    }
                }
            }
            return result;
        }

        using FileStream fs = File.OpenRead(opsFile);
        using JsonDocument doc = JsonDocument.Parse(fs);
        Dictionary<String, List<Dictionary<String, Object?>>> resultJson = new Dictionary<String, List<Dictionary<String, Object?>>>(StringComparer.OrdinalIgnoreCase);
        if (doc.RootElement.ValueKind == JsonValueKind.Object) {
            foreach (JsonProperty prop in doc.RootElement.EnumerateObject()) {
                List<Dictionary<String, Object?>> list = new List<Dictionary<String, Object?>>();
                if (prop.Value.ValueKind == JsonValueKind.Array) {
                    foreach (JsonElement item in prop.Value.EnumerateArray()) {
                        if (item.ValueKind == JsonValueKind.Object) {
                            list.Add(ToMap(item));
                        }
                    }
                }
                resultJson[prop.Name] = list;
            }
        }
        return resultJson;
    }

    private static Dictionary<String, Object?> ToMap(JsonElement obj) {
        Dictionary<String, Object?> dict = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty p in obj.EnumerateObject()) {
            dict[p.Name] = FromJson(p.Value);
        }
        return dict;
    }

    private static Object? FromJson(JsonElement el) {
        return el.ValueKind switch {
            JsonValueKind.Object => ToMap(el),
            JsonValueKind.Array => ToList(el),
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out Int64 l) ? l : el.TryGetDouble(out global::System.Double d) ? d : el.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static List<Object?> ToList(JsonElement arr) {
        List<Object?> list = new List<Object?>();
        foreach (JsonElement item in arr.EnumerateArray()) {
            list.Add(FromJson(item));
        }

        return list;
    }

    private static Dictionary<String, Object?> ToMap(TomlTable table) {
        Dictionary<String, Object?> dict = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<String, Object> kv in table) {
            dict[kv.Key] = FromToml(kv.Value);
        }

        return dict;
    }

    private static Object? FromToml(Object? value) {
        switch (value) {
            case TomlTable tt:
                return ToMap(tt);
            case TomlTableArray ta:
                List<Object?> listTa = new List<Object?>();
                foreach (TomlTable item in ta) {
                    listTa.Add(FromToml(item));
                }

                return listTa;
            case TomlArray arr:
                List<Object?> listArr = new List<Object?>();
                foreach (Object? item in arr) {
                    listArr.Add(FromToml(item));
                }

                return listArr;
            default:
                return value;
        }
    }

}
