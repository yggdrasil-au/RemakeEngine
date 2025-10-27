// ------------------------------------------------------------
// File: EngineNet/EngineConfig.cs
// Purpose: Load a JSON file into a case-insensitive dictionary,
//          support nested objects/arrays, numbers, booleans, and strings,
//          and allow reloading from disk.
// ------------------------------------------------------------

namespace EngineNet {
    /// <summary>
    /// Loads configuration from a JSON file into a case-insensitive dictionary.
    /// - Keys are looked up without regard to case (e.g., "Foo" == "foo").
    /// - Supports nested objects (as Dictionary&lt;string, object?&gt;), arrays (as List&lt;object?&gt;),
    ///   numbers (Int64 if possible, otherwise Double), booleans, strings, and null.
    /// - Can be reloaded from disk using <see cref="Reload"/>.
    /// </summary>
    public sealed class EngineConfig {
        /// <summary>
        /// The file system path to the JSON config file that this instance reads.
        /// </summary>
        public string Path {
            get;
        }

        /// <summary>
        /// Exposes the loaded data as a read-only dictionary interface.
        /// Keys are case-insensitive due to the underlying comparer.
        /// </summary>
        public IDictionary<string, object?> Data => _data;

        // Backing store for Data. Uses case-insensitive comparison (OrdinalIgnoreCase).
        private Dictionary<string, object?> _data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Constructs a config bound to a file path and immediately loads its content.
        /// </summary>
        /// <param name="path">Absolute or relative path to a JSON file.</param>
        public EngineConfig(string path) {
            Path = path;     // Step 1: Remember where to read from
            Reload();        // Step 2: Initial load from disk
        }

        /// <summary>
        /// Reloads configuration from <see cref="Path"/> and replaces the current data snapshot.
        /// Safe: if the file is missing or invalid, the data becomes an empty dictionary.
        /// </summary>
        public void Reload() {
            // Step: Re-parse the file and swap in the new dictionary
            _data = LoadJsonFile(Path);
        }

        /// <summary>
        /// Loads a JSON file into a case-insensitive dictionary:
        /// - If the file doesn't exist or JSON is invalid, returns an empty dictionary.
        /// - Objects become Dictionary&lt;string, object?&gt; (case-insensitive keys).
        /// - Arrays become List&lt;object?&gt;.
        /// - Numbers prefer Int64 when possible, else Double.
        /// - Booleans, strings, and nulls map naturally.
        /// </summary>
        /// <param name="filePath">Path to the JSON file.</param>
        /// <returns>Parsed dictionary; never null.</returns>
        public static Dictionary<string, object?> LoadJsonFile(string filePath) {
            try {
                // Step 1: If the file is absent, short-circuit with empty config.
                if (File.Exists(filePath)) {
                    // Step 2: Open a shared read-only stream for parsing.
                    using FileStream fs = File.OpenRead(filePath);

                    // Step 3: Parse into a DOM (JsonDocument) to inspect the root element.
                    using JsonDocument doc = JsonDocument.Parse(fs);

                    // Step 4: If the root is an object, convert it node-by-node for full control.
                    if (doc.RootElement.ValueKind == JsonValueKind.Object) {
                        // Convert to a .NET structure with case-insensitive dictionaries.
                        return ToDotNet(doc.RootElement) as Dictionary<string, object?>
                               ?? new Dictionary<string, object?>();
                    }

                    // Step 5 (Fallback): For simple top-level maps that aren't explicitly objects in the DOM,
                    // attempt direct deserialize into Dictionary<string, object?>.
                    fs.Position = 0; // Rewind stream for a second read
                    Dictionary<string, object?>? dict =
                        JsonSerializer.Deserialize<Dictionary<string, object?>>(fs,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    // Step 6: Ensure we never return null.
                    return dict ?? new Dictionary<string, object?>();
                }
            } catch {
                // Step 7: Swallow parsing/IO errors and fall through to empty map.
                // Rationale: Config consumers often prefer "no config" over hard failure on corrupt files.
            }

            // Step 8: Missing file or error path -> empty config (safe default).
            return new Dictionary<string, object?>();
        }

        /// <summary>
        /// Recursively converts a <see cref="JsonElement"/> to idiomatic .NET objects:
        /// - Object  ? Dictionary&lt;string, object?&gt; (case-insensitive keys)
        /// - Array   ? List&lt;object?&gt;
        /// - String  ? string
        /// - Number  ? Int64 if possible, otherwise Double, otherwise raw text
        /// - True/False ? bool
        /// - Null/Undefined ? null
        /// </summary>
        private static object? ToDotNet(JsonElement el) {
            switch (el.ValueKind) {
                case JsonValueKind.Object:
                    // Create a case-insensitive dictionary for object properties.
                    Dictionary<string, object?> obj = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                    // Enumerate each property and recursively convert its value.
                    foreach (JsonProperty prop in el.EnumerateObject()) {
                        obj[prop.Name] = ToDotNet(prop.Value); // Step: recurse on property value
                    }

                    return obj;

                case JsonValueKind.Array:
                    // Convert each item of the array recursively.
                    List<object?> list = new List<object?>();
                    foreach (JsonElement item in el.EnumerateArray()) {
                        list.Add(ToDotNet(item)); // Step: recurse on array element
                    }
                    return list;

                case JsonValueKind.String:
                    // Return the string value directly.
                    return el.GetString();

                case JsonValueKind.Number:
                    // Prefer Int64 if it fits exactly (e.g., 123).
                    if (el.TryGetInt64(out long l)) {
                        return l;
                    }

                    // Otherwise, try double precision (e.g., 3.14).
                    if (el.TryGetDouble(out double d)) {
                        return d;
                    }

                    // As a last resort, return the raw text (covers uncommon numeric forms).
                    return el.GetRawText();

                case JsonValueKind.True:
                    return true;

                case JsonValueKind.False:
                    return false;

                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                default:
                    // Represent JSON null/undefined as C# null.
                    return null;
            }
        }
    }
}
