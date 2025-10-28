namespace EngineNet.Core.ScriptEngines.Helpers;

/// <summary>
/// Reusable TOML read/write utilities built on Tomlyn.
/// Converts between Tomlyn.Model.TomlTable/TomlArray/TomlTableArray and plain .NET objects:
/// - Dictionaries (string -> object) map to TOML tables
/// - Lists/arrays map to TOML arrays
/// - Lists of dictionaries map to arrays-of-tables
/// - Primitives map to their TOML counterparts
/// </summary>
internal static class TomlHelpers {
    public static object ParseFileToPlainObject(string path) {
        string text = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : string.Empty;
        return ParseToPlainObject(text);
    }

    public static object ParseToPlainObject(string toml) {
        Tomlyn.Model.TomlTable model = Tomlyn.Toml.ToModel(toml ?? string.Empty);
        return ConvertTomlToPlain(model);
    }

    public static void WriteTomlFile(string path, object? data) {
        // Convert plain objects to Tomlyn.Model.TomlTable model and serialize
        Tomlyn.Model.TomlTable root = ConvertPlainToTomlTable(data) ?? new Tomlyn.Model.TomlTable();
        string text = Tomlyn.Toml.FromModel(root);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");
        System.IO.File.WriteAllText(path, text);
    }

    private static object ConvertTomlToPlain(object? value) {
        switch (value) {
            case null:
                return new Dictionary<string, object?>();
            case Tomlyn.Model.TomlTable tt:
                Dictionary<string, object?>? dict = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                foreach (string key in tt.Keys) {
                    object? v = tt[key];
                    dict[key] = ConvertTomlToPlain(v);
                }
                return dict;
            case Tomlyn.Model.TomlArray arr: {
                List<object?>? list = new List<object?>();
                foreach (object? item in arr) {
                    list.Add(ConvertTomlToPlain(item));
                }
                return list;
            }
            case Tomlyn.Model.TomlTableArray taa: {
                List<object?>? list = new List<object?>();
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

    private static Tomlyn.Model.TomlTable? ConvertPlainToTomlTable(object? data) {
        if (data is null)
            return new Tomlyn.Model.TomlTable();

        if (data is Tomlyn.Model.TomlTable t)
            return t;

        if (data is IDictionary rawDict) {
            var table = new Tomlyn.Model.TomlTable();
            foreach (DictionaryEntry entry in rawDict) {
                if (entry.Key is null)
                    continue;
                string key = entry.Key.ToString()!;
                var val = ConvertPlainToTomlValue(entry.Value);
                if (val != null)
                    table[key] = val;
            }
            return table;
        }

        if (data is IEnumerable enumerable && data is not string) {
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

        // Normalize numeric types to produce stable TOML:
        // - If a double/float is mathematically an integer, write it as an integer (long) instead of 1.0
        //   This is important because Lua numbers come through as doubles, but many config values are intended as ints.
        if (value is double d) {
            if (!double.IsNaN(d) && !double.IsInfinity(d)) {
                double rounded = System.Math.Round(d);
                if (System.Math.Abs(d - rounded) < 1e-9 && rounded <= long.MaxValue && rounded >= long.MinValue) {
                    long asLong = (long)rounded;
                    if (asLong <= int.MaxValue && asLong >= int.MinValue)
                        return (int)asLong;
                    return asLong;
                }
            }
            return d;
        }
        if (value is float f) {
            if (!float.IsNaN(f) && !float.IsInfinity(f)) {
                double fd = f;
                double rounded = System.Math.Round(fd);
                if (System.Math.Abs(fd - rounded) < 1e-6 && rounded <= long.MaxValue && rounded >= long.MinValue) {
                    long asLong = (long)rounded;
                    if (asLong <= int.MaxValue && asLong >= int.MinValue)
                        return (int)asLong;
                    return asLong;
                }
            }
            return f;
        }

        // Preserve other TOML-supported primitives as-is
        if (value is string || value is bool || value is int || value is long || value is System.DateTime || value is System.DateTimeOffset)
            return value;

        if (value is Tomlyn.Model.TomlTable || value is Tomlyn.Model.TomlArray || value is Tomlyn.Model.TomlTableArray)
            return value;

        // IDictionary -> Tomlyn.Model.TomlTable
        if (value is IDictionary dict) {
            var table = new Tomlyn.Model.TomlTable();
            foreach (DictionaryEntry entry in dict) {
                if (entry.Key is null)
                    continue;
                string key = entry.Key.ToString()!;
                var val = ConvertPlainToTomlValue(entry.Value);
                if (val != null)
                    table[key] = val;
            }
            return table;
        }

        // IEnumerable -> Tomlyn.Model.TomlArray or Tomlyn.Model.TomlTableArray (arrays of tables)
        if (value is IEnumerable enumerable && value is not string) {
            var items = enumerable.Cast<object?>().ToList();
            bool allDicts = items.Count > 0 && items.All(x => x is IDictionary);
            if (allDicts) {
                var taa = new Tomlyn.Model.TomlTableArray();
                foreach (var item in items) {
                    var t = ConvertPlainToTomlTable(item);
                    if (t != null)
                        taa.Add(t);
                }
                return taa;
            } else {
                var arr = new Tomlyn.Model.TomlArray();
                foreach (var item in items) {
                    var v = ConvertPlainToTomlValue(item);
                    arr.Add(v ?? string.Empty);
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
}
