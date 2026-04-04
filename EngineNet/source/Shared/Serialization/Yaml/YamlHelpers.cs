
namespace EngineNet.Shared.Serialization.Yaml;

/// <summary>
/// Reusable YAML read/write utilities built on YamlDotNet.
/// Converts YAML mappings/sequences/scalars to plain .NET objects:
/// - Mappings -> Dictionary(string, object)
/// - Sequences -> List(object)
/// - Scalars -> primitive CLR types
/// </summary>
internal static class YamlHelpers {
    internal static object ParseFileToPlainObject(string path) {
        string text = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : string.Empty;

        if (string.IsNullOrWhiteSpace(text)) {
            return new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        }

        return ParseDocumentToPlainObject(text);
    }

    internal static object ParseDocumentToPlainObject(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        }

        IDeserializer deserializer = new DeserializerBuilder().Build();
        object? model = deserializer.Deserialize<object>(text);
        object? plain = ConvertYamlToPlain(model);
        return plain ?? new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
    }

    internal static void WriteYamlFile(string path, object? data) {
        string document = WriteDocument(data);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");
        System.IO.File.WriteAllText(path, document);
    }

    internal static string WriteDocument(object? data) {
        object serializableRoot = ConvertPlainToYaml(data) ?? new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        ISerializer serializer = new SerializerBuilder().Build();
        return serializer.Serialize(serializableRoot);
    }

    private static object? ConvertYamlToPlain(object? value) {
        switch (value) {
            case null:
                return null;
            case IDictionary dict: {
                Dictionary<string, object?> map = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                foreach (DictionaryEntry entry in dict) {
                    string key = entry.Key?.ToString() ?? string.Empty;
                    map[key] = ConvertYamlToPlain(entry.Value);
                }
                return map;
            }
            case IEnumerable sequence when value is not string: {
                List<object?> list = new List<object?>();
                foreach (object? item in sequence) {
                    list.Add(ConvertYamlToPlain(item));
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
            Dictionary<string, object?> map = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in dict) {
                string key = entry.Key?.ToString() ?? string.Empty;
                map[key] = ConvertPlainToYaml(entry.Value);
            }
            return map;
        }

        if (value is IEnumerable sequence && value is not string) {
            List<object?> list = new List<object?>();
            foreach (object? item in sequence) {
                list.Add(ConvertPlainToYaml(item));
            }
            return list;
        }

        Dictionary<string, object?> reflected = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        System.Reflection.PropertyInfo[] props = value.GetType().GetProperties();
        foreach (System.Reflection.PropertyInfo prop in props) {
            if (!prop.CanRead) {
                continue;
            }

            reflected[prop.Name] = ConvertPlainToYaml(prop.GetValue(value));
        }

        return reflected;
    }
}
