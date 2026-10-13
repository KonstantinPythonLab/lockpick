using System.Text.Json;
using System.Text.Json.Serialization;
using Lockpick.Core.Events;
using Lockpick.Core.State;

namespace Lockpick.Core.Recording;

public sealed record RecordingHeader(string Format, int Version, string GameId, string? ProfileId, DateTimeOffset RecordedAt)
{
    public const string FormatName = "lockpick-recording";
}

/// <summary>One recorded snapshot and its offset from the start of the recording.</summary>
public sealed record RecordedFrame(double OffsetMs, GameSnapshot Snapshot);

/// <summary>
/// JSON Lines session recordings: a header line followed by one line per snapshot change.
/// Used for test fixtures, bug reports and UI playback without the game.
/// </summary>
public static class SessionRecording
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static (RecordingHeader Header, IReadOnlyList<RecordedFrame> Frames) Read(TextReader reader)
    {
        var headerLine = reader.ReadLine() ?? throw new InvalidDataException("Recording is empty.");
        var header = JsonSerializer.Deserialize<RecordingHeader>(headerLine, JsonOptions)
            ?? throw new InvalidDataException("Recording header is missing.");
        if (header.Format != RecordingHeader.FormatName || header.Version != 1)
        {
            throw new InvalidDataException($"Unsupported recording format '{header.Format}' v{header.Version}.");
        }

        var frames = new List<RecordedFrame>();
        string? line;
        var lineNumber = 1;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                frames.Add(JsonSerializer.Deserialize<RecordedFrame>(line, JsonOptions)
                    ?? throw new InvalidDataException($"Line {lineNumber} is empty."));
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Line {lineNumber} is not a valid frame: {ex.Message}", ex);
            }
        }

        return (header, frames);
    }

    public static (RecordingHeader Header, IReadOnlyList<RecordedFrame> Frames) ReadFile(string path)
    {
        using var reader = new StreamReader(path);
        return Read(reader);
    }

    /// <summary>Runs recorded frames through the event detector, exactly as the live monitor would.</summary>
    public static IReadOnlyList<GameEvent> DetectEvents(IEnumerable<RecordedFrame> frames) =>
        EventDetector.DetectAll(frames.Select(f => f.Snapshot));
}

/// <summary>Appends snapshot changes to a recording file. Writes are buffered; call <see cref="Dispose"/> to flush.</summary>
public sealed class SessionRecorder : IDisposable
{
    private readonly TextWriter _writer;
    private readonly DateTimeOffset _start;
    private readonly object _gate = new();
    private bool _disposed;

    public SessionRecorder(TextWriter writer, string gameId, string? profileId, DateTimeOffset start)
    {
        _writer = writer;
        _start = start;
        _writer.WriteLine(JsonSerializer.Serialize(new RecordingHeader(RecordingHeader.FormatName, 1, gameId, profileId, start), SessionRecording.JsonOptions));
    }

    public string? Path { get; private init; }

    public int FrameCount { get; private set; }

    public static SessionRecorder Create(string path, string gameId, string? profileId, DateTimeOffset start)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var writer = new StreamWriter(path, append: false) { AutoFlush = false };
        return new SessionRecorder(writer, gameId, profileId, start) { Path = path };
    }

    public void Append(GameSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var frame = new RecordedFrame((snapshot.CapturedAt - _start).TotalMilliseconds, snapshot);
            _writer.WriteLine(JsonSerializer.Serialize(frame, SessionRecording.JsonOptions));
            FrameCount++;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }
}
