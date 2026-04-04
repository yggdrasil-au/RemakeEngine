using System.Runtime.InteropServices;
using System.Diagnostics;

namespace EngineNet.Core.Utils;

/// <summary>
/// Native Windows utilities for robust process management.
/// </summary>
internal static class NativeMethods {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);

    internal enum JobObjectInfoType {
        AssociateCompletionPortInformation = 7,
        BasicLimitInformation = 2,
        BasicUIRestrictions = 4,
        EndOfJobTimeInformation = 6,
        ExtendedLimitInformation = 9,
        SecurityLimitInformation = 5,
        GroupInformation = 11
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION {
        internal long PerProcessUserTimeLimit;
        internal long PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal UIntPtr MinimumWorkingSetSize;
        internal UIntPtr MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal long Affinity;
        internal uint PriorityClass;
        internal uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IO_COUNTERS {
        internal ulong ReadOperationCount;
        internal ulong WriteOperationCount;
        internal ulong OtherOperationCount;
        internal ulong ReadTransferCount;
        internal ulong WriteTransferCount;
        internal ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
        internal JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        internal IO_COUNTERS IoCounters;
        internal UIntPtr ProcessMemoryLimit;
        internal UIntPtr JobMemoryLimit;
        internal UIntPtr PeakProcessMemoryLimit;
        internal UIntPtr PeakJobMemoryLimit;
    }

    internal const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
}

/// <summary>
/// A managed wrapper for a Windows Job Object to ensure child process termination.
/// </summary>
internal sealed class JobObject : IDisposable {
    private IntPtr _handle;
    private bool _disposed;

    internal JobObject(string? name = null) {
        if (!OperatingSystem.IsWindows()) return;

        _handle = NativeMethods.CreateJobObject(IntPtr.Zero, name);
        if (_handle == IntPtr.Zero) return;

        var info = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
            BasicLimitInformation = new NativeMethods.JOBOBJECT_BASIC_LIMIT_INFORMATION {
                LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        int length = Marshal.SizeOf(typeof(NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
        IntPtr ptr = Marshal.AllocHGlobal(length);
        try {
            Marshal.StructureToPtr(info, ptr, false);
            if (!NativeMethods.SetInformationJobObject(_handle, NativeMethods.JobObjectInfoType.ExtendedLimitInformation, ptr, (uint)length)) {
                Shared.Diagnostics.Log($"[JobObject] Failed to set JobObject information: {Marshal.GetLastWin32Error()}");
            }
        } finally {
            Marshal.FreeHGlobal(ptr);
        }
    }

    internal bool AddProcess(Process process) {
        if (!OperatingSystem.IsWindows() || _handle == IntPtr.Zero || process.HasExited) return false;
        return NativeMethods.AssignProcessToJobObject(_handle, process.Handle);
    }

    internal void Terminate(uint exitCode = 1) {
        if (!OperatingSystem.IsWindows() || _handle == IntPtr.Zero) return;
        NativeMethods.TerminateJobObject(_handle, exitCode);
    }

    public void Dispose() {
        if (_disposed) return;
        if (_handle != IntPtr.Zero) {
            NativeMethods.CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
        _disposed = true;
    }
}
