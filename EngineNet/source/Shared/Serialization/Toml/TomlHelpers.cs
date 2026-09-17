
namespace EngineNet.Shared.Serialization.Toml;

using Tomlyn.Model;

/// <summary>
/// Reusable TOML read/write utilities built on Tomlyn.
/// Converts between Tomlyn.Model.TomlTable/TomlArray/TomlTableArray and plain .NET objects:
/// - Dictionaries (string -> object) map to TOML tables
/// - Lists/arrays map to TOML arrays
/// - Lists of dictionaries map to arrays-of-tables
/// - Primitives map to their TOML counterparts
/// </summary>
public static class TomlHelpers {
    public static object ParseFileToPlainObject(string path) {
        string text = System.IO.File.Exists(path: path) ? System.IO.File.ReadAllText(path: path) : string.Empty;

        // Handle empty strings explicitly to avoid deserialization exceptions
        TomlTable model = string.IsNullOrWhiteSpace(text)
            ? new Tomlyn.Model.TomlTable()
            : Tomlyn.TomlSerializer.Deserialize<Tomlyn.Model.TomlTable>(toml: text)!;

        // Convert the Tomlyn model (TomlTable/TomlArray) into standard .NET types
        // (Dictionary<string, object?> and List<object?>).
        // While TomlTable implements IDictionary, returning the raw model causes type-check
        // failures in core engine components like OperationsLoader.cs and Lua script engines
        // which expect standard .NET collections for recursive iteration and duck-typing.
        return ConvertTomlToPlain(model);
    }

    public static void WriteTomlFile(string path, object? data) {
        // Convert plain objects to Tomlyn.Model.TomlTable model and serialize
        Tomlyn.Model.TomlTable root = ConvertPlainToTomlTable(data: data);
        string text = Tomlyn.TomlSerializer.Serialize(root);
        System.IO.Directory.CreateDirectory(path: System.IO.Path.GetDirectoryName(path: path) ?? ".");
        System.IO.File.WriteAllText(path: path, contents: text);
    }

