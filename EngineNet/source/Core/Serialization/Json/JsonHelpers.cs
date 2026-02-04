using System.Collections.Generic;
using System.Text.Json;
using EngineNet.Core;
using EngineNet.Core.Serialization;

namespace EngineNet.Core.Serialization.Json;

internal sealed class JsonHelpers {

    internal static Dictionary<string, object?> LoadJsonFile(string filePath) {
        try {
            if (System.IO.File.Exists(filePath)) {
                using System.IO.FileStream fs = System.IO.File.OpenRead(filePath);
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(fs);

                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object) {
                    return Core.Serialization.DocModelConverter.FromJsonObject(obj: doc.RootElement);
                }

                fs.Position = 0; // Rewind stream for a second read
                Dictionary<string, object?>? dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(fs, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                // Step 6: Ensure we never return null.
                return dict ?? new Dictionary<string, object?>();
            }
        } catch {
            Core.Diagnostics.Bug($"[JsonHelpers] Failed to load or parse JSON config file at '{filePath}'. Returning empty config.");
        }

        // Step 8: Missing file or error path -> empty config (safe default).
        return new Dictionary<string, object?>();
    }

}

