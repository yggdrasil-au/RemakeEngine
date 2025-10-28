using Tomlyn;
namespace EngineNet.Core;

internal sealed partial class OperationsEngine {
    /// <summary>
    /// Loads a flat list of operations from a TOML or JSON file.
    /// </summary>
    /// <param name="opsFile">Path to operations.toml or operations.json.</param>
    /// <returns>List of operation maps (dictionary of string to object).</returns>
    public List<Dictionary<string, object?>> LoadOperationsList(string opsFile) {
        string ext = System.IO.Path.GetExtension(opsFile);
        if (ext.Equals(".toml", System.StringComparison.OrdinalIgnoreCase)) {
            Tomlyn.Syntax.DocumentSyntax tdoc = Tomlyn.Toml.Parse(System.IO.File.ReadAllText(opsFile));
            Tomlyn.Model.TomlTable model = tdoc.ToModel();
            List<Dictionary<string, object?>> list = new List<Dictionary<string, object?>>();
            if (model is Tomlyn.Model.TomlTable table) {
                foreach (KeyValuePair<string, object> kv in table) {
                    if (kv.Value is Tomlyn.Model.TomlTableArray arr) {
                        foreach (Tomlyn.Model.TomlTable item in arr) {
                            if (item is Tomlyn.Model.TomlTable tt) {
                                list.Add(ToMap(tt));
                            }
                        }
                    }
                }
            }
            return list;
        }
		using System.IO.FileStream fs = System.IO.File.OpenRead(opsFile);
        using System.Text.Json.JsonDocument jdoc = System.Text.Json.JsonDocument.Parse(fs);
        if (jdoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array) {
            List<Dictionary<string, object?>> list = new List<Dictionary<string, object?>>();
            foreach (System.Text.Json.JsonElement item in jdoc.RootElement.EnumerateArray()) {
                if (item.ValueKind == System.Text.Json.JsonValueKind.Object) {
                    list.Add(ToMap(item));
                }
            }
            return list;
        }
        if (jdoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object) {
            // Fallback: flatten grouped format into a single list (preserving group order)
            List<Dictionary<string, object?>> flat = new List<Dictionary<string, object?>>();
            foreach (System.Text.Json.JsonProperty prop in jdoc.RootElement.EnumerateObject()) {
                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Array) {
                    foreach (System.Text.Json.JsonElement item in prop.Value.EnumerateArray()) {
                        if (item.ValueKind == System.Text.Json.JsonValueKind.Object) {
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
    public Dictionary<string, List<Dictionary<string, object?>>> LoadOperations(string opsFile) {
        string ext = System.IO.Path.GetExtension(opsFile);
        if (ext.Equals(".toml", System.StringComparison.OrdinalIgnoreCase)) {
            Tomlyn.Syntax.DocumentSyntax tdoc = Tomlyn.Toml.Parse(System.IO.File.ReadAllText(opsFile));
            Tomlyn.Model.TomlTable model = tdoc.ToModel();
            Dictionary<string, List<Dictionary<string, object?>>> result = new Dictionary<string, List<Dictionary<string, object?>>>(System.StringComparer.OrdinalIgnoreCase);
            if (model is Tomlyn.Model.TomlTable table) {
                foreach (KeyValuePair<string, object> kv in table) {
                    if (kv.Value is Tomlyn.Model.TomlTableArray arr) {
                        List<Dictionary<string, object?>> list = new List<Dictionary<string, object?>>();
                        foreach (Tomlyn.Model.TomlTable item in arr) {
                            if (item is Tomlyn.Model.TomlTable tt) {
                                list.Add(ToMap(tt));
                            }
                        }
                        result[kv.Key] = list;
                    }
                }
            }
            return result;
        }

        using System.IO.FileStream fs = System.IO.File.OpenRead(opsFile);
        using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(fs);
        Dictionary<string, List<Dictionary<string, object?>>> resultJson = new Dictionary<string, List<Dictionary<string, object?>>>(System.StringComparer.OrdinalIgnoreCase);
        if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object) {
            foreach (System.Text.Json.JsonProperty prop in doc.RootElement.EnumerateObject()) {
                List<Dictionary<string, object?>> list = new List<Dictionary<string, object?>>();
                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Array) {
                    foreach (System.Text.Json.JsonElement item in prop.Value.EnumerateArray()) {
                        if (item.ValueKind == System.Text.Json.JsonValueKind.Object) {
                            list.Add(ToMap(item));
                        }
                    }
                }
                resultJson[prop.Name] = list;
            }
        }
        return resultJson;
    }

    private static Dictionary<string, object?> ToMap(System.Text.Json.JsonElement obj) {
        Dictionary<string, object?> dict = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.Json.JsonProperty p in obj.EnumerateObject()) {
            dict[p.Name] = FromJson(p.Value);
        }
        return dict;
    }

    private static object? FromJson(System.Text.Json.JsonElement el) {
        return el.ValueKind switch {
            System.Text.Json.JsonValueKind.Object => ToMap(el),
            System.Text.Json.JsonValueKind.Array => ToList(el),
            System.Text.Json.JsonValueKind.String => el.GetString(),
            System.Text.Json.JsonValueKind.Number => el.TryGetInt64(out long l) ? l : el.TryGetDouble(out double d) ? d : el.GetRawText(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            _ => null
        };
    }

    private static List<object?> ToList(System.Text.Json.JsonElement arr) {
        List<object?> list = new List<object?>();
        foreach (System.Text.Json.JsonElement item in arr.EnumerateArray()) {
            list.Add(FromJson(item));
        }

        return list;
    }

    private static Dictionary<string, object?> ToMap(Tomlyn.Model.TomlTable table) {
        Dictionary<string, object?> dict = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, object> kv in table) {
            dict[kv.Key] = FromToml(kv.Value);
        }

        return dict;
    }

    private static object? FromToml(object? value) {
        switch (value) {
            case Tomlyn.Model.TomlTable tt:
                return ToMap(tt);
            case Tomlyn.Model.TomlTableArray ta:
                List<object?> listTa = new List<object?>();
                foreach (Tomlyn.Model.TomlTable item in ta) {
                    listTa.Add(FromToml(item));
                }

                return listTa;
            case Tomlyn.Model.TomlArray arr:
                List<object?> listArr = new List<object?>();
                foreach (object? item in arr) {
                    listArr.Add(FromToml(item));
                }

                return listArr;
            default:
                return value;
        }
    }

}
