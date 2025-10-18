
namespace EngineNet.Tools;

/// <summary>
/// Loads tool paths from JSON. Supports either:
///  - { "ffmpeg": "C:/path/ffmpeg.exe", ... }
///  - { "ffmpeg": { "exe": "./Tools/ffmpeg/bin/ffmpeg.exe", ... }, ... }
/// Unknown shapes are ignored. Relative paths resolve relative to the JSON file.
/// </summary>
public sealed class JsonToolResolver:IToolResolver {
    private readonly Dictionary<String, String> _tools = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly String _baseDir;

    public JsonToolResolver(String jsonPath) {
        _baseDir = Path.GetDirectoryName(Path.GetFullPath(jsonPath)) ?? Directory.GetCurrentDirectory();
        using FileStream stream = File.OpenRead(jsonPath);
        using JsonDocument doc = JsonDocument.Parse(stream);
        if (doc.RootElement.ValueKind == JsonValueKind.Object) {
            foreach (JsonProperty prop in doc.RootElement.EnumerateObject()) {
                String? path = ExtractPath(prop.Value);
                if (!String.IsNullOrWhiteSpace(path)) {
                    String resolved = path!;
                    if (!Path.IsPathRooted(resolved)) {
                        resolved = Path.GetFullPath(Path.Combine(_baseDir, resolved));
                    }

                    _tools[prop.Name] = resolved;
                }
            }
        }
    }

    private static String? ExtractPath(JsonElement value) {
        switch (value.ValueKind) {
            case JsonValueKind.String:
                return value.GetString();
            case JsonValueKind.Object:
                if (value.TryGetProperty("exe", out JsonElement exe) && exe.ValueKind == JsonValueKind.String) {
                    return exe.GetString();
                }

                if (value.TryGetProperty("path", out JsonElement path) && path.ValueKind == JsonValueKind.String) {
                    return path.GetString();
                }

                if (value.TryGetProperty("command", out JsonElement cmd) && cmd.ValueKind == JsonValueKind.String) {
                    return cmd.GetString();
                }

                return null;
            default:
                return null;
        }
    }

    public String ResolveToolPath(String toolId) {
        if (_tools.TryGetValue(toolId, out String? path)) {
            return path;
        }
        // Fallback to PATH lookup by returning the id
        return toolId;
    }
}
