
namespace EngineNet.Shared.Serialization.Json;

internal sealed class JsonHelpers {

    /// <summary>
    /// Loads a JSON file and returns its contents as a dictionary.
    /// If the file is missing, malformed, or not a JSON object, an empty dictionary is returned.
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    internal static Dictionary<string, object?> LoadJsonFile(string filePath) {
        try {
            if (System.IO.File.Exists(filePath)) {
                using System.IO.FileStream fs = System.IO.File.OpenRead(filePath);
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(fs);

                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object) {
                    return Shared.Serialization.DocModelConverter.FromJsonObject(obj: doc.RootElement);
                }

                fs.Position = 0; // Rewind stream for a second read
                Dictionary<string, object?>? dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(fs, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                // Step 6: Ensure we never return null.
                return dict ?? new Dictionary<string, object?>();
            }
        } catch (System.Text.Json.JsonException ex) {
            Shared.IO.Diagnostics.Bug($"[JsonHelpers] JSON parsing error for file '{filePath}': {ex}");
        } catch (System.IO.IOException ex) {
            Shared.IO.Diagnostics.Bug($"[JsonHelpers] IO error reading file '{filePath}': {ex}");
        } catch (System.UnauthorizedAccessException ex) {
            Shared.IO.Diagnostics.Bug($"[JsonHelpers] Access denied reading JSON file '{filePath}': {ex}");
        } catch (System.ArgumentException ex) {
            Shared.IO.Diagnostics.Bug($"[JsonHelpers] Invalid JSON file path '{filePath}': {ex}");
        }

        // Step 8: Missing file or error path -> empty config (safe default).
        return new Dictionary<string, object?>();
    }

}

