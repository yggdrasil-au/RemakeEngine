
using System.Collections.Generic;
using System.Linq;
using System;

using EngineNet.Core;

namespace EngineNet.Core.ExternalTools;

/// <summary>
/// Loads tool paths from JSON. Supports modular registry aggregation and version-aware resolution.
/// Prioritizes <see cref="ToolLockfile.ToolLockfileName"/> for persistent installations.
/// </summary>
internal sealed class JsonToolResolver:IToolResolver {
    // ToolName -> Version -> ExePath
    private readonly Dictionary<string, Dictionary<string, string>> _tools = new Dictionary<string, Dictionary<string, string>>(System.StringComparer.OrdinalIgnoreCase);

    private readonly string _lockfilePath;
    private string? _loadedFile;
    private System.DateTime _lastWriteTime;

    internal JsonToolResolver() {
        _lockfilePath = ToolLockfile.GetPath(Program.rootPath);
        Load();
    }

    /// <summary>
    /// Loads or reloads the tool definitions from the local tracking file.
    /// </summary>
    private void Load() {
        string? found = System.IO.File.Exists(_lockfilePath) ? _lockfilePath : null;

        if (found == null) {
            if (_loadedFile != null) {
                _tools.Clear();
                _loadedFile = null;
            }
            return;
        }

        bool isNewFile = !string.Equals(found, _loadedFile, System.StringComparison.OrdinalIgnoreCase);
        System.DateTime writeTime = System.IO.File.GetLastWriteTimeUtc(found);

        if (isNewFile || writeTime > _lastWriteTime) {
            _tools.Clear();
            _loadedFile = found;
            _lastWriteTime = writeTime;

            try {
                string _baseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(found)) ?? System.IO.Directory.GetCurrentDirectory();
                using System.IO.FileStream stream = System.IO.File.OpenRead(found);
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(stream);

                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object) {
                    foreach (System.Text.Json.JsonProperty toolProp in doc.RootElement.EnumerateObject()) {
                        string toolName = toolProp.Name;
                        var versions = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

                        if (toolProp.Value.ValueKind == System.Text.Json.JsonValueKind.Object) {
                            // Check if this is the new versioned structure or the old flat structure
                            bool isVersioned = false;
                            foreach (System.Text.Json.JsonProperty vProp in toolProp.Value.EnumerateObject()) {
                                // If sub-key is an object and contains an 'exe' or 'version', it's likely versioned
                                if (vProp.Value.ValueKind == System.Text.Json.JsonValueKind.Object) {
                                    string? path = ExtractPath(vProp.Value);
                                    if (path != null) {
                                        versions[vProp.Name] = ResolvePath(_baseDir, path);
                                        isVersioned = true;
                                    }
                                }
                            }

                            if (!isVersioned) {
                                // Fallback to old flat structure: { "ToolName": { "exe": "...", "version": "..." } }
                                string? path = ExtractPath(toolProp.Value);
                                if (path != null) {
                                    string version = "1.0.0";
                                    if (toolProp.Value.TryGetProperty("version", out System.Text.Json.JsonElement v) && v.ValueKind == System.Text.Json.JsonValueKind.String) {
                                        version = v.GetString() ?? "1.0.0";
                                    }
                                    versions[version] = ResolvePath(_baseDir, path);
                                }
                            }
                        } else if (toolProp.Value.ValueKind == System.Text.Json.JsonValueKind.String) {
                            // legacy simple mapping: { "ToolName": "path/to/exe" }
                            versions["1.0.0"] = ResolvePath(_baseDir, toolProp.Value.GetString() ?? string.Empty);
                        }

                        if (versions.Count > 0) {
                            _tools[toolName] = versions;
                        }
                    }
                }
            } catch (System.Exception ex) {
                Core.Diagnostics.Log($"[JsonToolResolver] Error loading tools from {found}: {ex.Message}");
            }
        }
    }

    private static string ResolvePath(string baseDir, string path) {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (!System.IO.Path.IsPathRooted(path)) {
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, path));
        }
        return path;
    }

    private static string? ExtractPath(System.Text.Json.JsonElement value) {
        if (value.ValueKind == System.Text.Json.JsonValueKind.String) return value.GetString();
        if (value.ValueKind != System.Text.Json.JsonValueKind.Object) return null;

        string[] possibleKeys = { "exe", "path", "command" };
        foreach (var key in possibleKeys) {
            if (value.TryGetProperty(key, out System.Text.Json.JsonElement elem) && elem.ValueKind == System.Text.Json.JsonValueKind.String) {
                return elem.GetString();
            }
        }
        return null;
    }

    public string ResolveToolPath(string toolId, string? version = null) {
        Load();

        if (_tools.TryGetValue(toolId, out var versions)) {
            if (version != null && versions.TryGetValue(version, out string? path)) {
                return path;
            }

            // If no version specified or not found, try to find the "latest"
            // For now, just pick the last one available (which should be latest if added chronologically)
            if (versions.Count > 0) {
                return versions.Values.Last();
            }
        }

        return toolId;
    }
}
