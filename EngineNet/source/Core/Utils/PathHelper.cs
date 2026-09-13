namespace EngineNet.Core.Utils;

/// <summary>
/// Helper for resolving relative paths and normalizing them.
/// </summary>
internal static class PathHelper {
    /// <summary>
    /// Resolves a path relative to a root directory if it's not already rooted.
    /// Also handles placeholder resolution if passed, but primarily ensures a valid absolute path.
    /// </summary>
    /// <param name="root">The base directory to resolve against.</param>
    /// <param name="path">The path to resolve.</param>
    /// <returns>The absolute normalized path, or the original path if null/empty.</returns>
    internal static string ResolveRelativePath(string root, string? path) {
        if (string.IsNullOrWhiteSpace(path)) return path ?? string.Empty;

        // Ensure path uses consistent separators for the OS
        string normalizedPath = path.Replace(oldChar: '/', newChar: Path.DirectorySeparatorChar).Replace(oldChar: '\\', newChar: Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(path: normalizedPath)) {
            return Path.GetFullPath(path: normalizedPath);
        }

        return Path.GetFullPath(path: Path.Combine(path1: root, path2: normalizedPath));
    }
}
