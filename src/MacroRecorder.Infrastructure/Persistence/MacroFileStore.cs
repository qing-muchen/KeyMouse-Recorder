using System.Diagnostics.CodeAnalysis;
using MacroRecorder.Core.Models;

namespace MacroRecorder.Infrastructure.Persistence;

/// <summary>
/// Saves and loads one explicitly addressed Macro file. Saves commit a complete same-directory
/// temporary file with a filesystem replace or move; loads never alter or consume their source.
/// </summary>
public sealed class MacroFileStore
{
    private const int FileBufferSize = 64 * 1024;
    private readonly IMacroSerializer serializer;

    public MacroFileStore()
        : this(new MacroJsonSerializer())
    {
    }

    public MacroFileStore(IMacroSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        this.serializer = serializer;
    }

    public async Task SaveAsync(
        string path,
        Macro macro,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(macro);
        cancellationToken.ThrowIfCancellationRequested();

        var targetPath = GetFilePath(path);
        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new ArgumentException("The target path must include a file name.", nameof(path));
        Directory.CreateDirectory(targetDirectory);

        var tempPath = Path.Combine(
            targetDirectory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        var tempNeedsCleanup = true;
        Exception? primaryException = null;
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                FileBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await serializer.SerializeAsync(stream, macro, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            CommitTempFile(tempPath, targetPath);
            tempNeedsCleanup = false;
        }
        catch (Exception exception)
        {
            primaryException = exception;
            throw;
        }
        finally
        {
            if (tempNeedsCleanup)
            {
                var cleanupException = TryDeleteTempFile(tempPath);
                if (cleanupException is not null && primaryException is not null)
                {
                    primaryException.Data["MacroTempCleanupException"] = cleanupException;
                }
            }
        }
    }

    public async Task<Macro> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        var sourcePath = GetFilePath(path);
        await using var stream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await serializer.DeserializeAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static string GetFilePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (string.IsNullOrEmpty(Path.GetFileName(fullPath)))
        {
            throw new ArgumentException("The path must identify a file.", nameof(path));
        }

        return fullPath;
    }

    private static void CommitTempFile(string tempPath, string targetPath) =>
        File.Move(tempPath, targetPath, overwrite: true);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Temporary-file cleanup is best effort and must never replace the primary save failure.")]
    private static Exception? TryDeleteTempFile(string tempPath)
    {
        try
        {
            File.Delete(tempPath);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
