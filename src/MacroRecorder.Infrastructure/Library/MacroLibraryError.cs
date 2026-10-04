namespace MacroRecorder.Infrastructure.Library;

/// <summary>Short diagnostic for an unreadable library file; contains no stack trace or file content.</summary>
public sealed record MacroLibraryError(string ErrorType, string Message);
