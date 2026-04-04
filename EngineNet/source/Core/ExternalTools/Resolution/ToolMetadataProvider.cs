using System.Text.Json;

namespace EngineNet.Core.ExternalTools;

/// <summary>
/// Provides metadata for tools (executable path and optional version) by
/// consulting <see cref="ToolLockfile.ToolLockfileName"/> when available, and falling back to EngineNet.Core.ExternalTools.JsonToolResolver.
/// </summary>
internal static class ToolMetadataProvider {

    internal static (string? exe, string? version) ResolveExeAndVersion(string toolId, string _rootPath, JsonToolResolver _toolResolver) {
        string jsonPath = ToolLockfile.GetPath(_rootPath);

        if (System.IO.File.Exists(jsonPath)) {
            try {
                JsonDocument doc = System.Text.Json.JsonDocument.Parse(System.IO.File.OpenRead(jsonPath));

                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object) {
                    foreach (System.Text.Json.JsonProperty prop in doc.RootElement.EnumerateObject()) {
                        if (!prop.Name.Equals(toolId, System.StringComparison.OrdinalIgnoreCase)) continue;

                        // Accept string or { exe, version }
                        if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String) {
                            string? exeStr = prop.Value.GetString();
                            return (ResolveRelative(jsonPath, exeStr), null);
                        }

                        if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Object) {
                            string? exe = null;
                            string? version = null;

                            // check if this is versioned structure: { "tool": { "1.0": { "exe": "..." } } }
                            // we just take the first version for now if not specified
                            foreach (var vProp in prop.Value.EnumerateObject()) {
                                if (vProp.Value.ValueKind == System.Text.Json.JsonValueKind.Object) {
                                    if (vProp.Value.TryGetProperty("exe", out System.Text.Json.JsonElement exeEl) && exeEl.ValueKind == System.Text.Json.JsonValueKind.String) {
                                        exe = ResolveRelative(jsonPath, exeEl.GetString());
                                        version = vProp.Name;
                                        return (exe, version);
                                    }
                                }
                            }

                            // fallback to { exe, version } format
                            if (prop.Value.TryGetProperty("exe", out System.Text.Json.JsonElement exeEl2) && exeEl2.ValueKind == System.Text.Json.JsonValueKind.String) {
                                exe = ResolveRelative(jsonPath, exeEl2.GetString());
                            }
                            if (prop.Value.TryGetProperty("version", out System.Text.Json.JsonElement verEl) && verEl.ValueKind == System.Text.Json.JsonValueKind.String) {
                                version = verEl.GetString();
                            }
                            return (exe, version);
                        }
                    }
                }
            } catch (System.Text.Json.JsonException ex) {
                Shared.IO.Diagnostics.Bug($"[ToolMetadataProvider] JSON parse error reading lockfile '{jsonPath}'.", ex);
                /* ignore parse errors and fallback */
            } catch (System.IO.IOException ex) {
                Shared.IO.Diagnostics.Bug($"[ToolMetadataProvider] IO error reading lockfile '{jsonPath}'.", ex);
                /* ignore parse errors and fallback */
            } catch (System.UnauthorizedAccessException ex) {
                Shared.IO.Diagnostics.Bug($"[ToolMetadataProvider] Access denied reading lockfile '{jsonPath}'.", ex);
                /* ignore parse errors and fallback */
            } catch (System.ArgumentException ex) {
                Shared.IO.Diagnostics.Bug($"[ToolMetadataProvider] Invalid lockfile path '{jsonPath}'.", ex);
                /* ignore parse errors and fallback */
            }
        }

        // Fallback to tool resolver path
        string path = _toolResolver.ResolveToolPath(toolId);
        return (path, null);
    }

    private static string ResolveRelative(string jsonPath, string? path) {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (System.IO.Path.IsPathRooted(path)) return path;
        string baseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(jsonPath)) ?? System.IO.Directory.GetCurrentDirectory();
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, path));
    }
}

