using System.Text.Json;
using MacroRecorder.Core.Diagnostics;
using MacroRecorder.Infrastructure.Diagnostics;

namespace MacroRecorder.Core.Tests;

public sealed class LocalFileLoggerTests
{
    [Fact]
    public void WritesStructuredEventsWithoutExceptionContent()
    {
        using var directory = new TestDirectory();
        using var logger = new LocalFileLogger(directory.Path);
        logger.Write(AppLogLevel.Information, AppLogEvent.ApplicationStarted);
        logger.Write(AppLogLevel.Error, AppLogEvent.StartupFailed, new IOException("private-input-sentinel"));

        var lines = ReadActiveLog(logger.FilePath);
        Assert.Equal(2, lines.Length);
        using var entry = JsonDocument.Parse(lines[1]);
        Assert.Equal("Error", entry.RootElement.GetProperty("level").GetString());
        Assert.Equal("StartupFailed", entry.RootElement.GetProperty("eventId").GetString());
        Assert.Equal("System.IO.IOException", entry.RootElement.GetProperty("exceptionType").GetString());
        Assert.Equal(TimeSpan.Zero, entry.RootElement.GetProperty("timestampUtc").GetDateTimeOffset().Offset);
        Assert.DoesNotContain("private-input-sentinel", string.Join('\n', lines), StringComparison.Ordinal);
    }

    [Fact]
    public void ConcurrentWritesRemainCompleteJsonLines()
    {
        using var directory = new TestDirectory();
        using var logger = new LocalFileLogger(directory.Path);
        Parallel.For(0, 100, _ => logger.Write(AppLogLevel.Information, AppLogEvent.ApplicationStarted));

        var lines = ReadActiveLog(logger.FilePath);
        Assert.Equal(100, lines.Length);
        foreach (var line in lines)
        {
            using var entry = JsonDocument.Parse(line);
            Assert.Equal("ApplicationStarted", entry.RootElement.GetProperty("eventId").GetString());
        }
    }

    [Fact]
    public void DisposeIsIdempotentReleasesFileAndRejectsFurtherWrites()
    {
        using var directory = new TestDirectory();
        using var logger = new LocalFileLogger(directory.Path);
        logger.Write(AppLogLevel.Information, AppLogEvent.ApplicationStopped);
        logger.Dispose();
        logger.Dispose();

        Assert.Throws<ObjectDisposedException>(() => logger.Write(AppLogLevel.Information, AppLogEvent.ApplicationStarted));
        using var exclusive = new FileStream(logger.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.Length > 0);
    }

    [Fact]
    public void SeparateInstancesDoNotOverwriteEachOther()
    {
        using var directory = new TestDirectory();
        using var first = new LocalFileLogger(directory.Path);
        using var second = new LocalFileLogger(directory.Path);
        first.Write(AppLogLevel.Information, AppLogEvent.ApplicationStarted);
        second.Write(AppLogLevel.Information, AppLogEvent.ApplicationStarted);

        Assert.NotEqual(first.FilePath, second.FilePath);
        Assert.Single(ReadActiveLog(first.FilePath));
        Assert.Single(ReadActiveLog(second.FilePath));
    }

    private static string[] ReadActiveLog(string path)
    {
        // Windows readers must share write access while the logger still owns its writer.
        using var reader = new StreamReader(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite,
        });
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
}