    private static object ConvertTomlToPlain(object? value) {
        switch (value) {
            case null:
                return new Dictionary<string, object?>();
            case Tomlyn.Model.TomlTable tt:
                Dictionary<string, object?> dict = new(comparer: System.StringComparer.OrdinalIgnoreCase);
                foreach (string key in tt.Keys) {
                    object v = tt[key: key];
                    dict[key: key] = ConvertTomlToPlain(v);
                }
                return dict;
            case Tomlyn.Model.TomlArray arr: {
                List<object?> list = new();
                foreach (object? item in arr) {
                    list.Add(item: ConvertTomlToPlain(item));
                }
                return list;
            }
            case Tomlyn.Model.TomlTableArray taa: {
                List<object?> list = new();
                foreach (TomlTable t in taa) {
                    list.Add(item: ConvertTomlToPlain(t));
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
        switch (data) {
            case null:
                return new Tomlyn.Model.TomlTable();
            case Tomlyn.Model.TomlTable t:
                return t;
            case IDictionary rawDict: {
                TomlTable table = new();
                foreach (DictionaryEntry entry in rawDict) {
                    //if (entry.Key is null)
                    //    continue;
                    string key = entry.Key.ToString()!;
                    object? val = ConvertPlainToTomlValue(entry.Value);
                    if (val != null) {
                        table[key: key] = val;
                    }
                }
                return table;
            }
        }
        if (data is IEnumerable enumerable and not string) {
            // If the root is a list, wrap it under a single key "root"? Better to coerce into a table
            // For our purposes we expect a table at the root. Create a table with a single key if needed.
            TomlTable table = new();
            object? rootVal = ConvertPlainToTomlValue(enumerable);
            if (rootVal != null)
                table[key: "root"] = rootVal;
            return table;
        }
        // Primitive at root -> put under "value"
        TomlTable t2 = new();
        object? v2 = ConvertPlainToTomlValue(data);
        if (v2 != null)
            t2[key: "value"] = v2;
        return t2;
    }

    private static object? ConvertPlainToTomlValue(object? value) {
        switch (value) {
            case null:
                return null;
            // Preserve supported primitives as-is.
            // Lua-specific double-to-int normalization is handled at the Lua boundary in LuaUtilities.cs
            case string or bool or int or long or double or float or System.DateTime or System.DateTimeOffset:
            case Tomlyn.Model.TomlTable or Tomlyn.Model.TomlArray or Tomlyn.Model.TomlTableArray:
                return value;
            // IDictionary -> Tomlyn.Model.TomlTable
            case IDictionary dict: {
                TomlTable table = new();
                foreach (DictionaryEntry entry in dict) {
                    //if (entry.Key is null)
                    //    continue;
                    string key = entry.Key.ToString()!;
                    object? val = ConvertPlainToTomlValue(entry.Value);
                    if (val != null)
                        table[key: key] = val;
                }
                return table;
            }
        }

        // IEnumerable -> Tomlyn.Model.TomlArray or Tomlyn.Model.TomlTableArray (arrays of tables)
        if (value is IEnumerable enumerable and not string) {
            List<object?> items = enumerable.Cast<object?>().ToList();
            bool allDicts = items.Count > 0 && items.All(predicate: x => x is IDictionary);
            if (allDicts) {
                TomlTableArray taa = new();
                foreach (TomlTable table in items.Select(selector: ConvertPlainToTomlTable).Where(predicate: _ => true)) {
                    taa.Add(item: table);
                }
                return taa;
            } else {
                TomlArray arr = new();
                foreach (object? entryValue in items.Select(selector: ConvertPlainToTomlValue)) {
                    arr.Add(item: entryValue ?? string.Empty);
                }
                return arr;
            }
        }

        // Fallback: try to reflect into a dictionary of properties
        Dictionary<string, object?> props = value.GetType().GetProperties()
            .Where(predicate: p => p.CanRead)
            .ToDictionary(keySelector: p => p.Name, elementSelector: p => p.GetValue(obj: value), comparer: System.StringComparer.OrdinalIgnoreCase);
        return ConvertPlainToTomlValue(props);
    }

    public static string WriteDocument(object? data) {
        Tomlyn.Model.TomlTable root = ConvertPlainToTomlTable(data: data);
        return Tomlyn.TomlSerializer.Serialize(root);
    }

    /// <summary>
    /// Specialized helper to read the [[tool]] array of tables from module tool manifests.
    /// </summary>
    public static List<Dictionary<string, object?>> ReadTools(string path) {
        if (!System.IO.File.Exists(path: path)) return new List<Dictionary<string, object?>>();

        object parsed = ParseFileToPlainObject(path: path);
        if (parsed is IDictionary<string, object?> root && root.TryGetValue(key: "tool", out object? toolsObj)
            && toolsObj is System.Collections.IEnumerable toolsList) {
            return toolsList.Cast<object>().OfType<IDictionary<string, object?>>()
                .Select(selector: d => d.ToDictionary(keySelector: kvp => kvp.Key, elementSelector: kvp => kvp.Value, comparer: System.StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
        return new List<Dictionary<string, object?>>();
    }

    /// <summary>
    /// Specialized helper to read and merge [[placeholders]] blocks from config files.
    /// </summary>
    public static Dictionary<string, object?> ReadPlaceholdersFile(string path) {
        Dictionary<string, object?> result = new(comparer: System.StringComparer.OrdinalIgnoreCase);
        if (!System.IO.File.Exists(path: path)) return result; // return empty if file doesn't exist

        object parsed = ParseFileToPlainObject(path: path); // this should give us a Dictionary<string, object?> representing the root TOML table
        // if the root is a table and has a "placeholders" key whose value is a list of tables, merge all those tables into one dictionary and return it. This allows us to support multiple [[placeholders]] blocks in the same file, which is useful for modular config files where each module can define its own placeholders without worrying about merging with other modules.
        if (parsed is not IDictionary<string, object?> root || !root.TryGetValue(key: "placeholders", out object? placeholdersObj) || placeholdersObj is not System.Collections.IEnumerable placeholdersList) {
            return result;
        }

        // Merge all tables in the [[placeholders]] array into the result dictionary. Later tables overwrite earlier ones in case of key conflicts, allowing for modular overrides.
        foreach (object item in placeholdersList) {
            if (item is not IDictionary<string, object?> table) continue;
            foreach (KeyValuePair<string, object?> kvp in table) {
                result[key: kvp.Key] = kvp.Value;
            }
        }
        return result;
    }
}
