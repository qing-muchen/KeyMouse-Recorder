using MacroRecorder.Core.Models;

namespace MacroRecorder.Infrastructure.Library;

/// <summary>Small metadata snapshot that deliberately excludes the Macro event collection.</summary>
public sealed record MacroLibrarySummary(
    Guid Id,
    string Name,
    string Description,
    long DurationUs,
    int EventCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    internal static MacroLibrarySummary FromMacro(Macro macro) => new(
        macro.Id,
        macro.Name,
        macro.Description,
        macro.Recording.DurationUs,
        macro.Recording.EventCount,
        macro.CreatedAt,
        macro.UpdatedAt);
}
