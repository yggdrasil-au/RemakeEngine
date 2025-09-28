using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tomlyn;
using Tomlyn.Model;

namespace EngineNet.Core.ScriptEngines.Helpers;

/// <summary>
/// Reusable TOML read/write utilities built on Tomlyn.
/// Converts between TomlTable/TomlArray/TomlTableArray and plain .NET objects:
/// - Dictionaries (string -> object) map to TOML tables
/// - Lists/arrays map to TOML arrays
/// - Lists of dictionaries map to arrays-of-tables
/// - Primitives map to their TOML counterparts
/// </summary>
public static class TomlHelpers
{
    public static Object ParseFileToPlainObject(String path)
    {
        String text = File.Exists(path) ? File.ReadAllText(path) : String.Empty;
        return ParseToPlainObject(text);
    }

    public static Object ParseToPlainObject(String toml)
    {
        TomlTable model = Toml.ToModel(toml ?? String.Empty);
        return ConvertTomlToPlain(model);
    }

    public static void WriteTomlFile(String path, Object? data)
    {
        // Convert plain objects to TomlTable model and serialize
        TomlTable root = ConvertPlainToTomlTable(data) ?? new TomlTable();
        String text = Toml.FromModel(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, text);
    }

    private static Object ConvertTomlToPlain(Object? value)
    {
        switch (value)
        {
            case null:
                return new Dictionary<String, Object?>();
            case TomlTable tt:
                var dict = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var key in tt.Keys)
                {
                    var v = tt[key];
                    dict[key] = ConvertTomlToPlain(v);
                }
                return dict;
            case TomlArray arr:
            {
                var list = new List<Object?>();
                foreach (var item in arr)
                {
                    list.Add(ConvertTomlToPlain(item));
                }
                return list;
            }
            case TomlTableArray taa:
            {
                var list = new List<Object?>();
                foreach (var t in taa)
                {
                    list.Add(ConvertTomlToPlain(t));
                }
                return list;
            }
            // Primitive TOML values map directly
            case string s:
                return s;
            case bool b:
                return b;
            case int i:
                return i;
            case long l:
                return l;
            case double d:
                return d;
            case float f:
                return (double)f;
            case DateTime dt:
                return dt;
            case DateTimeOffset dto:
                return dto;
            default:
                // Try to keep unknowns as-is; Tomlyn may expose other numeric types
                return value;
        }
    }

    private static TomlTable? ConvertPlainToTomlTable(Object? data)
    {
        if (data is null)
            return new TomlTable();

        if (data is TomlTable t)
            return t;

        if (data is IDictionary rawDict)
        {
            var table = new TomlTable();
            foreach (DictionaryEntry entry in rawDict)
            {
                if (entry.Key is null) continue;
                String key = entry.Key.ToString()!;
                var val = ConvertPlainToTomlValue(entry.Value);
                if (val != null) table[key] = val;
            }
            return table;
        }

        if (data is IEnumerable enumerable && data is not string)
        {
            // If the root is a list, wrap it under a single key "root"? Better to coerce into a table
            // For our purposes we expect a table at the root. Create a table with a single key if needed.
            var table = new TomlTable();
            var rootVal = ConvertPlainToTomlValue(enumerable);
            if (rootVal != null) table["root"] = rootVal;
            return table;
        }

        // Primitive at root -> put under "value"
        var t2 = new TomlTable();
        var v2 = ConvertPlainToTomlValue(data);
        if (v2 != null) t2["value"] = v2;
        return t2;
    }

    private static Object? ConvertPlainToTomlValue(Object? value)
    {
        if (value is null) return null;

        // Preserve TOML-supported primitives
        if (value is string || value is bool || value is int || value is long || value is double || value is float || value is DateTime || value is DateTimeOffset)
            return value;

        if (value is TomlTable || value is TomlArray || value is TomlTableArray)
            return value;

        // IDictionary -> TomlTable
        if (value is IDictionary dict)
        {
            var table = new TomlTable();
            foreach (DictionaryEntry entry in dict)
            {
                if (entry.Key is null) continue;
                String key = entry.Key.ToString()!;
                var val = ConvertPlainToTomlValue(entry.Value);
                if (val != null) table[key] = val;
            }
            return table;
        }

        // IEnumerable -> TomlArray or TomlTableArray (arrays of tables)
        if (value is IEnumerable enumerable && value is not string)
        {
            var items = enumerable.Cast<Object?>().ToList();
            bool allDicts = items.Count > 0 && items.All(x => x is IDictionary);
            if (allDicts)
            {
                var taa = new TomlTableArray();
                foreach (var item in items)
                {
                    var t = ConvertPlainToTomlTable(item);
                    if (t != null) taa.Add(t);
                }
                return taa;
            }
            else
            {
                var arr = new TomlArray();
                foreach (var item in items)
                {
                    var v = ConvertPlainToTomlValue(item);
                    arr.Add(v ?? String.Empty);
                }
                return arr;
            }
        }

        // Fallback: try to reflect into a dictionary of properties
        var props = value.GetType().GetProperties()
            .Where(p => p.CanRead)
            .ToDictionary(p => p.Name, p => p.GetValue(value), StringComparer.OrdinalIgnoreCase);
        return ConvertPlainToTomlValue(props);
    }
}
