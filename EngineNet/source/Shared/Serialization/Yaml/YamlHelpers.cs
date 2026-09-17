
namespace EngineNet.Shared.Serialization.Yaml;

/// <summary>
/// Reusable YAML read/write utilities built on YamlDotNet.
/// Converts YAML mappings/sequences/scalars to plain .NET objects:
/// - Mappings -> Dictionary(string, object)
/// - Sequences -> List(object)
/// - Scalars -> primitive CLR types
/// </summary>
public static class YamlHelpers {
    public static object ParseFileToPlainObject(string path) {
        string text = System.IO.File.Exists(path: path) ? System.IO.File.ReadAllText(path: path) : string.Empty;

        if (string.IsNullOrWhiteSpace(text)) {
            return new Dictionary<string, object?>(comparer: System.StringComparer.OrdinalIgnoreCase);
        }

        return ParseDocumentToPlainObject(text: text);
    }

    public static object ParseDocumentToPlainObject(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return new Dictionary<string, object?>(comparer: System.StringComparer.OrdinalIgnoreCase);
        }

        IDeserializer deserializer = new DeserializerBuilder().Build();
        object? model = deserializer.Deserialize<object>(input: text);
        object? plain = ConvertYamlToPlain(model);
        return plain ?? new Dictionary<string, object?>(comparer: System.StringComparer.OrdinalIgnoreCase);
    }

    public static void WriteYamlFile(string path, object? data) {
        string document = WriteDocument(data: data);
        System.IO.Directory.CreateDirectory(path: System.IO.Path.GetDirectoryName(path: path) ?? ".");
        System.IO.File.WriteAllText(path: path, contents: document);
    }

    public static string WriteDocument(object? data) {
        object serializableRoot = ConvertPlainToYaml(data) ?? new Dictionary<string, object?>(comparer: System.StringComparer.OrdinalIgnoreCase);
        ISerializer serializer = new SerializerBuilder().Build();
        return serializer.Serialize(graph: serializableRoot);
    }

    private static object? ConvertYamlToPlain(object? value) {
        switch (value) {
            case null:
                return null;
            case IDictionary dict: {
                Dictionary<string, object?> map = new(comparer: System.StringComparer.OrdinalIgnoreCase);
                foreach (DictionaryEntry entry in dict) {
                    string key = entry.Key?.ToString() ?? string.Empty;
                    map[key: key] = ConvertYamlToPlain(entry.Value);
                }
                return map;
            }
            case IEnumerable sequence when value is not string: {
                List<object?> list = new();
                foreach (object? item in sequence) {
                    list.Add(item: ConvertYamlToPlain(item));
                }
                return list;
            }
            default:
                return value;
        }
    }

    private static object? ConvertPlainToYaml(object? value) {
        if (value is null) {
            return null;
        }

        if (value is string || value is bool || value is int || value is long || value is double || value is float || value is decimal || value is System.DateTime || value is System.DateTimeOffset) {
            return value;
        }

        if (value is IDictionary dict) {
            Dictionary<string, object?> map = new(comparer: System.StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in dict) {
                string key = entry.Key?.ToString() ?? string.Empty;
                map[key: key] = ConvertPlainToYaml(entry.Value);
            }
            return map;
        }

        if (value is IEnumerable sequence && value is not string) {
            List<object?> list = new();
            foreach (object? item in sequence) {
                list.Add(item: ConvertPlainToYaml(item));
            }
            return list;
        }

        Dictionary<string, object?> reflected = new(comparer: System.StringComparer.OrdinalIgnoreCase);
        System.Reflection.PropertyInfo[] props = value.GetType().GetProperties();
        foreach (System.Reflection.PropertyInfo prop in props) {
            if (!prop.CanRead) {
                continue;
            }

            reflected[key: prop.Name] = ConvertPlainToYaml(prop.GetValue(obj: value));
        }

        return reflected;
    }
}
