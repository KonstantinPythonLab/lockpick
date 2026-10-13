using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lockpick.Core.Processes;
using Microsoft.Win32.SafeHandles;

namespace Lockpick.Platform.Windows;

/// <summary>Finds game processes and opens them with read-only access (no write or code-execution rights).</summary>
[SupportedOSPlatform("windows")]
public sealed class Win32ProcessHost : IProcessHost
{
    public IReadOnlyList<ProcessCandidate> FindProcesses(IReadOnlyCollection<string> executableNames)
    {
        var result = new List<ProcessCandidate>();
        foreach (var exe in executableNames)
        {
            var name = Path.GetFileNameWithoutExtension(exe);
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    var path = TryGetImagePath(process.Id);
                    if (path is not null && !string.Equals(Path.GetFileName(path), exe, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    result.Add(new ProcessCandidate(process.Id, process.ProcessName, path, TryGetStartTime(process)));
                }
            }
        }

        return result;
    }

    public IAttachedProcess OpenReadOnly(ProcessCandidate candidate) => Win32AttachedProcess.Open(candidate.ProcessId);

    /// <summary>Full executable path via QueryFullProcessImageName (works across bitness and needs only limited rights).</summary>
    public static string? TryGetImagePath(int processId)
    {
        using var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        return handle.IsInvalid ? null : QueryImagePath(handle);
    }

    internal static unsafe string? QueryImagePath(SafeProcessHandle handle)
    {
        var buffer = stackalloc char[1024];
        uint size = 1024;
        return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
    }

    private static DateTimeOffset? TryGetStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}

/// <summary>A process handle opened with PROCESS_VM_READ | PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE only.</summary>
[SupportedOSPlatform("windows")]
public sealed class Win32AttachedProcess : IAttachedProcess
{
    private const int ErrorAccessDenied = 5;
    private const uint Access = NativeMethods.ProcessVmRead | NativeMethods.ProcessQueryLimitedInformation | NativeMethods.Synchronize;

    private readonly SafeProcessHandle _handle;

    private Win32AttachedProcess(int processId, SafeProcessHandle handle, ModuleImage mainModule)
    {
        ProcessId = processId;
        _handle = handle;
        MainModule = mainModule;
    }

    public int ProcessId { get; }

    public ModuleImage MainModule { get; }

    public bool HasExited => _handle.IsClosed || NativeMethods.WaitForSingleObject(_handle, 0) == NativeMethods.WaitObject0;

    public long FailedReads { get; private set; }

    public static Win32AttachedProcess Open(int processId)
    {
        var handle = NativeMethods.OpenProcess(Access, false, processId);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            if (error == ErrorAccessDenied && !Environment.IsPrivilegedProcess)
            {
                throw new ProcessAccessException(
                    $"Access denied reading PID {processId}. The game is probably running as administrator (for example, launched by the GOG installer). " +
                    "Restart the game normally, or run Lockpick as administrator.");
            }

            throw new ProcessAccessException($"OpenProcess({processId}) failed: {new Win32Exception(error).Message}");
        }

        try
        {
            return new Win32AttachedProcess(processId, handle, QueryMainModule(handle));
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public unsafe bool TryRead(ulong address, Span<byte> destination)
    {
        if (destination.IsEmpty)
        {
            return true;
        }

        if (_handle.IsClosed || address > (ulong)nint.MaxValue)
        {
            return false;
        }

        fixed (byte* buffer = destination)
        {
            var ok = NativeMethods.ReadProcessMemory(_handle, (nint)address, buffer, (nuint)destination.Length, out var read);
            if (ok && read == (nuint)destination.Length)
            {
                return true;
            }
        }

        FailedReads++;
        return false;
    }

    public void Dispose() => _handle.Dispose();

    private static unsafe ModuleImage QueryMainModule(SafeProcessHandle handle)
    {
        // The first module returned by EnumProcessModulesEx is the main executable. This can fail
        // briefly while a process is still initializing; the monitor simply retries on the next scan.
        nint first;
        if (!NativeMethods.EnumProcessModulesEx(handle, &first, (uint)sizeof(nint), out _, NativeMethods.ListModulesAll))
        {
            throw new ProcessAccessException($"EnumProcessModulesEx failed: {new Win32Exception(Marshal.GetLastPInvokeError()).Message}");
        }

        if (!NativeMethods.GetModuleInformation(handle, first, out var info, (uint)sizeof(NativeMethods.ModuleInfo)))
        {
            throw new ProcessAccessException($"GetModuleInformation failed: {new Win32Exception(Marshal.GetLastPInvokeError()).Message}");
        }

        return new ModuleImage((ulong)info.BaseOfDll, info.SizeOfImage, Win32ProcessHost.QueryImagePath(handle));
    }
}
