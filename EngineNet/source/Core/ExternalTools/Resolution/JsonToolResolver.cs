namespace EngineNet.Core.ExternalTools;

/// <summary>
/// Loads tool paths from the canonical typed lockfile and resolves version-aware executable paths.
/// Prioritizes <see cref="ToolLockfile.ToolLockfileName"/> for persistent installations.
/// </summary>
public class JsonToolResolver {
    private readonly Dictionary<string, Dictionary<string, string>> _tools = new Dictionary<string, Dictionary<string, string>>(comparer: System.StringComparer.OrdinalIgnoreCase);
    private readonly string _lockfilePath;
    private string? _loadedFile;
    private System.DateTime _lastWriteTime;

    public JsonToolResolver() {
        _lockfilePath = ToolLockfile.GetPath(rootPath: EngineNet.Shared.State.RootPath);
        Load();
    }

    /// <summary>
    /// Loads or reloads the tool definitions from the local tracking file.
    /// </summary>
    private void Load() {
        string? found = System.IO.File.Exists(path: _lockfilePath) ? _lockfilePath : null;

        if (found == null) {
            if (_loadedFile == null) {
                return;
            }

            _tools.Clear();
            _loadedFile = null;
            return;
        }

        bool isNewFile = !string.Equals(a: found, b: _loadedFile, comparisonType: System.StringComparison.OrdinalIgnoreCase);
        System.DateTime writeTime = System.IO.File.GetLastWriteTimeUtc(path: found);

        if (!isNewFile && writeTime <= _lastWriteTime) {
            return;
        }

        _tools.Clear();
        _loadedFile = found;
        _lastWriteTime = writeTime;

        string baseDir = System.IO.Path.GetDirectoryName(path: System.IO.Path.GetFullPath(path: found)) ?? System.IO.Directory.GetCurrentDirectory();
        Dictionary<string, Dictionary<string, ToolLockfileEntry>> lockData = ToolLockfileManager.Load(lockPath: found);

        foreach (KeyValuePair<string, Dictionary<string, ToolLockfileEntry>> toolProp in lockData) {
            Dictionary<string, string> versions = new Dictionary<string, string>(comparer: System.StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, ToolLockfileEntry> versionEntry in toolProp.Value) {
                if (string.IsNullOrWhiteSpace(versionEntry.Value.Exe)) {
                    continue;
                }

                versions[key: versionEntry.Key] = ResolvePath(baseDir: baseDir, path: versionEntry.Value.Exe);
            }

            if (versions.Count > 0) {
                _tools[key: toolProp.Key] = versions;
            }
        }
    }

    private static string ResolvePath(string baseDir, string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return string.Empty;
        }

        if (!System.IO.Path.IsPathRooted(path: path)) {
            return System.IO.Path.GetFullPath(path: System.IO.Path.Combine(path1: baseDir, path2: path));
        }

        return path;
    }

    public virtual string ResolveToolPath(string toolId, string? version = null) {
        Load();

        if (!_tools.TryGetValue(key: toolId, out Dictionary<string, string>? versions)) {
            return toolId;
        }

        if (version != null && versions.TryGetValue(key: version, out string? resolvedPath)) {
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
