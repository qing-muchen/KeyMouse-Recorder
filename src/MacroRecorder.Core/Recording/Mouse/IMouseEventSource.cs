namespace MacroRecorder.Core.Recording.Mouse;

/// <summary>Restartable source of mouse actions. Implementations own their capture resources.</summary>
public interface IMouseEventSource : IDisposable
{
    event Action<MouseCaptureEvent>? EventReceived;

    bool IsRunning { get; }

    void StartCapture();

    void StopCapture();
}
