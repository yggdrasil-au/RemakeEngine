using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using MoonSharp.Interpreter;

namespace EngineNet.Core.ScriptEngines.LuaModules;

/// <summary>
/// Utility methods for converting between Lua and .NET data types.
/// </summary>
internal static class LuaUtilities {
    public static DynValue ToDynValue(Script lua, Object? value) {
        if (value is null || value is DBNull)
            return DynValue.Nil;

        // Map common primitives directly first
        switch (value) {
            case Boolean b: return DynValue.NewBoolean(b);
            case Byte bt: return DynValue.NewNumber(bt);
            case SByte sb: return DynValue.NewNumber(sb);
            case Int16 i16: return DynValue.NewNumber(i16);
            case UInt16 ui16: return DynValue.NewNumber(ui16);
            case Int32 i32: return DynValue.NewNumber(i32);
            case UInt32 ui32: return DynValue.NewNumber(ui32);
            case Int64 i64: return DynValue.NewNumber(i64);
            case UInt64 ui64: return DynValue.NewNumber(ui64);
            case Single f: return DynValue.NewNumber(f);
            case Double d: return DynValue.NewNumber(d);
            case Decimal dec: return DynValue.NewNumber((Double)dec);
            case DateTime dt: return DynValue.NewString(dt.ToString("o", CultureInfo.InvariantCulture));
            case DateTimeOffset dto: return DynValue.NewString(dto.ToString("o", CultureInfo.InvariantCulture));
            case Byte[] bytes: return DynValue.NewString(Convert.ToHexString(bytes));
            case String s: return DynValue.NewString(s);
        }

        // IDictionary -> Lua table with string keys
        if (value is System.Collections.IDictionary idict) {
            Table t = new Table(lua);
            foreach (System.Collections.DictionaryEntry entry in idict) {
                String key = entry.Key?.ToString() ?? String.Empty;
                t[key] = ToDynValue(lua, entry.Value);
            }
            return DynValue.NewTable(t);
        }

        // IEnumerable -> Lua array-like table (1-based)
        if (value is System.Collections.IEnumerable ienum && value is not String) {
            Table t = new Table(lua);
            Int32 i = 1;
            foreach (Object? item in ienum) {
                t[i++] = ToDynValue(lua, item);
            }
            return DynValue.NewTable(t);
        }

        // Fallback to string representation
        return DynValue.NewString(value.ToString() ?? String.Empty);
    }

    public static IDictionary<String, Object?> TableToDictionary(Table table) {
        Dictionary<String, Object?> dict = new Dictionary<String, Object?>(StringComparer.Ordinal);
        foreach (TablePair pair in table.Pairs) {
            // Convert key to string
            String key = pair.Key.Type switch {
                DataType.String => pair.Key.String,
                DataType.Number => pair.Key.Number.ToString(CultureInfo.InvariantCulture),
                _ => pair.Key.ToPrintString()
            };
            dict[key] = FromDynValue(pair.Value);
        }
        return dict;
    }

    public static Object? FromDynValue(DynValue v) => v.Type switch {
        DataType.Nil or DataType.Void => null,
        DataType.Boolean => v.Boolean,
        DataType.Number => v.Number,
        DataType.String => v.String,
        DataType.Table => TableToPlainObject(v.Table),
        _ => v.ToPrintString()
    };

    public static Object TableToPlainObject(Table t) {
        // Heuristic: if all keys are consecutive 1..n numbers, treat as array
        Int32 count = 0;
        Boolean arrayLike = true;
        foreach (TablePair pair in t.Pairs) {
            count++;
            if (pair.Key.Type != DataType.Number) {
                arrayLike = false;
            }
        }
        if (arrayLike) {
            List<Object?> list = new List<Object?>(count);
            for (Int32 i = 1; i <= count; i++) {
                DynValue dv = t.Get(i);
                list.Add(FromDynValue(dv));
            }
            return list;
        }
        return TableToDictionary(t);
    }

    public static List<String> TableToStringList(Table t) {
        List<String> list = new List<String>();
        // Iterate up to the numeric length; stop when we hit a Nil entry
        for (Int32 i = 1; i <= t.Length; i++) {
            DynValue dv = t.Get(i);
            if (dv.Type == DataType.Nil || dv.Type == DataType.Void) {
                break;
            }

            String s = dv.Type == DataType.String ? dv.String : dv.ToPrintString();
            list.Add(s);
        }
        return list;
    }

    public static DynValue JsonElementToDynValue(Script lua, JsonElement el) {
        switch (el.ValueKind) {
            case JsonValueKind.Object:
                Table t = new Table(lua);
                foreach (JsonProperty p in el.EnumerateObject()) {
                    t[p.Name] = JsonElementToDynValue(lua, p.Value);
                }
                return DynValue.NewTable(t);
            case JsonValueKind.Array:
                Table arr = new Table(lua);
                Int32 i = 1;
                foreach (JsonElement item in el.EnumerateArray()) {
                    arr[i++] = JsonElementToDynValue(lua, item);
                }
                return DynValue.NewTable(arr);
            case JsonValueKind.String:
                return DynValue.NewString(el.GetString() ?? String.Empty);
            case JsonValueKind.Number:
                if (el.TryGetDouble(out Double d)) {
                    return DynValue.NewNumber(d);
                }

                return DynValue.NewNumber(0);
            case JsonValueKind.True:
                return DynValue.True;
            case JsonValueKind.False:
                return DynValue.False;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                return DynValue.Nil;
        }
    }
}