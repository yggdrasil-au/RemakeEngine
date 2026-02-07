

namespace EngineNet.ScriptEngines.Global.SdkModule;

/// <summary>
/// File system utilities for script execution.
/// Provides safe file system operations with proper security checks.
/// </summary>
internal static class FileSystemUtils {
    internal static bool PathExists(string path) => System.IO.Path.Exists(path);

    internal static bool PathExistsIncludingLinks(string path) {
        if (PathExists(path)) {
            return true;
        }

        try {
            System.IO.FileSystemInfo info = GetInfo(path);
            return info.Exists ? true : info.LinkTarget != null;
        } catch (Exception ex) {
            Core.Diagnostics.luaInternalCatch("path_exists_including_links failed for path: " + path + " with exception: " + ex);
            return false;
        }
    }

    internal static bool IsSymlink(string path) {
        try {
            System.IO.FileSystemInfo info = GetInfo(path);
            return info.LinkTarget != null || info.Attributes.HasFlag(System.IO.FileAttributes.ReparsePoint);
        } catch (Exception ex) {
            Core.Diagnostics.luaInternalCatch("is_symlink failed for path: " + path + " with exception: " + ex);
            return false;
        }
    }

    internal static string? RealPath(string path) {
        try {
            return System.IO.Path.GetFullPath(path);
        } catch (Exception ex) {
            Core.Diagnostics.luaInternalCatch("real_path failed for path: " + path + " with exception: " + ex);
            return null;
        }
    }

    internal static string? ReadLink(string path) {
        try {
            System.IO.FileSystemInfo info = GetInfo(path);
            return info.LinkTarget;
        } catch (Exception ex) {
            Core.Diagnostics.luaInternalCatch("read_link failed for path: " + path + " with exception: " + ex);
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