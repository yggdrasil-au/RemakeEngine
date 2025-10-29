
using System.Collections.Generic;

namespace EngineNet.Core.Utils;

internal sealed class Operations {

    internal static Dictionary<string, object?> ToMap(Tomlyn.Model.TomlTable table) {
        Dictionary<string, object?> dict = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, object> kv in table) {
            dict[kv.Key] = FromToml(kv.Value);
        }

        return dict;
    }
    internal static Dictionary<string, object?> ToMap(System.Text.Json.JsonElement obj) {
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
