using MacroRecorder.Core.Models.Input;

namespace MacroRecorder.Core.Recording.Mouse;

/// <summary>
/// Applies mouse recording policy and maps captured facts into the active RecordingSession.
/// Stop the recorder before stopping its session so an in-flight callback can finish safely.
/// </summary>
public sealed class MouseRecorder : IMouseRecorder
{
    private readonly object lifecycleSync = new();
    private readonly object callbackSync = new();
    private readonly IMouseEventSource eventSource;
    private readonly MouseMoveSampler moveSampler;
    private MouseRecorderState state = MouseRecorderState.Stopped;
    private RecordingSession? activeSession;

    public MouseRecorder(IMouseEventSource eventSource, MouseMoveSampler moveSampler)
    {
        ArgumentNullException.ThrowIfNull(eventSource);
        ArgumentNullException.ThrowIfNull(moveSampler);
        this.eventSource = eventSource;
        this.moveSampler = moveSampler;
    }

    public MouseRecorderState State
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
                ObjectDisposedException.ThrowIf(state == MouseRecorderState.Disposed, this);
                if (state == MouseRecorderState.Running)
                {
                    throw new InvalidOperationException("The mouse recorder is already running.");
                }

                if (session.State != RecordingSessionState.Recording)
                {
                    throw new ArgumentException("The recording session must be active.", nameof(session));
                }

                moveSampler.Reset();
                activeSession = session;
                eventSource.EventReceived += OnEventReceived;
                state = MouseRecorderState.Running;
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
                    state = MouseRecorderState.Stopped;
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
                if (state == MouseRecorderState.Disposed)
                {
                    return;
                }

                eventSource.EventReceived -= OnEventReceived;
                activeSession = null;
                moveSampler.Reset();
                state = MouseRecorderState.Stopped;
            }

            try
            {
                eventSource.Dispose();
            }
            finally
            {
                lock (callbackSync)
                {
                    state = MouseRecorderState.Disposed;
                }
            }
        }
    }

    private void StopCore()
    {
        lock (callbackSync)
        {
            if (state == MouseRecorderState.Disposed)
            {
                return;
            }

            if (state == MouseRecorderState.Running)
            {
                eventSource.EventReceived -= OnEventReceived;
                activeSession = null;
                moveSampler.Reset();
                state = MouseRecorderState.Stopped;
            }
            else if (!eventSource.IsRunning)
            {
                return;
            }
        }

        eventSource.StopCapture();
    }

    private void OnEventReceived(MouseCaptureEvent capturedEvent)
    {
        lock (callbackSync)
        {
            if (state != MouseRecorderState.Running || activeSession is null || capturedEvent.IsInjected)
            {
                return;
            }

            if (capturedEvent.Kind == MouseCaptureKind.Move && !moveSampler.ShouldRecord())
            {
                return;
            }

            var eventType = capturedEvent.Kind switch
            {
                MouseCaptureKind.Move => InputEventType.MouseMove,
                MouseCaptureKind.ButtonDown => InputEventType.MouseButtonDown,
                MouseCaptureKind.ButtonUp => InputEventType.MouseButtonUp,
                MouseCaptureKind.VerticalWheel => InputEventType.MouseVerticalWheel,
                MouseCaptureKind.HorizontalWheel => InputEventType.MouseHorizontalWheel,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(capturedEvent), capturedEvent.Kind, "Unsupported mouse capture kind."),
            };

            activeSession.AddMouseEvent(
                eventType,
                capturedEvent.X,
                capturedEvent.Y,
                capturedEvent.Button,
                capturedEvent.WheelDelta);
        }
    }
}
