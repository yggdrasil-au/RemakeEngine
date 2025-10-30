using System.Text.Json;
using System.Collections.Generic;
namespace EngineNet.Core;

/// <summary>
/// Loads configuration from a JSON file into a case-insensitive dictionary.
/// - Keys are looked up without regard to case (e.g., "Foo" == "foo").
/// - Supports nested objects (as Dictionary&lt;string, object?&gt;), arrays (as List&lt;object?&gt;),
///   numbers (long if possible, otherwise Double), booleans, strings, and null.
/// - Can be reloaded from disk using <see cref="Reload"/>.
/// </summary>
internal sealed class EngineConfig {
    /// <summary>
    /// The file system path to the JSON config file that this instance reads.
    /// </summary>
    internal string Path {
        get;
    }

    /// <summary>
    /// Exposes the loaded data as a read-only dictionary interface.
    /// Keys are case-insensitive due to the underlying comparer.
    /// </summary>
    internal IDictionary<string, object?> Data => _data;

    // Backing store for Data. Uses case-insensitive comparison (OrdinalIgnoreCase).
    private Dictionary<string, object?> _data = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Constructs a config bound to a file path and immediately loads its content.
    /// </summary>
    /// <param name="path">Absolute or relative path to a JSON file.</param>
    internal EngineConfig(string path) {
        Path = path;     // Step 1: Remember where to read from
        Reload();        // Step 2: Initial load from disk
    }

    /// <summary>
    /// Reloads configuration from <see cref="Path"/> and replaces the current data snapshot.
    /// Safe: if the file is missing or invalid, the data becomes an empty dictionary.
    /// </summary>
    internal void Reload() {
        // Step: Re-parse the file and swap in the new dictionary
        _data = LoadJsonFile(Path);
    }

    /// <summary>
    /// Loads a JSON file into a case-insensitive dictionary:
    /// - If the file doesn't exist or JSON is invalid, returns an empty dictionary.
    /// - Objects become Dictionary&lt;string, object?&gt; (case-insensitive keys).
    /// - Arrays become List&lt;object?&gt;.
    /// - Numbers prefer long when possible, else Double.
    /// - Booleans, strings, and nulls map naturally.
    /// </summary>
    /// <param name="filePath">Path to the JSON file.</param>
    /// <returns>Parsed dictionary; never null.</returns>
    internal static Dictionary<string, object?> LoadJsonFile(string filePath) {
        try {
            // Step 1: If the file is absent, short-circuit with empty config.
            if (System.IO.File.Exists(filePath)) {
                // Step 2: Open a shared read-only stream for parsing.
                using System.IO.FileStream fs = System.IO.File.OpenRead(filePath);

                // Step 3: Parse into a DOM (JsonDocument) to inspect the root element.
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(fs);

                // Step 4: If the root is an object, convert it node-by-node for full control.
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object) {
                    // Convert to a .NET structure with case-insensitive dictionaries.
                    return Utils.Converters.DocModelConverter.FromJsonObject(obj: doc.RootElement);
                }

                // Step 5 (Fallback): For simple top-level maps that aren't explicitly objects in the DOM,
                // attempt direct deserialize into Dictionary<string, object?>.
                fs.Position = 0; // Rewind stream for a second read
                Dictionary<string, object?>? dict =
                    System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(fs,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

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
    /// - object  ? Dictionary&lt;string, object?&gt; (case-insensitive keys)
    /// - Array   ? List&lt;object?&gt;
    /// - string  ? string
    /// - Number  ? long if possible, otherwise Double, otherwise raw text
    /// - True/False ? bool
    /// - Null/Undefined ? null
    /// </summary>
    private static object? ToDotNet(System.Text.Json.JsonElement el) {
        // Legacy helper preserved for compatibility; delegate to unified converter.
        return Utils.Converters.DocModelConverter.FromJsonElement(el);
    }
}

