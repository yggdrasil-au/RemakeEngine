using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace EngineNet.ScriptEngines.Global.SdkModule;

internal static class SymLink {
    private const string SE_CREATE_SYMBOLIC_LINK_NAME = "SeCreateSymbolicLinkPrivilege";

    /* :: :: Methods :: START :: */

    /// <summary>
    /// Creates a symbolic link and optionally overwrites an existing destination.
    /// On Windows, it validates Developer Mode and SeCreateSymbolicLinkPrivilege.
    /// </summary>
    internal static bool Create(string source, string destination, bool isDirectory, bool overwrite) {
        try {
            if (OperatingSystem.IsWindows() && !CanCreateSymLinks()) {
                Shared.IO.UI.EngineSdk.Error(GetFixInstructions());
                return false;
            }

            string destFull = System.IO.Path.GetFullPath(path: destination);
            string srcFull = System.IO.Path.GetFullPath(path: source);
            string? parent = System.IO.Path.GetDirectoryName(path: destFull);

            if (!string.IsNullOrEmpty(parent)) {
                System.IO.Directory.CreateDirectory(path: parent);
            }

            if (overwrite) {
                RemoveExistingDestination(destinationFullPath: destFull);
            }

            if (isDirectory) {
                System.IO.Directory.CreateSymbolicLink(path: destFull, pathToTarget: srcFull);
            } else {
                System.IO.File.CreateSymbolicLink(path: destFull, pathToTarget: srcFull);
            }

            return true;
        } catch (Exception ex) {
            Shared.IO.UI.EngineSdk.Error($"create_symlink failed: {ex.Message}");
            Shared.IO.Diagnostics.LuaInternalCatch(ex: $"create_symlink failed with exception: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Removes an existing file, directory, or symlink at the target path.
    /// </summary>
    private static void RemoveExistingDestination(string destinationFullPath) {
        if (FileSystemUtils.IsSymlink(path: destinationFullPath) || System.IO.File.Exists(path: destinationFullPath)) {
            System.IO.File.Delete(path: destinationFullPath);
            return;
        }

        if (System.IO.Directory.Exists(path: destinationFullPath)) {
            System.IO.Directory.Delete(path: destinationFullPath, recursive: true);
        }
    }

    /// <summary>
    /// Checks if Developer Mode is enabled AND if the current user has the SeCreateSymbolicLinkPrivilege.
    /// </summary>
    [SupportedOSPlatform(platformName: "windows")]
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

    [SupportedOSPlatform(platformName: "windows")]
    private static bool IsDevModeEnabled() {
        const string keyName = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock";
        const string valueName = "AllowDevelopmentWithoutDevLicense";
        try {
            object? val = Registry.GetValue(keyName: keyName, valueName: valueName, defaultValue: 0);
            return val != null && (int)val == 1;
        } catch {
            return false;
        }
    }

    [SupportedOSPlatform(platformName: "windows")]
    private static bool HasSymbolicLinkPrivilege() {
        IntPtr hToken = IntPtr.Zero;
        try {
            // Open the access token for the current process
            // TOKEN_QUERY = 0x0008
            if (!OpenProcessToken(ProcessHandle: GetCurrentProcess(), DesiredAccess: TOKEN_QUERY, TokenHandle: out hToken)) {
                return false;
            }

            Luid luid = new();
            // "SeCreateSymbolicLinkPrivilege"
            if (!LookupPrivilegeValue(lpSystemName: null, lpName: SE_CREATE_SYMBOLIC_LINK_NAME, lpLuid: ref luid)) {
                return false;
            }

            uint tokenInfoLength;
            GetTokenInformation(TokenHandle: hToken, TokenInformationClass: TokenInformationClass.TokenPrivileges, TokenInformation: IntPtr.Zero, TokenInformationLength: 0, ReturnLength: out tokenInfoLength);

            IntPtr tokenInfo = Marshal.AllocHGlobal(cb: (int)tokenInfoLength);
            try {
                if (GetTokenInformation(TokenHandle: hToken, TokenInformationClass: TokenInformationClass.TokenPrivileges, TokenInformation: tokenInfo, TokenInformationLength: tokenInfoLength, ReturnLength: out tokenInfoLength)) {
                    int privilegeCount = Marshal.ReadInt32(ptr: tokenInfo);
                    // Ptr arithmetic: offset by size of int (PrivilegeCount)
                    IntPtr currentPtr = new(tokenInfo.ToInt64() + sizeof(int));

                    for (int i = 0; i < privilegeCount; i++) {
                        LuidAndAttributes laa = Marshal.PtrToStructure<LuidAndAttributes>(ptr: currentPtr);
                        if (laa.Luid.LowPart == luid.LowPart && laa.Luid.HighPart == luid.HighPart) {
                            // If the privilege is present in the token (even if disabled, it can often be enabled, 
                            // but usually for this check we just care if it's assigned).
                            // The SE_PRIVILEGE_ENABLED attribute might be relevant if we needed to *use* it manually,
                            // but .NET runtime handles enabling it if present.
                            return true;
                        }
                        currentPtr = new IntPtr(currentPtr.ToInt64() + Marshal.SizeOf(t: typeof(LuidAndAttributes)));
                    }
                }
            } finally {
                Marshal.FreeHGlobal(hglobal: tokenInfo);
            }
        } finally {
            if (hToken != IntPtr.Zero) {
                CloseHandle(hObject: hToken);
            }
        }
        return false;
    }

    /* :: :: Internal Checks :: END :: */
    // //
    /* :: :: P/Invoke Boilerplate :: START :: */

    [DllImport(dllName: "advapi32.dll", SetLastError = true)]
    [return: MarshalAs(unmanagedType: UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport(dllName: "kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport(dllName: "advapi32.dll", SetLastError = true)]
    [return: MarshalAs(unmanagedType: UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr TokenHandle, TokenInformationClass TokenInformationClass, IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

    [DllImport(dllName: "advapi32.dll", SetLastError = true)]
    [return: MarshalAs(unmanagedType: UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, ref Luid lpLuid);

    [DllImport(dllName: "kernel32.dll", SetLastError = true)]
    [return: MarshalAs(unmanagedType: UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint TOKEN_QUERY = 0x0008;

    private enum TokenInformationClass { TokenPrivileges = 3 }

    [StructLayout(layoutKind: LayoutKind.Sequential)]
    private struct Luid {
        internal uint LowPart; internal int HighPart;
    }

    [StructLayout(layoutKind: LayoutKind.Sequential)]
    private struct LuidAndAttributes {
        internal Luid Luid; internal uint Attributes;
    }

    /* :: :: P/Invoke Boilerplate :: END :: */
}
