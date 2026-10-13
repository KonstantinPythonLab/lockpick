using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Lockpick.Core.Logging;

/// <summary>A captured log entry, also kept in memory for the diagnostics view.</summary>
public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message, string? Exception);

/// <summary>
/// Minimal structured logger: one JSON object per line, written by a background flusher so log calls
/// never block the monitor thread. Recent entries are also kept in a bounded in-memory ring.
/// </summary>
public sealed class JsonLinesLoggerProvider : ILoggerProvider
{
    private readonly BlockingCollection<LogEntry> _queue = new(boundedCapacity: 4096);
    private readonly ConcurrentQueue<LogEntry> _recent = new();
    private readonly int _recentCapacity;
    private readonly string? _path;
    private readonly long _maxFileBytes;
    private readonly Thread _writerThread;
    private readonly LogLevel _minimumLevel;

    public JsonLinesLoggerProvider(string? path, LogLevel minimumLevel = LogLevel.Information, int recentCapacity = 500, long maxFileBytes = 5 * 1024 * 1024)
    {
        _path = path;
        _minimumLevel = minimumLevel;
        _recentCapacity = recentCapacity;
        _maxFileBytes = maxFileBytes;
        if (_path is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        }

        _writerThread = new Thread(WriteLoop) { IsBackground = true, Name = "Lockpick log writer" };
        _writerThread.Start();
    }

    /// <summary>Raised (on the logging thread) for every entry at or above the minimum level.</summary>
    public event Action<LogEntry>? EntryLogged;

    public string? FilePath => _path;

    public IReadOnlyList<LogEntry> RecentEntries => _recent.ToArray();

    public ILogger CreateLogger(string categoryName) => new JsonLinesLogger(this, categoryName);

    public void Dispose()
    {
        _queue.CompleteAdding();
        _writerThread.Join(TimeSpan.FromSeconds(2));
        _queue.Dispose();
    }

    internal bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= _minimumLevel;

    internal void Enqueue(LogEntry entry)
    {
        _recent.Enqueue(entry);
        while (_recent.Count > _recentCapacity && _recent.TryDequeue(out _))
        {
        }

        EntryLogged?.Invoke(entry);
        if (_path is not null && !_queue.IsAddingCompleted)
        {
            // Drop rather than block if the disk stalls; the monitor thread must never wait on logging.
            _queue.TryAdd(entry);
        }
    }

    private void WriteLoop()
    {
        if (_path is null)
        {
            return;
        }

        StreamWriter? writer = null;
        try
        {
            foreach (var entry in _queue.GetConsumingEnumerable())
            {
                writer ??= OpenWriter();
                writer.WriteLine(JsonSerializer.Serialize(new
                {
                    ts = entry.Timestamp,
                    level = entry.Level.ToString(),
                    category = entry.Category,
                    message = entry.Message,
                    exception = entry.Exception,
                }));

                // Batch: only flush when the queue drains or on warnings, to avoid needless disk writes.
                if (_queue.Count == 0 || entry.Level >= LogLevel.Warning)
                {
                    writer.Flush();
                    if (writer.BaseStream.Length > _maxFileBytes)
                    {
                        writer.Dispose();
                        File.Move(_path, _path + ".1", overwrite: true);
                        writer = null;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // Logging must never take the app down. Keep the in-memory ring working.
        }
        finally
        {
            writer?.Dispose();
        }
    }

    private StreamWriter OpenWriter() =>
        new(new FileStream(_path!, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = false };

    private sealed class JsonLinesLogger(JsonLinesLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Enqueue(new LogEntry(DateTimeOffset.Now, logLevel, category, formatter(state, exception), exception?.ToString()));
        }
    }
}
