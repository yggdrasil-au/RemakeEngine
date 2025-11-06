using System.Collections.Generic;
using System.Diagnostics;
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
    /// Exposes the loaded data as a read-only dictionary interface.
    /// Keys are case-insensitive due to the underlying comparer.
    /// </summary>
    internal IDictionary<string, object?> Data => _data;

    // Backing store for Data. Uses case-insensitive comparison (OrdinalIgnoreCase).
    private Dictionary<string, object?> _data = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);

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
                Dictionary<string, object?>? dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(fs, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                // Step 6: Ensure we never return null.
                return dict ?? new Dictionary<string, object?>();
            }
        } catch {
            #if DEBUG
            Trace.WriteLine($"[EngineConfig] Failed to load or parse JSON config file at '{filePath}'. Returning empty config.");
            #endif
        }

        // Step 8: Missing file or error path -> empty config (safe default).
        return new Dictionary<string, object?>();
    }

}

