namespace MacroRecorder.Core.Models.Input;

public sealed record KeyboardInputEvent : InputEvent
{
    public KeyboardInputEvent(long timestampUs, InputEventType eventType, uint virtualKey, uint scanCode, uint flags)
        : base(timestampUs, eventType)
    {
        if (eventType is not (InputEventType.KeyboardKeyDown or InputEventType.KeyboardKeyUp))
        {
            throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "A keyboard event must be key down or key up.");
        }

        VirtualKey = virtualKey;
        ScanCode = scanCode;
        Flags = flags;
    }

    public uint VirtualKey { get; }
    public uint ScanCode { get; }

    /// <summary>
    /// Raw flags supplied by the input source, preserved without interpretation or filtering.
    /// Platform adapters own mapping these bits; Core does not depend on platform constants.
    /// </summary>
    public uint Flags { get; }
}
