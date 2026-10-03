using MacroRecorder.Core.Recording.Mouse;

namespace MacroRecorder.Core.Tests.TestDoubles;

internal sealed class FakeMouseEventSource : IMouseEventSource
{
    private bool disposed;

    public event Action<MouseCaptureEvent>? EventReceived;

    public bool IsRunning { get; private set; }
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public bool IsDisposed => disposed;
    public Exception? StartException { get; set; }

    public void StartCapture()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsRunning)
        {
            throw new InvalidOperationException("The fake mouse source is already running.");
        }

        if (StartException is not null)
        {
            throw StartException;
        }

        IsRunning = true;
        StartCount++;
    }

    public void StopCapture()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;
        StopCount++;
    }

    public void Raise(MouseCaptureEvent capturedEvent)
    {
        if (IsRunning)
        {
            EventReceived?.Invoke(capturedEvent);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        StopCapture();
        disposed = true;
    }
}
