using MacroRecorder.Core.Models.Input;

namespace MacroRecorder.Core.InputInjection;

/// <summary>Injects exactly one domain input event without applying timeline delays.</summary>
public interface IInputInjector
{
    void Inject(InputEvent inputEvent);
}
