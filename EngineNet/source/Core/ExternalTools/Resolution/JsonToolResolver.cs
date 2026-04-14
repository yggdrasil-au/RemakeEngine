namespace EngineNet.Core.ExternalTools;

/// <summary>
/// Loads tool paths from the canonical typed lockfile and resolves version-aware executable paths.
/// Prioritizes <see cref="ToolLockfile.ToolLockfileName"/> for persistent installations.
/// </summary>
public class JsonToolResolver {
    private readonly Dictionary<string, Dictionary<string, string>> _tools = new Dictionary<string, Dictionary<string, string>>(System.StringComparer.OrdinalIgnoreCase);
    private readonly string _lockfilePath;
    private string? _loadedFile;
    private System.DateTime _lastWriteTime;

    public JsonToolResolver() {
        _lockfilePath = ToolLockfile.GetPath(EngineNet.Shared.State.RootPath);
        Load();
    }

    /// <summary>
    /// Loads or reloads the tool definitions from the local tracking file.
    /// </summary>
    private void Load() {
        string? found = System.IO.File.Exists(_lockfilePath) ? _lockfilePath : null;

        if (found == null) {
            if (_loadedFile == null) {
                return;
            }

            _tools.Clear();
            _loadedFile = null;
            return;
        }

        bool isNewFile = !string.Equals(found, _loadedFile, System.StringComparison.OrdinalIgnoreCase);
        System.DateTime writeTime = System.IO.File.GetLastWriteTimeUtc(found);

        if (!isNewFile && writeTime <= _lastWriteTime) {
            return;
        }

        _tools.Clear();
        _loadedFile = found;
        _lastWriteTime = writeTime;

        string baseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(found)) ?? System.IO.Directory.GetCurrentDirectory();
        Dictionary<string, Dictionary<string, ToolLockfileEntry>> lockData = ToolLockfileManager.Load(found);

        foreach (KeyValuePair<string, Dictionary<string, ToolLockfileEntry>> toolProp in lockData) {
            var versions = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, ToolLockfileEntry> versionEntry in toolProp.Value) {
                if (string.IsNullOrWhiteSpace(versionEntry.Value.Exe)) {
                    continue;
                }

                versions[versionEntry.Key] = ResolvePath(baseDir, versionEntry.Value.Exe);
            }

            if (versions.Count > 0) {
                _tools[toolProp.Key] = versions;
            }
        }
    }

    private static string ResolvePath(string baseDir, string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return string.Empty;
        }

        if (!System.IO.Path.IsPathRooted(path)) {
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, path));
        }

        return path;
    }

    public virtual string ResolveToolPath(string toolId, string? version = null) {
        Load();

        if (!_tools.TryGetValue(toolId, out var versions)) {
            return toolId;
        }

        if (version != null && versions.TryGetValue(version, out string? resolvedPath)) {
            return resolvedPath;
        }

        string? lastPath = null;
        foreach (string candidatePath in versions.Values) {
            lastPath = candidatePath;
        }

        if (!string.IsNullOrWhiteSpace(lastPath)) {
            return lastPath;
        }

        return toolId;
    }
}
