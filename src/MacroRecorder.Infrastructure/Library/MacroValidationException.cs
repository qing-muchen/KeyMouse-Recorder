using MacroRecorder.Core.Validation;

namespace MacroRecorder.Infrastructure.Library;

/// <summary>Raised when an invalid Macro is offered as a formal library asset.</summary>
public sealed class MacroValidationException : Exception
{
    public MacroValidationException(ValidationResult validationResult)
        : base("Macro failed validation and was not saved.")
    {
        ArgumentNullException.ThrowIfNull(validationResult);
        ValidationResult = validationResult;
    }

    public ValidationResult ValidationResult { get; }
}
