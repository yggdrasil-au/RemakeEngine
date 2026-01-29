
using System.Collections.Generic;
using System.Linq;
using System;

using EngineNet.Core;

namespace EngineNet.Core.Tools;

/// <summary>
/// Loads tool paths from JSON. Supports either:
///  - { "ffmpeg": "C:/path/ffmpeg.exe", ... }
///  - { "ffmpeg": { "exe": "./Tools/ffmpeg/bin/ffmpeg.exe", ... }, ... }
/// Unknown shapes are ignored. Relative paths resolve relative to the JSON file.
/// Automatically reloads if the file changes or a higher-priority file appears.
/// </summary>
internal sealed class JsonToolResolver:IToolResolver {
    private readonly Dictionary<string, string> _tools = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    // //
    private readonly string[] _candidates;
    private string? _loadedFile;
    private System.DateTime _lastWriteTime;

    internal JsonToolResolver() {
        _candidates = new[] {
            System.IO.Path.Combine(Program.rootPath, "Tools.local.json"), // local cache of installed tools
            System.IO.Path.Combine(Program.rootPath, "tools.local.json"),
            System.IO.Path.Combine(Program.rootPath, "EngineApps", "Registries", "Tools", "Main.json"), // local registry of supported tools
            System.IO.Path.Combine(Program.rootPath, "EngineApps", "Registries", "Tools", "main.json")
        };
        Load();
    }

    /// <summary>
    /// Loads or reloads the tool definitions from the highest-priority JSON file.
    /// </summary>
    private void Load() {
        string? found = _candidates.FirstOrDefault(System.IO.File.Exists);

        // If no file found, clear tools and return
        if (found == null) {
            Core.Diagnostics.Trace("No tool definition file found.");
            if (_loadedFile != null) {
                _tools.Clear();
                _loadedFile = null;
                Core.Diagnostics.Trace("Cleared loaded tools.");
            }
            Core.Diagnostics.Trace("No tools loaded.");
            return;
        }

        // If we found a file, check if it's different from loaded or newer
        bool isNewFile = !string.Equals(found, _loadedFile, System.StringComparison.OrdinalIgnoreCase);
        System.DateTime writeTime = System.IO.File.GetLastWriteTimeUtc(found);

        // If it's new or updated, reload
        if (isNewFile || writeTime > _lastWriteTime) {
            // Only clear if switching files or reloading
            _tools.Clear();
            _loadedFile = found;
            _lastWriteTime = writeTime;

            try {
                string _baseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(found)) ?? System.IO.Directory.GetCurrentDirectory();
                using System.IO.FileStream stream = System.IO.File.OpenRead(found);
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
            } catch (System.Exception ex) {
                Core.Diagnostics.Log($"Error loading tools from {found}: {ex.Message}");
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
        Load(); // Check for updates
        if (_tools.TryGetValue(toolId, out string? path)) {
            return path;
        }
        // Fallback to PATH lookup by returning the id
        return toolId;
    }
}
