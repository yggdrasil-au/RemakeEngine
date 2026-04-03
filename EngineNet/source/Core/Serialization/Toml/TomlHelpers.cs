
using System.Collections;

namespace EngineNet.Core.Serialization.Toml;

/// <summary>
/// Reusable TOML read/write utilities built on Tomlyn.
/// Converts between Tomlyn.Model.TomlTable/TomlArray/TomlTableArray and plain .NET objects:
/// - Dictionaries (string -> object) map to TOML tables
/// - Lists/arrays map to TOML arrays
/// - Lists of dictionaries map to arrays-of-tables
/// - Primitives map to their TOML counterparts
/// </summary>
internal static class TomlHelpers {
    internal static object ParseFileToPlainObject(string path) {
        string text = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : string.Empty;

        // Handle empty strings explicitly to avoid deserialization exceptions
        var model = string.IsNullOrWhiteSpace(text)
            ? new Tomlyn.Model.TomlTable()
            : Tomlyn.TomlSerializer.Deserialize<Tomlyn.Model.TomlTable>(text)!;

        // Convert the Tomlyn model (TomlTable/TomlArray) into standard .NET types
        // (Dictionary<string, object?> and List<object?>).
        // While TomlTable implements IDictionary, returning the raw model causes type-check
        // failures in core engine components like OperationsLoader.cs and Lua script engines
        // which expect standard .NET collections for recursive iteration and duck-typing.
        return ConvertTomlToPlain(model);
    }

    internal static void WriteTomlFile(string path, object? data) {
        // Convert plain objects to Tomlyn.Model.TomlTable model and serialize
        Tomlyn.Model.TomlTable root = ConvertPlainToTomlTable(data);
        string text = Tomlyn.TomlSerializer.Serialize(root);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");
        System.IO.File.WriteAllText(path, text);
    }

    private static object ConvertTomlToPlain(object? value) {
        switch (value) {
            case null:
                return new Dictionary<string, object?>();
            case Tomlyn.Model.TomlTable tt:
                Dictionary<string, object?> dict = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                foreach (string key in tt.Keys) {
                    object v = tt[key];
                    dict[key] = ConvertTomlToPlain(v);
                }
                return dict;
            case Tomlyn.Model.TomlArray arr: {
                List<object?> list = new List<object?>();
                foreach (object? item in arr) {
                    list.Add(ConvertTomlToPlain(item));
                }
                return list;
            }
            case Tomlyn.Model.TomlTableArray taa: {
                List<object?> list = new List<object?>();
                foreach (var t in taa) {
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
            case System.DateTime dt:
                return dt;
            case System.DateTimeOffset dto:
                return dto;
            default:
                // Try to keep unknowns as-is; Tomlyn may expose other numeric types
                return value;
        }
    }

    private static Tomlyn.Model.TomlTable ConvertPlainToTomlTable(object? data) {
        if (data is null)
            return new Tomlyn.Model.TomlTable();

        if (data is Tomlyn.Model.TomlTable t)
            return t;

        if (data is IDictionary rawDict) {
            var table = new Tomlyn.Model.TomlTable();
            foreach (DictionaryEntry entry in rawDict) {
                //if (entry.Key is null)
                //    continue;
                string key = entry.Key.ToString()!;
                var val = ConvertPlainToTomlValue(entry.Value);
                if (val != null)
                    table[key] = val;
            }
            return table;
        }

        if (data is IEnumerable enumerable and not string) {
            // If the root is a list, wrap it under a single key "root"? Better to coerce into a table
            // For our purposes we expect a table at the root. Create a table with a single key if needed.
            var table = new Tomlyn.Model.TomlTable();
            var rootVal = ConvertPlainToTomlValue(enumerable);
            if (rootVal != null)
                table["root"] = rootVal;
            return table;
        }

        // Primitive at root -> put under "value"
        var t2 = new Tomlyn.Model.TomlTable();
        var v2 = ConvertPlainToTomlValue(data);
        if (v2 != null)
            t2["value"] = v2;
        return t2;
    }

    private static object? ConvertPlainToTomlValue(object? value) {
        if (value is null)
            return null;

        // Preserve supported primitives as-is.
        // Lua-specific double-to-int normalization is handled at the Lua boundary in LuaUtilities.cs
        if (value is string || value is bool || value is int || value is long || value is double || value is float || value is System.DateTime || value is System.DateTimeOffset)
            return value;

        if (value is Tomlyn.Model.TomlTable || value is Tomlyn.Model.TomlArray || value is Tomlyn.Model.TomlTableArray)
            return value;

        // IDictionary -> Tomlyn.Model.TomlTable
        if (value is IDictionary dict) {
            var table = new Tomlyn.Model.TomlTable();
            foreach (DictionaryEntry entry in dict) {
                //if (entry.Key is null)
                //    continue;
                string key = entry.Key.ToString()!;
                object? val = ConvertPlainToTomlValue(entry.Value);
                if (val != null)
                    table[key] = val;
            }
            return table;
        }

        // IEnumerable -> Tomlyn.Model.TomlArray or Tomlyn.Model.TomlTableArray (arrays of tables)
        if (value is IEnumerable enumerable and not string) {
            var items = enumerable.Cast<object?>().ToList();
            bool allDicts = items.Count > 0 && items.All(x => x is IDictionary);
            if (allDicts) {
                var taa = new Tomlyn.Model.TomlTableArray();
                foreach (var table in items.Select(ConvertPlainToTomlTable).Where(_ => true)) {
                    taa.Add(table);
                }
                return taa;
            } else {
                var arr = new Tomlyn.Model.TomlArray();
                foreach (var entryValue in items.Select(ConvertPlainToTomlValue)) {
                    arr.Add(entryValue ?? string.Empty);
                }
                return arr;
            }
        }

        // Fallback: try to reflect into a dictionary of properties
        var props = value.GetType().GetProperties()
            .Where(p => p.CanRead)
            .ToDictionary(p => p.Name, p => p.GetValue(value), System.StringComparer.OrdinalIgnoreCase);
        return ConvertPlainToTomlValue(props);
    }

    internal static string WriteDocument(object? data) {
        Tomlyn.Model.TomlTable root = ConvertPlainToTomlTable(data);
        return Tomlyn.TomlSerializer.Serialize(root);
    }

    /// <summary>
    /// Specialized helper to read the [[tool]] array of tables from module tool manifests.
    /// </summary>
    internal static List<Dictionary<string, object?>> ReadTools(string path) {
        if (!System.IO.File.Exists(path)) return new List<Dictionary<string, object?>>();

        object parsed = ParseFileToPlainObject(path);
        if (parsed is IDictionary<string, object?> root && root.TryGetValue("tool", out object? toolsObj)
            && toolsObj is System.Collections.IEnumerable toolsList) {
            return toolsList.Cast<object>().OfType<IDictionary<string, object?>>()
                .Select(d => d.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, System.StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
        return new List<Dictionary<string, object?>>();
    }

    /// <summary>
    /// Specialized helper to read and merge [[placeholders]] blocks from config files.
    /// </summary>
    internal static Dictionary<string, object?> ReadPlaceholdersFile(string path) {
        var result = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        if (!System.IO.File.Exists(path)) return result;

        object parsed = ParseFileToPlainObject(path);
        if (parsed is IDictionary<string, object?> root && root.TryGetValue("placeholders", out object? placeholdersObj)
            && placeholdersObj is System.Collections.IEnumerable placeholdersList) {
            foreach (object item in placeholdersList) {
                if (item is IDictionary<string, object?> table) {
                    foreach (var kvp in table) {
                        result[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
        return result;
    }
}
