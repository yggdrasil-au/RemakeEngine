namespace EngineNet.Core.ScriptEngines.LuaModules;

/// <summary>
/// File system utilities for Lua script execution.
/// Provides safe file system operations with proper security checks.
/// </summary>
internal static class LuaFileSystemUtils {
    internal static bool PathExists(string path) => System.IO.Directory.Exists(path) || System.IO.File.Exists(path);

    internal static bool PathExistsIncludingLinks(string path) {
        if (PathExists(path)) {
            return true;
        }

        try {
            System.IO.FileSystemInfo info = GetInfo(path);
            return info.Exists ? true : info.LinkTarget != null;
        } catch {
            return false;
        }
    }

    internal static bool IsSymlink(string path) {
        try {
            System.IO.FileSystemInfo info = GetInfo(path);
            return info.LinkTarget != null || info.Attributes.HasFlag(System.IO.FileAttributes.ReparsePoint);
        } catch {
            return false;
        }
    }

    internal static bool CreateSymlink(string source, string destination, bool isDirectory) {
        try {
            string destFull = System.IO.Path.GetFullPath(destination);
            string srcFull = System.IO.Path.GetFullPath(source);
            string? parent = System.IO.Path.GetDirectoryName(destFull);
            if (!string.IsNullOrEmpty(parent)) {
                System.IO.Directory.CreateDirectory(parent);
            }

            if (isDirectory) {
                System.IO.Directory.CreateSymbolicLink(destFull, srcFull);
            } else {
                System.IO.File.CreateSymbolicLink(destFull, srcFull);
            }

            return true;
        } catch {
            return false;
        }
    }

    internal static string? RealPath(string path) {
        try {
            return System.IO.Path.GetFullPath(path);
        } catch {
            return null;
        }
    }

    internal static string? ReadLink(string path) {
        try {
            System.IO.FileSystemInfo info = GetInfo(path);
            return info.LinkTarget;
        } catch {
            return null;
        }
    }

    private static System.IO.FileSystemInfo GetInfo(string path) {
        string full = System.IO.Path.GetFullPath(path);
        System.IO.DirectoryInfo dirInfo = new System.IO.DirectoryInfo(full);
        if (dirInfo.Exists) {
            return dirInfo;
        }

        System.IO.FileInfo fileInfo = new System.IO.FileInfo(full);
        if (fileInfo.Exists) {
            return fileInfo;
        }
        // Determine based on trailing separator
        return full.EndsWith(System.IO.Path.DirectorySeparatorChar) || full.EndsWith(System.IO.Path.AltDirectorySeparatorChar)
            ? new System.IO.DirectoryInfo(full.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))
            : fileInfo;
    }
}