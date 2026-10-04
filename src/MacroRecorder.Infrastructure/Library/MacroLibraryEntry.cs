using MacroRecorder.Core.Models;
using MacroRecorder.Core.Validation;

namespace MacroRecorder.Infrastructure.Library;

/// <summary>Immutable library-list snapshot; it never retains a complete Macro or its Events.</summary>
public sealed record MacroLibraryEntry
{
    private MacroLibraryEntry(
        string fileName,
        string fullPath,
        MacroLibraryEntryStatus status,
        MacroLibrarySummary? summary,
        ValidationResult? validation,
        MacroLibraryError? error)
    {
        FileName = fileName;
        FullPath = fullPath;
        Status = status;
        Summary = summary;
        Validation = validation;
        Error = error;
    }

    public string FileName { get; }
    public string FullPath { get; }
    public MacroLibraryEntryStatus Status { get; }
    public MacroLibrarySummary? Summary { get; }
    public ValidationResult? Validation { get; }
    public MacroLibraryError? Error { get; }

    internal static MacroLibraryEntry FromMacro(
        string fullPath,
        Macro macro,
        ValidationResult validation) =>
        new(
            Path.GetFileName(fullPath),
            fullPath,
            validation.IsValid ? MacroLibraryEntryStatus.Valid : MacroLibraryEntryStatus.Invalid,
            MacroLibrarySummary.FromMacro(macro),
            validation,
            error: null);

    internal static MacroLibraryEntry FromError(string fullPath, Exception exception) =>
        new(
            Path.GetFileName(fullPath),
            fullPath,
            MacroLibraryEntryStatus.Unreadable,
            summary: null,
            validation: null,
            new MacroLibraryError(exception.GetType().Name, exception.Message));
}
