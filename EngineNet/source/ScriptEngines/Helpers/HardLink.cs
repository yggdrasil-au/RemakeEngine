using System;
using System.IO;

namespace EngineNet.ScriptEngines.Helpers;

/// <summary>
/// Cross-platform hardlink creation helper for .NET 6+.
/// Uses CreateHardLinkW on Windows and link(2) on Unix-like systems.
/// </summary>
internal static class HardLink {
    internal static void Create(string existingFile, string newLinkPath) {
        if (string.IsNullOrWhiteSpace(existingFile)) throw new ArgumentNullException(nameof(existingFile));
        if (string.IsNullOrWhiteSpace(newLinkPath)) throw new ArgumentNullException(nameof(newLinkPath));

        string src = System.IO.Path.GetFullPath(existingFile);
        string dst = System.IO.Path.GetFullPath(newLinkPath);

        if (!System.IO.File.Exists(src)) throw new System.IO.FileNotFoundException("Existing file not found.", src);

        if (OperatingSystem.IsWindows()) {
            if (!CreateHardLinkW(dst, src, IntPtr.Zero)) {
                int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                throw new IOException($"CreateHardLink failed with error {err}.");
            }
        } else {
            // Unix (Linux/macOS): libc link() returns 0 on success
            int rc = link(src, dst);
            if (rc != 0) {
                int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error(); // maps to errno
                throw new IOException($"link(2) failed with errno {err}.");
            }
        }
    }

    // Windows
    [System.Runtime.InteropServices.DllImport("Kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "CreateHardLinkW")]
    private static extern bool CreateHardLinkW(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);

    // Unix (Linux/macOS): libc's link()
    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true, EntryPoint = "link")]
    private static extern int link(string oldpath, string newpath);
}