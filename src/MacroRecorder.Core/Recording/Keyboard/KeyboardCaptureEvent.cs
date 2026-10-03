namespace MacroRecorder.Core.Recording.Keyboard;

/// <summary>
/// Lightweight, non-persisted keyboard capture facts supplied by an input source.
/// RecordingSession assigns the domain timeline timestamp.
/// </summary>
public readonly record struct KeyboardCaptureEvent(
    KeyboardTransition Transition,
    uint VirtualKey,
    uint ScanCode,
    uint Flags,
    bool IsInjected);
