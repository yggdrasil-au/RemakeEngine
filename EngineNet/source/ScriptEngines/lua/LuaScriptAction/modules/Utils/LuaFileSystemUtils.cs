

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

namespace EngineNet.ScriptEngines.lua.LuaModules.Utils;

/// <summary>
/// File system utilities for Lua script execution.
/// Provides safe file system operations with proper security checks.
/// </summary>
internal static class LuaFileSystemUtils {
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

    internal static bool CreateSymlink(string source, string destination, bool isDirectory) {
        try {
            if (OperatingSystem.IsWindows()) {
                if (!ValidateWindowsSymlinkSupport(out string errorMessage)) {
                    Core.UI.EngineSdk.Error($"[Symlink] Requirement Not Met: {errorMessage}");
                    return false;
                }
            }

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
        } catch (Exception ex) {
            Core.UI.EngineSdk.Error("create_symlink failed: " + ex.Message);
            Core.Diagnostics.luaInternalCatch("create_symlink failed with exception: " + ex);
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool ValidateWindowsSymlinkSupport(out string message) {
        message = string.Empty;

        // 1. Check if running as Administrator
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) {
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            if (principal.IsInRole(WindowsBuiltInRole.Administrator)) return true;
        }

        // 2. Check for Developer Mode if not Admin
        try {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock")) {
                if (key?.GetValue("AllowDevelopmentSettings") is int value && value > 0) {
                    return true;
                }
            }
        } catch { /* Ignore registry read errors */ }

        message = "Symbolic links require 'Developer Mode' to be enabled in Windows Settings or the process to be 'Run as Administrator'. " +
                  "Please enable Developer Mode in 'Settings > System > For developers' (or 'Update & Security > For developers' on older Windows).";
        return false;
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