using MacroRecorder.Core.Recording;
using MacroRecorder.Core.Recording.Mouse;

namespace MacroRecorder.Core.Tests.TestDoubles;

internal sealed class FakeMouseRecorder(ICollection<string>? operations = null) : IMouseRecorder
{
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public int DisposeCount { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsDisposed { get; private set; }
    public RecordingSession? LastSession { get; private set; }
    public Exception? StartException { get; set; }
    public Exception? StopException { get; set; }
    public Exception? DisposeException { get; set; }
    public Action? OnStop { get; set; }

    public void Start(RecordingSession session)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        StartCount++;
        LastSession = session;
        IsRunning = true;
        operations?.Add("mouse.start");

        if (StartException is not null)
        {
            throw StartException;
        }
    }

    public void Stop()
    {
        StopCount++;
        IsRunning = false;
        operations?.Add("mouse.stop");
        OnStop?.Invoke();

        if (StopException is not null)
        {
            throw StopException;
        }
    }

    public void Dispose()
    {
        DisposeCount++;
        IsRunning = false;
        IsDisposed = true;
        operations?.Add("mouse.dispose");

        if (DisposeException is not null)
        {
            throw DisposeException;
        }
    }
}
