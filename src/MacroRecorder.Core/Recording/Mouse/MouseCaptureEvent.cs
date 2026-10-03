using MacroRecorder.Core.Models.Input;

namespace MacroRecorder.Core.Recording.Mouse;

/// <summary>
/// Lightweight, non-persisted mouse capture facts supplied by an input source.
/// RecordingSession assigns the domain timeline timestamp.
/// </summary>
public readonly record struct MouseCaptureEvent(
    MouseCaptureKind Kind,
    int X,
    int Y,
    MouseButton Button,
    int WheelDelta,
    bool IsInjected);
