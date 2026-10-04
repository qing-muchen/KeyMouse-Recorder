namespace MacroRecorder.Infrastructure.Library;

/// <summary>Describes whether a library file can be loaded and safely used.</summary>
public enum MacroLibraryEntryStatus
{
    Valid,
    Invalid,
    Unreadable,
}
