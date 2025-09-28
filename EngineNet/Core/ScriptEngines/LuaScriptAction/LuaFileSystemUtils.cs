using System;
using System.IO;

namespace EngineNet.Core.ScriptEngines.LuaModules;

/// <summary>
/// File system utilities for Lua script execution.
/// Provides safe file system operations with proper security checks.
/// </summary>
internal static class LuaFileSystemUtils {
    public static Boolean PathExists(String path) => Directory.Exists(path) || File.Exists(path);

    public static Boolean PathExistsIncludingLinks(String path) {
        if (PathExists(path)) {
            return true;
        }

        try {
            FileSystemInfo info = GetInfo(path);
            return info.Exists ? true : info.LinkTarget != null;
        } catch {
            return false;
        }
    }

    public static Boolean IsSymlink(String path) {
        try {
            FileSystemInfo info = GetInfo(path);
            return info.LinkTarget != null || info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        } catch {
            return false;
        }
    }

    public static Boolean CreateSymlink(String source, String destination, Boolean isDirectory) {
        try {
            String destFull = Path.GetFullPath(destination);
            String srcFull = Path.GetFullPath(source);
            String? parent = Path.GetDirectoryName(destFull);
            if (!String.IsNullOrEmpty(parent)) {
                Directory.CreateDirectory(parent);
            }

            if (isDirectory) {
                Directory.CreateSymbolicLink(destFull, srcFull);
            } else {
                File.CreateSymbolicLink(destFull, srcFull);
            }

            return true;
        } catch {
            return false;
        }
    }

    public static String? RealPath(String path) {
        try {
            return Path.GetFullPath(path);
        } catch {
            return null;
        }
    }

    public static String? ReadLink(String path) {
        try {
            FileSystemInfo info = GetInfo(path);
            return info.LinkTarget;
        } catch {
            return null;
        }
    }

    private static FileSystemInfo GetInfo(String path) {
        String full = Path.GetFullPath(path);
        DirectoryInfo dirInfo = new DirectoryInfo(full);
        if (dirInfo.Exists) {
            return dirInfo;
        }

        FileInfo fileInfo = new FileInfo(full);
        if (fileInfo.Exists) {
            return fileInfo;
        }
        // Determine based on trailing separator
        return full.EndsWith(Path.DirectorySeparatorChar) || full.EndsWith(Path.AltDirectorySeparatorChar)
            ? new DirectoryInfo(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : fileInfo;
    }
}