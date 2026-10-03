using MacroRecorder.Core.Recording;
using MacroRecorder.Core.Recording.Keyboard;

namespace MacroRecorder.Core.Tests.TestDoubles;

internal sealed class FakeKeyboardRecorder(ICollection<string>? operations = null) : IKeyboardRecorder
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
    public Action? OnStart { get; set; }
    public Action? OnStop { get; set; }

    public void Start(RecordingSession session)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        StartCount++;
        LastSession = session;
        IsRunning = true;
        operations?.Add("keyboard.start");
        OnStart?.Invoke();

        if (StartException is not null)
        {
            throw StartException;
        }
    }

    public void Stop()
    {
        StopCount++;
        IsRunning = false;
        operations?.Add("keyboard.stop");
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
        operations?.Add("keyboard.dispose");

        if (DisposeException is not null)
        {
            throw DisposeException;
        }
    }
}
