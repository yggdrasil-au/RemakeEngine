
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections;

namespace EngineNet.Tools;

/// <summary>
/// Loads tool paths from JSON. Supports either:
///  - { "ffmpeg": "C:/path/ffmpeg.exe", ... }
///  - { "ffmpeg": { "exe": "./Tools/ffmpeg/bin/ffmpeg.exe", ... }, ... }
/// Unknown shapes are ignored. Relative paths resolve relative to the JSON file.
/// </summary>
internal sealed class JsonToolResolver:IToolResolver {
    private readonly Dictionary<string, string> _tools = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

    internal JsonToolResolver(string jsonPath) {
        string _baseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(jsonPath)) ?? System.IO.Directory.GetCurrentDirectory();
        using System.IO.FileStream stream = System.IO.File.OpenRead(jsonPath);
        using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(stream);
        if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object) {
            foreach (System.Text.Json.JsonProperty prop in doc.RootElement.EnumerateObject()) {
                string? path = ExtractPath(prop.Value);
                if (!string.IsNullOrWhiteSpace(path)) {
                    string resolved = path!;
                    if (!System.IO.Path.IsPathRooted(resolved)) {
                        resolved = System.IO.Path.GetFullPath(System.IO.Path.Combine(_baseDir, resolved));
                    }

                    _tools[prop.Name] = resolved;
                }
            }
        }
    }

    private static string? ExtractPath(System.Text.Json.JsonElement value) {
        switch (value.ValueKind) {
            case System.Text.Json.JsonValueKind.String:
                return value.GetString();
            case System.Text.Json.JsonValueKind.Object:
                if (value.TryGetProperty("exe", out System.Text.Json.JsonElement exe) && exe.ValueKind == System.Text.Json.JsonValueKind.String) {
                    return exe.GetString();
                }

                if (value.TryGetProperty("path", out System.Text.Json.JsonElement path) && path.ValueKind == System.Text.Json.JsonValueKind.String) {
                    return path.GetString();
                }

                if (value.TryGetProperty("command", out System.Text.Json.JsonElement cmd) && cmd.ValueKind == System.Text.Json.JsonValueKind.String) {
                    return cmd.GetString();
                }

                return null;
            default:
                return null;
        }
    }

    public string ResolveToolPath(string toolId) {
        if (_tools.TryGetValue(toolId, out string? path)) {
            return path;
        }
        // Fallback to PATH lookup by returning the id
        return toolId;
    }
}
