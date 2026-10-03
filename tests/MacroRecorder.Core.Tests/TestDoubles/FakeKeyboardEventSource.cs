using MacroRecorder.Core.Recording.Keyboard;

namespace MacroRecorder.Core.Tests.TestDoubles;

internal sealed class FakeKeyboardEventSource : IKeyboardEventSource
{
    private bool disposed;

    public event Action<KeyboardCaptureEvent>? EventReceived;

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
            throw new InvalidOperationException("The fake keyboard source is already running.");
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

    public void Raise(KeyboardCaptureEvent capturedEvent)
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
