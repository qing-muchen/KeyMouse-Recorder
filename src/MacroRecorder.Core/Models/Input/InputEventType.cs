namespace MacroRecorder.Core.Models.Input;

/// <summary>Names are part of schema v1 when serialized with the string enum converter.</summary>
public enum InputEventType
{
    KeyboardKeyDown,
    KeyboardKeyUp,
    MouseMove,
    MouseButtonDown,
    MouseButtonUp,
    MouseVerticalWheel,
    MouseHorizontalWheel,
}
