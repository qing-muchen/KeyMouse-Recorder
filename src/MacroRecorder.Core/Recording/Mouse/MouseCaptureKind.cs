namespace MacroRecorder.Core.Recording.Mouse;

/// <summary>Managed mouse action independent of Windows message identifiers.</summary>
public enum MouseCaptureKind
{
    Move,
    ButtonDown,
    ButtonUp,
    VerticalWheel,
    HorizontalWheel,
}
