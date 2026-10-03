using System.Text.Json.Serialization;

namespace MacroRecorder.Core.Models.Input;

/// <summary>
/// One input transition on a recording timeline. Serialize standalone events as InputEvent
/// to include the stable schema discriminator; no CLR type names are persisted.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(KeyboardInputEvent), "keyboard")]
[JsonDerivedType(typeof(MouseInputEvent), "mouse")]
public abstract record InputEvent
{
    protected InputEvent(long timestampUs, InputEventType eventType)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timestampUs);
        TimestampUs = timestampUs;
        EventType = eventType;
    }

    /// <summary>
    /// Non-negative relative microseconds since recording start, never wall-clock time.
    /// Equal timestamps are allowed; collection order resolves ties. Timeline ordering is validated later.
    /// </summary>
    public long TimestampUs { get; }

    public InputEventType EventType { get; }
}
