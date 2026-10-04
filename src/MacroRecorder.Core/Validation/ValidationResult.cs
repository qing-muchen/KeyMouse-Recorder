using System.Collections.Immutable;

namespace MacroRecorder.Core.Validation;

/// <summary>Immutable result of validating one Macro definition.</summary>
public sealed record ValidationResult
{
    public ValidationResult(ImmutableArray<ValidationIssue> issues)
    {
        if (issues.IsDefault)
        {
            throw new ArgumentException("Issues must be an initialized immutable array.", nameof(issues));
        }

        Issues = issues;
    }

    public ImmutableArray<ValidationIssue> Issues { get; }

    /// <summary>Warnings are advisory; any error makes the Macro invalid.</summary>
    public bool IsValid => !Issues.Any(static issue => issue.Severity == ValidationSeverity.Error);
}
