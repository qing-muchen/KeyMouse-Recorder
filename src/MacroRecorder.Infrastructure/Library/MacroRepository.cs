using System.Collections.Immutable;
using System.Text.Json;
using MacroRecorder.Core.Models;
using MacroRecorder.Core.Validation;
using MacroRecorder.Infrastructure.Persistence;

namespace MacroRecorder.Infrastructure.Library;

/// <summary>Manages one non-recursive directory of local Macro JSON assets.</summary>
public sealed class MacroRepository
{
    private static readonly StringComparer FileNameComparer = StringComparer.OrdinalIgnoreCase;
    private readonly MacroFileStore fileStore;

    public MacroRepository(string libraryRootPath)
        : this(libraryRootPath, new MacroFileStore())
    {
    }

    public MacroRepository(string libraryRootPath, MacroFileStore fileStore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRootPath);
        ArgumentNullException.ThrowIfNull(fileStore);
        if (!Path.IsPathFullyQualified(libraryRootPath))
        {
            throw new ArgumentException("The Macro library root must be an absolute path.", nameof(libraryRootPath));
        }

        LibraryRootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryRootPath));
        this.fileStore = fileStore;
    }

    public string LibraryRootPath { get; }

    /// <summary>Scans current disk state and returns immutable summaries in filename order.</summary>
    public async Task<ImmutableArray<MacroLibraryEntry>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(LibraryRootPath);

        var files = Directory
            .EnumerateFiles(LibraryRootPath, "*", SearchOption.TopDirectoryOnly)
            .Where(static path => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => Path.GetFileName(path), FileNameComparer)
            .ThenBy(static path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        var entries = ImmutableArray.CreateBuilder<MacroLibraryEntry>(files.Length);
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(await ReadEntryAsync(path, cancellationToken).ConfigureAwait(false));
        }

        return entries.MoveToImmutable();
    }

    public async Task<MacroLoadResult> LoadAsync(
        string fileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveFilePath(fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The Macro file does not exist.", path);
        }

        var macro = await fileStore.LoadAsync(path, cancellationToken).ConfigureAwait(false);
        return new MacroLoadResult(macro, MacroValidator.Validate(macro));
    }

    /// <summary>
    /// Saves or overwrites one validated asset. Supplying a different filename provides Save As semantics.
    /// </summary>
    public async Task<ValidationResult> SaveAsync(
        string fileName,
        Macro macro,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(macro);
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveFilePath(fileName);
        var validation = MacroValidator.Validate(macro);
        if (!validation.IsValid)
        {
            throw new MacroValidationException(validation);
        }

        await fileStore.SaveAsync(path, macro, cancellationToken).ConfigureAwait(false);
        return validation;
    }

    public Task RenameAsync(
        string oldFileName,
        string newFileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePath = ResolveFilePath(oldFileName);
        var destinationPath = ResolveFilePath(newFileName);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The source Macro file does not exist.", sourcePath);
        }

        if (string.Equals(sourcePath, destinationPath, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Case-only Macro file renames are not supported in Phase 8.");
        }

        if (File.Exists(destinationPath))
        {
            throw new IOException("The destination Macro file already exists.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        File.Move(sourcePath, destinationPath);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveFilePath(fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The Macro file does not exist.", path);
        }

        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(path);
        return Task.CompletedTask;
    }

    private async Task<MacroLibraryEntry> ReadEntryAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var macro = await fileStore.LoadAsync(path, cancellationToken).ConfigureAwait(false);
            return MacroLibraryEntry.FromMacro(path, macro, MacroValidator.Validate(macro));
        }
        catch (JsonException exception)
        {
            return MacroLibraryEntry.FromError(path, exception);
        }
        catch (IOException exception)
        {
            return MacroLibraryEntry.FromError(path, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return MacroLibraryEntry.FromError(path, exception);
        }
    }

    private string ResolveFilePath(string fileName)
    {
        var normalizedFileName = NormalizeFileName(fileName);
        var candidate = Path.GetFullPath(Path.Combine(LibraryRootPath, normalizedFileName));
        var parent = Path.GetDirectoryName(candidate);
        if (!string.Equals(parent, LibraryRootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The filename must remain directly inside the Macro library.", nameof(fileName));
        }

        return candidate;
    }

    private static string NormalizeFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (Path.IsPathFullyQualified(fileName)
            || fileName is "." or ".."
            || fileName.Contains(Path.DirectorySeparatorChar)
            || fileName.Contains(Path.AltDirectorySeparatorChar)
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("A Macro filename must not contain a path or invalid characters.", nameof(fileName));
        }

        var extension = Path.GetExtension(fileName);
        string normalizedFileName;
        if (extension.Length == 0)
        {
            normalizedFileName = fileName + ".json";
        }
        else if (!string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A Macro filename must use the .json extension.", nameof(fileName));
        }
        else
        {
            normalizedFileName = fileName;
        }

        if (string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(normalizedFileName)))
        {
            throw new ArgumentException("A Macro filename must include a name before its extension.", nameof(fileName));
        }

        return normalizedFileName;
    }
}
