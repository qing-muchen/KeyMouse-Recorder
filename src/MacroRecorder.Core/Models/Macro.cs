namespace MacroRecorder.Core.Models;

/// <summary>
/// Identity-only foundation for a reusable macro definition.
/// The event model and complete metadata belong to Phase 1.
/// Runtime recording and playback state must remain outside this definition.
/// </summary>
public sealed record Macro(Guid Id, string Name);
