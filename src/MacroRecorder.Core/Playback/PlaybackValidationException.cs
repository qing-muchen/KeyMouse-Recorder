using MacroRecorder.Core.Validation;

namespace MacroRecorder.Core.Playback;

/// <summary>Raised before playback starts when a Macro has validation errors.</summary>
public sealed class PlaybackValidationException : Exception
{
    public PlaybackValidationException(ValidationResult validationResult)
        : base("Macro cannot be played because validation failed.")
    {
        ArgumentNullException.ThrowIfNull(validationResult);
        ValidationResult = validationResult;
    }

    public ValidationResult ValidationResult { get; }
}
