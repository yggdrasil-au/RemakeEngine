using System.IO;

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
        string normalizedPath = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        
        if (Path.IsPathRooted(normalizedPath)) {
            return Path.GetFullPath(normalizedPath);
        }
        
        return Path.GetFullPath(Path.Combine(root, normalizedPath));
    }
}
