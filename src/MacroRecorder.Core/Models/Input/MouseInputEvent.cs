namespace MacroRecorder.Core.Models.Input;

public sealed record MouseInputEvent : InputEvent
{
    public MouseInputEvent(long timestampUs, InputEventType eventType, int x, int y, MouseButton button, int wheelDelta)
        : base(timestampUs, eventType)
    {
        if (eventType is not (InputEventType.MouseMove or InputEventType.MouseButtonDown or InputEventType.MouseButtonUp
            or InputEventType.MouseVerticalWheel or InputEventType.MouseHorizontalWheel))
        {
            throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "A mouse event must use a mouse event type.");
        }

        X = x;
        Y = y;
        Button = button;
        WheelDelta = wheelDelta;
    }

    /// <summary>Absolute screen coordinate in pixels; negative coordinates are valid.</summary>
    public int X { get; }

    /// <summary>Absolute screen coordinate in pixels; negative coordinates are valid.</summary>
    public int Y { get; }

    /// <summary>None for move/wheel; the specific button for down/up. Combination validation comes later.</summary>
    public MouseButton Button { get; }

    /// <summary>Raw signed wheel delta, including high-resolution values; zero for non-wheel events.</summary>
    public int WheelDelta { get; }
}
