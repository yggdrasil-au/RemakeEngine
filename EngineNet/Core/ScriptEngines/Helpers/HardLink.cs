using System;
using System.IO;
using System.Runtime.InteropServices;

namespace EngineNet.Core.ScriptEngines.Helpers;

/// <summary>
/// Cross-platform hardlink creation helper for .NET 6+.
/// Uses CreateHardLinkW on Windows and link(2) on Unix-like systems.
/// </summary>
internal static class HardLink {
    public static void Create(String existingFile, String newLinkPath) {
        if (String.IsNullOrWhiteSpace(existingFile)) throw new ArgumentNullException(nameof(existingFile));
        if (String.IsNullOrWhiteSpace(newLinkPath)) throw new ArgumentNullException(nameof(newLinkPath));

        String src = Path.GetFullPath(existingFile);
        String dst = Path.GetFullPath(newLinkPath);

        if (!File.Exists(src)) throw new FileNotFoundException("Existing file not found.", src);

        if (OperatingSystem.IsWindows()) {
            if (!CreateHardLinkW(dst, src, IntPtr.Zero)) {
                Int32 err = Marshal.GetLastWin32Error();
                throw new IOException($"CreateHardLink failed with error {err}.");
            }
        } else {
            // Unix (Linux/macOS): libc link() returns 0 on success
            Int32 rc = link(src, dst);
            if (rc != 0) {
                Int32 err = Marshal.GetLastWin32Error(); // maps to errno
                throw new IOException($"link(2) failed with errno {err}.");
            }
        }
    }

    // Windows
    [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateHardLinkW")]
    private static extern bool CreateHardLinkW(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);

    // Unix (Linux/macOS): libc's link()
    [DllImport("libc", SetLastError = true, EntryPoint = "link")]
    private static extern int link(string oldpath, string newpath);
}