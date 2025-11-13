using MoonSharp.Interpreter;

using System.Collections.Generic;

namespace EngineNet.ScriptEngines.LuaModules;

/// <summary>
/// Utility methods for converting between Lua and .NET data types.
/// </summary>
internal static class LuaUtilities {
    internal static DynValue ToDynValue(Script lua, object? value) {
        if (value is null || value is System.DBNull)
            return DynValue.Nil;

        // Map common primitives directly first
        switch (value) {
            case bool b: return DynValue.NewBoolean(b);
            case byte bt: return DynValue.NewNumber(bt);
            case sbyte sb: return DynValue.NewNumber(sb);
            case short i16: return DynValue.NewNumber(i16);
            case ushort ui16: return DynValue.NewNumber(ui16);
            case int i32: return DynValue.NewNumber(i32);
            case uint ui32: return DynValue.NewNumber(ui32);
            case long i64: return DynValue.NewNumber(i64);
            case ulong ui64: return DynValue.NewNumber(ui64);
            case float f: return DynValue.NewNumber(f);
            case double d: return DynValue.NewNumber(d);
            case decimal dec: return DynValue.NewNumber((double)dec);
            case System.DateTime dt: return DynValue.NewString(dt.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
            case System.DateTimeOffset dto: return DynValue.NewString(dto.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
            case byte[] bytes: return DynValue.NewString(System.Convert.ToHexString(bytes));
            case string s: return DynValue.NewString(s);
        }

        // IDictionary -> Lua table with string keys
        if (value is System.Collections.IDictionary idict) {
            Table t = new Table(lua);
            foreach (System.Collections.DictionaryEntry entry in idict) {
                string key = entry.Key?.ToString() ?? string.Empty;
                t[key] = ToDynValue(lua, entry.Value);
            }
            return DynValue.NewTable(t);
        }

        // IEnumerable -> Lua array-like table (1-based)
        if (value is System.Collections.IEnumerable ienum && value is not string) {
            Table t = new Table(lua);
            int i = 1;
            foreach (object? item in ienum) {
                t[i++] = ToDynValue(lua, item);
            }
            return DynValue.NewTable(t);
        }

        // Fallback to string representation
        return DynValue.NewString(value.ToString() ?? string.Empty);
    }

    internal static IDictionary<string, object?> TableToDictionary(Table table) {
        Dictionary<string, object?> dict = new Dictionary<string, object?>(System.StringComparer.Ordinal);
        foreach (TablePair pair in table.Pairs) {
            // Convert key to string
            string key = pair.Key.Type switch {
                DataType.String => pair.Key.String,
                DataType.Number => pair.Key.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => pair.Key.ToPrintString()
            };
            dict[key] = FromDynValue(pair.Value);
        }
        return dict;
    }

    internal static object? FromDynValue(DynValue v) => v.Type switch {
        DataType.Nil or DataType.Void => null,
        DataType.Boolean => v.Boolean,
        DataType.Number => v.Number,
        DataType.String => v.String,
        DataType.Table => TableToPlainObject(v.Table),
        _ => v.ToPrintString()
    };

    internal static object TableToPlainObject(Table t) {
        // Heuristic: if all keys are consecutive 1..n numbers, treat as array
        int count = 0;
        bool arrayLike = true;
        foreach (TablePair pair in t.Pairs) {
            count++;
            if (pair.Key.Type != DataType.Number) {
                arrayLike = false;
            }
        }
        if (arrayLike) {
            List<object?> list = new List<object?>(count);
            for (int i = 1; i <= count; i++) {
                DynValue dv = t.Get(i);
                list.Add(FromDynValue(dv));
            }
            return list;
        }
        return TableToDictionary(t);
    }

    internal static List<string> TableToStringList(Table t) {
        List<string> list = new List<string>();
        // Iterate up to the numeric length; stop when we hit a Nil entry
        for (int i = 1; i <= t.Length; i++) {
            DynValue dv = t.Get(i);
            if (dv.Type == DataType.Nil || dv.Type == DataType.Void) {
                break;
            }

            string s = dv.Type == DataType.String ? dv.String : dv.ToPrintString();
            list.Add(s);
        }
        return list;
    }

    internal static DynValue JsonElementToDynValue(Script lua, System.Text.Json.JsonElement el) {
        switch (el.ValueKind) {
            case System.Text.Json.JsonValueKind.Object:
                Table t = new Table(lua);
                foreach (System.Text.Json.JsonProperty p in el.EnumerateObject()) {
                    t[p.Name] = JsonElementToDynValue(lua, p.Value);
                }
                return DynValue.NewTable(t);
            case System.Text.Json.JsonValueKind.Array:
                Table arr = new Table(lua);
                int i = 1;
                foreach (System.Text.Json.JsonElement item in el.EnumerateArray()) {
                    arr[i++] = JsonElementToDynValue(lua, item);
                }
                return DynValue.NewTable(arr);
            case System.Text.Json.JsonValueKind.String:
                return DynValue.NewString(el.GetString() ?? string.Empty);
            case System.Text.Json.JsonValueKind.Number:
                if (el.TryGetDouble(out double d)) {
                    return DynValue.NewNumber(d);
                }

                return DynValue.NewNumber(0);
            case System.Text.Json.JsonValueKind.True:
                return DynValue.True;
            case System.Text.Json.JsonValueKind.False:
                return DynValue.False;
            case System.Text.Json.JsonValueKind.Null:
            case System.Text.Json.JsonValueKind.Undefined:
            default:
                return DynValue.Nil;
        }
    }
}