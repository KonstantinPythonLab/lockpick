using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lockpick.Platform.Windows;

/// <summary>
/// Freezes and unfreezes a whole process (<c>NtSuspendProcess</c> / <c>NtResumeProcess</c>), used only by the
/// ask-about-unknown-events capture mode so the game doesn't run on while the player answers. The handle has only
/// <c>PROCESS_SUSPEND_RESUME</c> access: suspending never reads or writes the game's memory.
/// </summary>
public static partial class ProcessSuspender
{
    private const uint ProcessSuspendResume = 0x0800;

    /// <summary>Suspends every thread of the process. Returns null on success, else the reason.</summary>
    public static string? Suspend(int processId) => Call(processId, NtSuspendProcess);

    /// <summary>Resumes a process suspended by <see cref="Suspend"/>. Returns null on success, else the reason.</summary>
    public static string? Resume(int processId) => Call(processId, NtResumeProcess);

    private static string? Call(int processId, Func<SafeProcessHandle, int> operation)
    {
        using var handle = NativeMethods.OpenProcess(ProcessSuspendResume, false, processId);
        if (handle.IsInvalid)
        {
            return $"can't open the game process (error {Marshal.GetLastPInvokeError()})";
        }

        var status = operation(handle);
        return status == 0 ? null : $"NTSTATUS 0x{status:X8}";
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtSuspendProcess(SafeProcessHandle process);

    [LibraryImport("ntdll.dll")]
    private static partial int NtResumeProcess(SafeProcessHandle process);
}
