using MacroRecorder.Core.Models;
using MacroRecorder.Core.Validation;

namespace MacroRecorder.Infrastructure.Library;

/// <summary>A complete explicitly loaded Macro together with its semantic validation result.</summary>
public sealed record MacroLoadResult(Macro Macro, ValidationResult Validation)
{
    public bool IsValid => Validation.IsValid;
}
