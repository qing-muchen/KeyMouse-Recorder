namespace MacroRecorder.Core.Validation;

/// <summary>A stable machine code plus human-readable context for one Macro problem.</summary>
public sealed record ValidationIssue
{
    public ValidationIssue(ValidationSeverity severity, string code, string message, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Severity = severity;
        Code = code;
        Message = message;
        Path = path;
    }

    public ValidationSeverity Severity { get; }
    public string Code { get; }
    public string Message { get; }
    public string Path { get; }
}
