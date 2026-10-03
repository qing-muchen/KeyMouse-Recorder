using MacroRecorder.Core.Models.Input;

namespace MacroRecorder.Core.Recording.Keyboard;

/// <summary>
/// Maps captured keyboard facts into the active RecordingSession. Stop the recorder before
/// stopping its session so any callback already in progress can finish safely.
/// </summary>
public sealed class KeyboardRecorder : IKeyboardRecorder
{
    private readonly object lifecycleSync = new();
    private readonly object callbackSync = new();
    private readonly IKeyboardEventSource eventSource;
    private KeyboardRecorderState state = KeyboardRecorderState.Stopped;
    private RecordingSession? activeSession;

    public KeyboardRecorder(IKeyboardEventSource eventSource)
    {
        ArgumentNullException.ThrowIfNull(eventSource);
        this.eventSource = eventSource;
    }

    public KeyboardRecorderState State
    {
        get
        {
            lock (callbackSync)
            {
                return state;
            }
        }
    }

    public void Start(RecordingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (lifecycleSync)
        {
            lock (callbackSync)
            {
                ObjectDisposedException.ThrowIf(state == KeyboardRecorderState.Disposed, this);
                if (state == KeyboardRecorderState.Running)
                {
                    throw new InvalidOperationException("The keyboard recorder is already running.");
                }

                if (session.State != RecordingSessionState.Recording)
                {
                    throw new ArgumentException("The recording session must be active.", nameof(session));
                }

                activeSession = session;
                eventSource.EventReceived += OnEventReceived;
                state = KeyboardRecorderState.Running;
            }

            try
            {
                eventSource.StartCapture();
            }
            catch
            {
                lock (callbackSync)
                {
                    eventSource.EventReceived -= OnEventReceived;
                    activeSession = null;
                    state = KeyboardRecorderState.Stopped;
                }

                throw;
            }
        }
    }

    public void Stop()
    {
        lock (lifecycleSync)
        {
            StopCore();
        }
    }

    public void Dispose()
    {
        lock (lifecycleSync)
        {
            lock (callbackSync)
            {
                if (state == KeyboardRecorderState.Disposed)
                {
                    return;
                }

                eventSource.EventReceived -= OnEventReceived;
                activeSession = null;
                state = KeyboardRecorderState.Stopped;
            }

            try
            {
                eventSource.Dispose();
            }
            finally
            {
                lock (callbackSync)
                {
                    state = KeyboardRecorderState.Disposed;
                }
            }
        }
    }

    private void StopCore()
    {
        lock (callbackSync)
        {
            if (state == KeyboardRecorderState.Disposed)
            {
                return;
            }

            if (state == KeyboardRecorderState.Running)
            {
                eventSource.EventReceived -= OnEventReceived;
                activeSession = null;
                state = KeyboardRecorderState.Stopped;
            }
            else if (!eventSource.IsRunning)
            {
                return;
            }
        }

        eventSource.StopCapture();
    }

    private void OnEventReceived(KeyboardCaptureEvent capturedEvent)
    {
        lock (callbackSync)
        {
            if (state != KeyboardRecorderState.Running || activeSession is null || capturedEvent.IsInjected)
            {
                return;
            }

            var eventType = capturedEvent.Transition switch
            {
                KeyboardTransition.KeyDown => InputEventType.KeyboardKeyDown,
                KeyboardTransition.KeyUp => InputEventType.KeyboardKeyUp,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(capturedEvent), capturedEvent.Transition, "Unsupported keyboard transition."),
            };

            activeSession.AddKeyboardEvent(
                eventType,
                capturedEvent.VirtualKey,
                capturedEvent.ScanCode,
                capturedEvent.Flags);
        }
    }
}
