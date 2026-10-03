namespace MacroRecorder.Core.Recording.Keyboard;

/// <summary>Restartable source of keyboard transitions. Implementations own their capture resources.</summary>
public interface IKeyboardEventSource : IDisposable
{
    event Action<KeyboardCaptureEvent>? EventReceived;

    bool IsRunning { get; }

    void StartCapture();

    void StopCapture();
}
