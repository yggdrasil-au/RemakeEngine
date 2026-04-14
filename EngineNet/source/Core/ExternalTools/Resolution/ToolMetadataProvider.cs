namespace EngineNet.Core.ExternalTools;

/// <summary>
/// Provides metadata for tools (executable path and optional version) by consulting the canonical typed lockfile,
/// and falling back to <see cref="JsonToolResolver"/> when the tool is not registered there.
/// </summary>
public static class ToolMetadataProvider {

    public static (string? exe, string? version) ResolveExeAndVersion(string toolId, string _rootPath, JsonToolResolver _toolResolver) {
        string jsonPath = ToolLockfile.GetPath(_rootPath);

        if (System.IO.File.Exists(jsonPath)) {
            Dictionary<string, Dictionary<string, ToolLockfileEntry>> lockData = ToolLockfileManager.Load(jsonPath);
            if (lockData.TryGetValue(toolId, out Dictionary<string, ToolLockfileEntry>? versions)) {
                foreach (KeyValuePair<string, ToolLockfileEntry> versionEntry in versions) {
                    if (string.IsNullOrWhiteSpace(versionEntry.Value.Exe)) {
                        continue;
                    }

                    string exe = ResolveRelative(jsonPath, versionEntry.Value.Exe);
                    return (exe, versionEntry.Key);
                }
            }
        }

        string path = _toolResolver.ResolveToolPath(toolId);
        return (path, null);
    }

    private static string ResolveRelative(string jsonPath, string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return string.Empty;
        }

        if (System.IO.Path.IsPathRooted(path)) {
            return path;
        }

        string baseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(jsonPath)) ?? System.IO.Directory.GetCurrentDirectory();
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, path));
    }
}
