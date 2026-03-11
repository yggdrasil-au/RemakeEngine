using System;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace EngineNet.Core.Utils;

/// <summary>
/// Native Windows utilities for robust process management.
/// </summary>
public static class NativeMethods {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);

    public enum JobObjectInfoType {
        AssociateCompletionPortInformation = 7,
        BasicLimitInformation = 2,
        BasicUIRestrictions = 4,
        EndOfJobTimeInformation = 6,
        ExtendedLimitInformation = 9,
        SecurityLimitInformation = 5,
        GroupInformation = 11
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_BASIC_LIMIT_INFORMATION {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public long Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IO_COUNTERS {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoCounters;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryLimit;
        public UIntPtr PeakJobMemoryLimit;
    }

    public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
}

/// <summary>
/// A managed wrapper for a Windows Job Object to ensure child process termination.
/// </summary>
public sealed class JobObject : IDisposable {
    private IntPtr _handle;
    private bool _disposed;

    public JobObject(string? name = null) {
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
                Diagnostics.Log($"[JobObject] Failed to set JobObject information: {Marshal.GetLastWin32Error()}");
            }
        } finally {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public bool AddProcess(Process process) {
        if (!OperatingSystem.IsWindows() || _handle == IntPtr.Zero || process.HasExited) return false;
        return NativeMethods.AssignProcessToJobObject(_handle, process.Handle);
    }

    public void Terminate(uint exitCode = 1) {
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
