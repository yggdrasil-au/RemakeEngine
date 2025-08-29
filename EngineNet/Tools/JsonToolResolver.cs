using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RemakeEngine.Tools;

/// <summary>
/// Loads tool paths from JSON. Supports either:
///  - { "ffmpeg": "C:/path/ffmpeg.exe", ... }
///  - { "ffmpeg": { "exe": "./Tools/ffmpeg/bin/ffmpeg.exe", ... }, ... }
/// Unknown shapes are ignored. Relative paths resolve relative to the JSON file.
/// </summary>
public sealed class JsonToolResolver:IToolResolver {
    private readonly Dictionary<string, string> _tools = new(System.StringComparer.OrdinalIgnoreCase);
    private readonly string _baseDir;

    public JsonToolResolver(string jsonPath) {
        _baseDir = Path.GetDirectoryName(Path.GetFullPath(jsonPath)) ?? Directory.GetCurrentDirectory();
        using var stream = File.OpenRead(jsonPath);
        using var doc = JsonDocument.Parse(stream);
        if (doc.RootElement.ValueKind == JsonValueKind.Object) {
            foreach (var prop in doc.RootElement.EnumerateObject()) {
                var path = ExtractPath(prop.Value);
                if (!string.IsNullOrWhiteSpace(path)) {
                    var resolved = path!;
                    if (!Path.IsPathRooted(resolved))
                        resolved = Path.GetFullPath(Path.Combine(_baseDir, resolved));
                    _tools[prop.Name] = resolved;
                }
            }
        }
    }

    private static string? ExtractPath(JsonElement value) {
        switch (value.ValueKind) {
            case JsonValueKind.String:
                return value.GetString();
            case JsonValueKind.Object:
                if (value.TryGetProperty("exe", out var exe) && exe.ValueKind == JsonValueKind.String)
                    return exe.GetString();
                if (value.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String)
                    return path.GetString();
                if (value.TryGetProperty("command", out var cmd) && cmd.ValueKind == JsonValueKind.String)
                    return cmd.GetString();
                return null;
            default:
                return null;
        }
    }

    public string ResolveToolPath(string toolId) {
        if (_tools.TryGetValue(toolId, out var path))
            return path;
        // Fallback to PATH lookup by returning the id
        return toolId;
    }
}
