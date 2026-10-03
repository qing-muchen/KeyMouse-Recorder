using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using MacroRecorder.Core.Recording.Keyboard;
using MacroRecorder.Core.Recording.Mouse;
using MacroRecorder.Core.Time;

namespace MacroRecorder.Core.Recording;

/// <summary>
/// Owns one complete keyboard-and-mouse recording lifecycle. Input callbacks continue to flow
/// directly from component recorders to RecordingSession and never pass through this coordinator.
/// </summary>
public sealed class UnifiedRecorder : IDisposable
{
    private readonly object lifecycleSync = new();
    private readonly object stateSync = new();
    private readonly IMonotonicClock clock;
    private readonly IKeyboardRecorder keyboardRecorder;
    private readonly IMouseRecorder mouseRecorder;
    private UnifiedRecorderState state = UnifiedRecorderState.Stopped;
    private RecordingSession? activeSession;

    /// <summary>
    /// Creates a coordinator that owns and disposes both component recorders.
    /// </summary>
    public UnifiedRecorder(
        IMonotonicClock clock,
        IKeyboardRecorder keyboardRecorder,
        IMouseRecorder mouseRecorder)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(keyboardRecorder);
        ArgumentNullException.ThrowIfNull(mouseRecorder);

        this.clock = clock;
        this.keyboardRecorder = keyboardRecorder;
        this.mouseRecorder = mouseRecorder;
    }

    public UnifiedRecorderState State
    {
        get
        {
            lock (stateSync)
            {
                return state;
            }
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Every startup failure must trigger rollback before it is rethrown or aggregated.")]
    public void StartRecording()
    {
        lock (lifecycleSync)
        {
            EnsureStopped();
            SetState(UnifiedRecorderState.Starting);

            RecordingSession? session = null;
            var keyboardAttempted = false;
            var mouseAttempted = false;
            try
            {
                session = new RecordingSession(clock);
                activeSession = session;

                keyboardAttempted = true;
                keyboardRecorder.Start(session);

                mouseAttempted = true;
                mouseRecorder.Start(session);

                SetState(UnifiedRecorderState.Recording);
            }
            catch (Exception startupException)
            {
                var errors = new List<Exception> { startupException };
                if (mouseAttempted)
                {
                    TryCleanup(mouseRecorder.Stop, errors);
                }

                if (keyboardAttempted)
                {
                    TryCleanup(keyboardRecorder.Stop, errors);
                }

                if (session is not null)
                {
                    TryCleanup(session.Cancel, errors);
                }

                activeSession = null;
                SetState(UnifiedRecorderState.Stopped);
                ThrowCollected("Unified recording startup failed.", errors);
            }
        }
    }

    public RecordingResult StopRecording()
    {
        lock (lifecycleSync)
        {
            var session = GetRecordingSession();
            SetState(UnifiedRecorderState.Stopping);

            var errors = new List<Exception>();
            TryCleanup(keyboardRecorder.Stop, errors);
            TryCleanup(mouseRecorder.Stop, errors);

            RecordingResult? result = null;
            TryCleanup(() => result = session.Stop(), errors);

            activeSession = null;
            SetState(UnifiedRecorderState.Stopped);
            if (errors.Count > 0)
            {
                ThrowCollected("Unified recording stop failed.", errors);
            }

            return result ?? throw new InvalidOperationException("The recording session produced no result.");
        }
    }

    public void CancelRecording()
    {
        lock (lifecycleSync)
        {
            var session = GetRecordingSession();
            SetState(UnifiedRecorderState.Cancelling);

            var errors = new List<Exception>();
            TryCleanup(keyboardRecorder.Stop, errors);
            TryCleanup(mouseRecorder.Stop, errors);
            TryCleanup(session.Cancel, errors);

            activeSession = null;
            SetState(UnifiedRecorderState.Stopped);
            if (errors.Count > 0)
            {
                ThrowCollected("Unified recording cancellation failed.", errors);
            }
        }
    }

    public void Dispose()
    {
        lock (lifecycleSync)
        {
            if (State == UnifiedRecorderState.Disposed)
            {
                return;
            }

            var errors = new List<Exception>();
            var session = activeSession;
            if (session is not null)
            {
                SetState(UnifiedRecorderState.Cancelling);
                TryCleanup(keyboardRecorder.Stop, errors);
                TryCleanup(mouseRecorder.Stop, errors);
                TryCleanup(session.Cancel, errors);
            }

            TryCleanup(keyboardRecorder.Dispose, errors);
            TryCleanup(mouseRecorder.Dispose, errors);

            activeSession = null;
            SetState(UnifiedRecorderState.Disposed);
            if (errors.Count > 0)
            {
                ThrowCollected("Unified recorder disposal failed.", errors);
            }
        }
    }

    private void EnsureStopped()
    {
        var currentState = State;
        ObjectDisposedException.ThrowIf(currentState == UnifiedRecorderState.Disposed, this);
        if (currentState != UnifiedRecorderState.Stopped)
        {
            throw new InvalidOperationException($"Cannot start recording while the unified recorder is {currentState}.");
        }
    }

    private RecordingSession GetRecordingSession()
    {
        var currentState = State;
        ObjectDisposedException.ThrowIf(currentState == UnifiedRecorderState.Disposed, this);
        if (currentState != UnifiedRecorderState.Recording || activeSession is null)
        {
            throw new InvalidOperationException($"Cannot finish recording while the unified recorder is {currentState}.");
        }

        return activeSession;
    }

    private void SetState(UnifiedRecorderState value)
    {
        lock (stateSync)
        {
            state = value;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Best-effort cleanup must retain one failure while continuing every remaining cleanup step.")]
    private static void TryCleanup(Action action, List<Exception> errors)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }
    }

    [DoesNotReturn]
    private static void ThrowCollected(string message, List<Exception> errors)
    {
        if (errors.Count == 1)
        {
            ExceptionDispatchInfo.Capture(errors.First()).Throw();
        }

        throw new AggregateException(message, errors);
    }
}
