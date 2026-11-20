// @module: reEngine.Launcher.LauncherJobObjectGuard
// @purpose: Pure BCL Win32 Job Object kill-on-close guard attached strictly when the launcher waits synchronously for its child process.
// @depends-on: System, System.ComponentModel, System.Diagnostics, System.Runtime.InteropServices
// @exports: LauncherJobObjectGuard

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace reEngine.Launcher;

internal sealed class LauncherJobObjectGuard : IDisposable
{
    private const int JobObjectExtendedLimitInformationClass = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000u;
    private IntPtr _jobHandle;
    private bool _disposed;

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInfoClass, ref JobObjectExtendedLimitInformation lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public bool TryAttachProcess(Process process)
    {
        if (_disposed || process is null || !OperatingSystem.IsWindows()) return false;
        try
        {
            if (process.HasExited) return false;
            if (_jobHandle == IntPtr.Zero)
            {
                IntPtr handle = CreateJobObjectW(IntPtr.Zero, null);
                if (handle == IntPtr.Zero) return false;
                var info = new JobObjectExtendedLimitInformation { BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose } };
                if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformationClass, ref info, (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
                {
                    _ = CloseHandle(handle);
                    return false;
                }
                _jobHandle = handle;
            }
            return AssignProcessToJobObject(_jobHandle, process.Handle);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_jobHandle != IntPtr.Zero) { _ = CloseHandle(_jobHandle); _jobHandle = IntPtr.Zero; }
    }
}
