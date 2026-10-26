using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HexIDE.Processes;

/// <summary>
/// A Windows job object that ends every process assigned to it when the job closes: when this is disposed, and when
/// HexIDE ends however it ends, since the system then closes the last handle to the job. It is for a child that would
/// otherwise outlive HexIDE: the compiler #1689 starts, which in that mode watches no parent, and
/// the language servers HexIDE launches (#406).
/// <para>
/// Job objects exist only on Windows. Elsewhere <see cref="Assign"/> returns false and the caller keeps its own lifetime
/// handling. A process is assigned once it has started, because <see cref="Process"/> cannot start one suspended, so a
/// process it starts before then is not in the job.
/// </para>
/// </summary>
public sealed partial class ChildProcessJob : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    private readonly JobHandle? job;

    public ChildProcessJob()
    {
        if (!OperatingSystem.IsWindows()) return;
        job = CreateJobObjectW(IntPtr.Zero, IntPtr.Zero);
        if (job.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "A job object could not be created.");
        var limits = new ExtendedLimitInformation { BasicLimitInformation = new() { LimitFlags = JobObjectLimitKillOnJobClose } };
        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref limits, Marshal.SizeOf<ExtendedLimitInformation>()))
        {
            var error = Marshal.GetLastPInvokeError();
            job.Dispose();
            throw new System.ComponentModel.Win32Exception(error, "A job object could not be set to end its processes when it closes.");
        }
    }

    /// <summary>
    /// Puts a started process in the job, so that it ends when the job closes. True when it is now in the job; false where
    /// there are no job objects. Throws when Windows refuses, so a child that will outlive HexIDE is never silent.
    /// </summary>
    public bool Assign(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (job is null) return false;
        ObjectDisposedException.ThrowIf(job.IsClosed, this);
        if (!AssignProcessToJobObject(job, process.SafeHandle))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "The process could not be put in its job object.");
        return true;
    }

    /// <summary>Closes the job, which ends every process still in it.</summary>
    public void Dispose() => job?.Dispose();

    private sealed class JobHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial JobHandle CreateJobObjectW(IntPtr jobAttributes, IntPtr name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(JobHandle job, int infoClass, ref ExtendedLimitInformation info, int length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(JobHandle job, SafeProcessHandle process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
