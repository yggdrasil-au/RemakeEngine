namespace EngineNet.Core.ExternalTools;

/// <summary>
/// Provides metadata for tools (executable path and optional version) by consulting the canonical typed lockfile,
/// and falling back to <see cref="Abstractions.IJsonToolResolver"/> when the tool is not registered there.
/// </summary>
public static class ToolMetadataProvider {

    public static (string? exe, string? version) ResolveExeAndVersion(string toolId, string _rootPath, Abstractions.IJsonToolResolver _toolResolver) {
        string jsonPath = ToolLockfile.GetPath(rootPath: _rootPath);

        if (System.IO.File.Exists(path: jsonPath)) {
            Dictionary<string, Dictionary<string, ToolLockfileEntry>> lockData = ToolLockfileManager.Load(lockPath: jsonPath);
            if (lockData.TryGetValue(key: toolId, out Dictionary<string, ToolLockfileEntry>? versions)) {
                foreach (KeyValuePair<string, ToolLockfileEntry> versionEntry in versions) {
                    if (string.IsNullOrWhiteSpace(versionEntry.Value.Exe)) {
                        continue;
                    }

                    string exe = ResolveRelative(jsonPath: jsonPath, path: versionEntry.Value.Exe);
                    return (exe, versionEntry.Key);
                }
            }
        }

        string path = _toolResolver.ResolveToolPath(toolId: toolId);
        return (path, null);
    }

    private static string ResolveRelative(string jsonPath, string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return string.Empty;
        }

        if (System.IO.Path.IsPathRooted(path: path)) {
            return path;
        }

        string baseDir = System.IO.Path.GetDirectoryName(path: System.IO.Path.GetFullPath(path: jsonPath)) ?? System.IO.Directory.GetCurrentDirectory();
        return System.IO.Path.GetFullPath(path: System.IO.Path.Combine(path1: baseDir, path2: path));
    }
}
