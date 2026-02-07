using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace EngineNet.ScriptEngines.Global.SdkModule;

internal static class SymLink {
    private const string SE_CREATE_SYMBOLIC_LINK_NAME = "SeCreateSymbolicLinkPrivilege";

    /* :: :: Methods :: START :: */

    /// <summary>
    /// Creates a symbolic link.
    /// On Windows, it validates Developer Mode and SeCreateSymbolicLinkPrivilege.
    /// </summary>
    internal static bool Create(string source, string destination, bool isDirectory) {
        try {
            if (OperatingSystem.IsWindows()) {
                if (!CanCreateSymLinks()) {
                    Core.UI.EngineSdk.Error(GetFixInstructions());
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
            Core.UI.EngineSdk.Error($"create_symlink failed: {ex.Message}");
            Core.Diagnostics.luaInternalCatch($"create_symlink failed with exception: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Checks if Developer Mode is enabled AND if the current user has the SeCreateSymbolicLinkPrivilege.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static bool CanCreateSymLinks() {
        return IsDevModeEnabled() || HasSymbolicLinkPrivilege();
    }

    /// <summary>
    /// Returns clear, step-by-step instructions on how to fix the missing permissions.
    /// </summary>
    internal static string GetFixInstructions() {
        return
            "[Symlink] Requirement Not Met: Symbolic Link Creation Privilege is Missing.\n" +
            "To fix this, you must add your user to the Local Security Policy:\n" +
            "1. Press [Win + R], type 'secpol.msc', and hit Enter.\n" +
            "2. Navigate to: Local Policies -> User Rights Assignment.\n" +
            "3. Double-click on 'Create symbolic links'.\n" +
            "4. Click 'Add User or Group', type your username (or 'Users'), and click OK.\n" +
            "5. CRITICAL: You must SIGN OUT and SIGN BACK IN (or restart) for this to take effect.\n\n" +
            "Alternatively, run this application as Administrator or enable Developer Mode in Windows Settings.";
    }

    /* :: :: Methods :: END :: */
    // //
    /* :: :: Internal Checks :: START :: */

    [SupportedOSPlatform("windows")]
    private static bool IsDevModeEnabled() {
        const string keyName = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock";
        const string valueName = "AllowDevelopmentWithoutDevLicense";
        try {
            var val = Registry.GetValue(keyName, valueName, 0);
            return val != null && (int)val == 1;
        } catch {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool HasSymbolicLinkPrivilege() {
        IntPtr hToken = IntPtr.Zero;
        try {
            // Open the access token for the current process
            // TOKEN_QUERY = 0x0008
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out hToken)) {
                return false;
            }

            Luid luid = new Luid();
            // "SeCreateSymbolicLinkPrivilege"
            if (!LookupPrivilegeValue(null, SE_CREATE_SYMBOLIC_LINK_NAME, ref luid)) {
                return false;
            }

            uint tokenInfoLength = 0;
            GetTokenInformation(hToken, TokenInformationClass.TokenPrivileges, IntPtr.Zero, 0, out tokenInfoLength);

            IntPtr tokenInfo = Marshal.AllocHGlobal((int)tokenInfoLength);
            try {
                if (GetTokenInformation(hToken, TokenInformationClass.TokenPrivileges, tokenInfo, tokenInfoLength, out tokenInfoLength)) {
                    int privilegeCount = Marshal.ReadInt32(tokenInfo);
                    // Ptr arithmetic: offset by size of int (PrivilegeCount)
                    IntPtr currentPtr = new IntPtr(tokenInfo.ToInt64() + sizeof(int));

                    for (int i = 0; i < privilegeCount; i++) {
                        LuidAndAttributes laa = Marshal.PtrToStructure<LuidAndAttributes>(currentPtr);
                        if (laa.Luid.LowPart == luid.LowPart && laa.Luid.HighPart == luid.HighPart) {
                            // If the privilege is present in the token (even if disabled, it can often be enabled, 
                            // but usually for this check we just care if it's assigned).
                            // The SE_PRIVILEGE_ENABLED attribute might be relevant if we needed to *use* it manually,
                            // but .NET runtime handles enabling it if present.
                            return true;
                        }
                        currentPtr = new IntPtr(currentPtr.ToInt64() + Marshal.SizeOf(typeof(LuidAndAttributes)));
                    }
                }
            } finally {
                Marshal.FreeHGlobal(tokenInfo);
            }
        } finally {
            if (hToken != IntPtr.Zero) {
                CloseHandle(hToken);
            }
        }
        return false;
    }

    /* :: :: Internal Checks :: END :: */
    // //
    /* :: :: P/Invoke Boilerplate :: START :: */

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr TokenHandle, TokenInformationClass TokenInformationClass, IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, ref Luid lpLuid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint TOKEN_QUERY = 0x0008;

    private enum TokenInformationClass { TokenPrivileges = 3 }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid {
        public uint LowPart; public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes {
        public Luid Luid; public uint Attributes;
    }

    /* :: :: P/Invoke Boilerplate :: END :: */
}
