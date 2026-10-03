using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacroRecorder.Core.Diagnostics;

namespace MacroRecorder.Infrastructure.Diagnostics;

/// <summary>
/// A single application instance owns one JSON-lines log. No event payloads,
/// exception messages, window titles, or keyboard text are accepted as log fields.
/// Write failures propagate so callers can report them instead of silently losing logs.
/// </summary>
public sealed class LocalFileLogger : IAppLogger, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object sync = new();
    private readonly StreamWriter writer;
    private bool disposed;

    public LocalFileLogger(string logsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);
        Directory.CreateDirectory(logsDirectory);
        FilePath = Path.Combine(logsDirectory, $"session-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
        writer = new StreamWriter(FilePath, append: false, new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
    }

    public string FilePath { get; }

    public void Write(AppLogLevel level, AppLogEvent eventId, Exception? exception = null)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var entry = new LogEntry(DateTimeOffset.UtcNow, level, eventId,
                exception?.GetType().FullName, exception?.HResult);
            writer.WriteLine(JsonSerializer.Serialize(entry, SerializerOptions));
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            writer.Dispose();
        }
    }

    private sealed record LogEntry(
        DateTimeOffset TimestampUtc,
        AppLogLevel Level,
        AppLogEvent EventId,
        string? ExceptionType,
        int? ErrorCode);
}
