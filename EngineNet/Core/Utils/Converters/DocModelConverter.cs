namespace EngineNet.Core.Utils.Converters;

using System.Collections.Generic;

/// <summary>
/// Utilities to convert JSON/TOML DOM nodes into case-insensitive
/// Dictionary<string, object?> and List<object?> models used by the engine.
/// Centralizes number/boolean/string handling to avoid drift.
/// </summary>
internal static class DocModelConverter {
    internal static Dictionary<string, object?> FromJsonObject(System.Text.Json.JsonElement obj) {
        Dictionary<string, object?> dict = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.Json.JsonProperty p in obj.EnumerateObject()) {
            dict[p.Name] = FromJsonElement(p.Value);
        }

        return dict;
    }

    internal static object? FromJsonElement(System.Text.Json.JsonElement el) {
        switch (el.ValueKind) {
            case System.Text.Json.JsonValueKind.Object:
                return FromJsonObject(el);
            case System.Text.Json.JsonValueKind.Array:
                List<object?> list = new List<object?>();
                foreach (System.Text.Json.JsonElement item in el.EnumerateArray()) {
                    list.Add(FromJsonElement(item));
                }
                return list;
            case System.Text.Json.JsonValueKind.String:
                return el.GetString();
            case System.Text.Json.JsonValueKind.Number:
                if (el.TryGetInt64(out long l)) {
                    return l;
                }
                if (el.TryGetDouble(out double d)) {
                    return d;
                }
                return el.GetRawText();
            case System.Text.Json.JsonValueKind.True:
                return true;
            case System.Text.Json.JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }

    internal static Dictionary<string, object?> FromTomlTable(Tomlyn.Model.TomlTable table) {
        Dictionary<string, object?> dict = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.Generic.KeyValuePair<string, object> kv in table) {
            dict[kv.Key] = FromTomlValue(kv.Value);
        }
        return dict;
    }

    internal static object? FromTomlValue(object? value) {
        if (value is null) {
            return null;
        }

        if (value is Tomlyn.Model.TomlTable tt) {
            return FromTomlTable(tt);
        }

        if (value is Tomlyn.Model.TomlTableArray ta) {
            List<object?> list = new List<object?>();
            foreach (Tomlyn.Model.TomlTable item in ta) {
                list.Add(FromTomlValue(item));
            }
            return list;
        }

        if (value is Tomlyn.Model.TomlArray arr) {
            List<object?> list = new List<object?>();
            foreach (object? item in arr) {
                list.Add(FromTomlValue(item));
            }
            return list;
        }

        return value;
    }
}

