using Lockpick.Core.Memory;

namespace Lockpick.Core.Processes;

/// <summary>A running process that might be a supported game.</summary>
public sealed record ProcessCandidate(int ProcessId, string ProcessName, string? ExecutablePath, DateTimeOffset? StartTime);

/// <summary>Where and how large the main executable image is mapped in the target process.</summary>
public sealed record ModuleImage(ulong BaseAddress, uint Size, string? Path);

/// <summary>An open, read-only handle to a target process.</summary>
public interface IAttachedProcess : IProcessMemory, IDisposable
{
    int ProcessId { get; }

    /// <summary>True once the process has terminated. Must be cheap to call every poll.</summary>
    bool HasExited { get; }

    ModuleImage MainModule { get; }
}

/// <summary>Platform services for discovering and opening game processes.</summary>
public interface IProcessHost
{
    /// <summary>Finds running processes whose executable file name matches one of <paramref name="executableNames"/>.</summary>
    IReadOnlyList<ProcessCandidate> FindProcesses(IReadOnlyCollection<string> executableNames);

    /// <summary>Opens the process with read-only access. Throws <see cref="ProcessAccessException"/> on failure.</summary>
    IAttachedProcess OpenReadOnly(ProcessCandidate candidate);
}

public sealed class ProcessAccessException(string message, Exception? inner = null) : Exception(message, inner);
