using System.Diagnostics.CodeAnalysis;
using MacroRecorder.Core.Models.Input;
using MacroRecorder.Core.Recording;
using MacroRecorder.Core.Recording.Keyboard;
using MacroRecorder.Core.Recording.Mouse;
using MacroRecorder.Core.Tests.TestDoubles;

namespace MacroRecorder.Core.Tests.Recording;

[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
    Justification = "Tests transfer component recorder ownership to UnifiedRecorder, which disposes both dependencies.")]
public sealed class UnifiedRecorderTests
{
    [Fact]
    public void StartsKeyboardThenMouseWithOneOwnedSession()
    {
        var operations = new List<string>();
        var keyboard = new FakeKeyboardRecorder(operations);
        var mouse = new FakeMouseRecorder(operations);
        using var recorder = new UnifiedRecorder(new FakeMonotonicClock(), keyboard, mouse);

        recorder.StartRecording();

        Assert.Equal(UnifiedRecorderState.Recording, recorder.State);
        Assert.Equal(["keyboard.start", "mouse.start"], operations);
        Assert.Same(keyboard.LastSession, mouse.LastSession);
        Assert.Equal(RecordingSessionState.Recording, keyboard.LastSession?.State);
        recorder.CancelRecording();
    }

    [Fact]
    public void StopEndsSourcesBeforeSessionAndReturnsResult()
    {
        var operations = new List<string>();
        var keyboard = new FakeKeyboardRecorder(operations);
        var mouse = new FakeMouseRecorder(operations);
        var clock = new FakeMonotonicClock(10_000);
        using var recorder = new UnifiedRecorder(clock, keyboard, mouse);
        recorder.StartRecording();
        var session = Assert.IsType<RecordingSession>(keyboard.LastSession);
        keyboard.OnStop = () => Assert.Equal(RecordingSessionState.Recording, session.State);
        mouse.OnStop = () => Assert.Equal(RecordingSessionState.Recording, session.State);
        session.AddKeyboardEvent(InputEventType.KeyboardKeyDown, 65, 30, 0);
        clock.AdvanceMicroseconds(500);

        var result = recorder.StopRecording();

        Assert.Equal(["keyboard.start", "mouse.start", "keyboard.stop", "mouse.stop"], operations);
        Assert.Equal(RecordingSessionState.Stopped, session.State);
        Assert.Equal(UnifiedRecorderState.Stopped, recorder.State);
        Assert.Equal(500, result.DurationUs);
        Assert.Single(result.Events);
    }

    [Fact]
    public void EmptyRecordingCanStopNormally()
    {
        var clock = new FakeMonotonicClock();
        using var recorder = new UnifiedRecorder(clock, new FakeKeyboardRecorder(), new FakeMouseRecorder());
        recorder.StartRecording();
        clock.AdvanceMicroseconds(125);

        var result = recorder.StopRecording();

        Assert.Empty(result.Events);
        Assert.Equal(0, result.EventCount);
        Assert.Equal(125, result.DurationUs);
    }

    [Fact]
    public void DuplicateStartIsRejectedWithoutStartingMoreComponents()
    {
        var keyboard = new FakeKeyboardRecorder();
        var mouse = new FakeMouseRecorder();
        using var recorder = new UnifiedRecorder(new FakeMonotonicClock(), keyboard, mouse);
        recorder.StartRecording();

        Assert.Throws<InvalidOperationException>(recorder.StartRecording);

        Assert.Equal(1, keyboard.StartCount);
        Assert.Equal(1, mouse.StartCount);
        recorder.CancelRecording();
    }

    [Fact]
    public void StopAndCancelRequireActiveRecording()
    {
        using var recorder = new UnifiedRecorder(
            new FakeMonotonicClock(), new FakeKeyboardRecorder(), new FakeMouseRecorder());

        Assert.Throws<InvalidOperationException>(() => recorder.StopRecording());
        Assert.Throws<InvalidOperationException>(recorder.CancelRecording);
    }

    [Fact]
    public void KeyboardStartFailureRollsBackAttemptAndAllowsRetry()
    {
        var failure = new InvalidOperationException("keyboard start failed");
        var keyboard = new FakeKeyboardRecorder { StartException = failure };
        var mouse = new FakeMouseRecorder();
        using var recorder = new UnifiedRecorder(new FakeMonotonicClock(), keyboard, mouse);

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(recorder.StartRecording));

        Assert.False(keyboard.IsRunning);
        Assert.Equal(1, keyboard.StopCount);
        Assert.Equal(0, mouse.StartCount);
        Assert.Equal(RecordingSessionState.Cancelled, keyboard.LastSession?.State);
        Assert.Equal(UnifiedRecorderState.Stopped, recorder.State);

        keyboard.StartException = null;
        recorder.StartRecording();
        recorder.CancelRecording();
    }

    [Fact]
    public void MouseStartFailureRollsBackBothComponentsAndSession()
    {
        var operations = new List<string>();
        var keyboard = new FakeKeyboardRecorder(operations);
        var failure = new InvalidOperationException("mouse start failed");
        var mouse = new FakeMouseRecorder(operations) { StartException = failure };
        using var recorder = new UnifiedRecorder(new FakeMonotonicClock(), keyboard, mouse);

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(recorder.StartRecording));

        Assert.Equal(
            ["keyboard.start", "mouse.start", "mouse.stop", "keyboard.stop"], operations);
        Assert.False(keyboard.IsRunning);
        Assert.False(mouse.IsRunning);
        Assert.Equal(RecordingSessionState.Cancelled, keyboard.LastSession?.State);
        Assert.Equal(UnifiedRecorderState.Stopped, recorder.State);
    }

    [Fact]
    public void StartupRollbackAggregatesEveryCleanupFailure()
    {
        var keyboard = new FakeKeyboardRecorder
        {
            StopException = new InvalidOperationException("keyboard stop failed"),
        };
        var mouse = new FakeMouseRecorder
        {
            StartException = new InvalidOperationException("mouse start failed"),
            StopException = new InvalidOperationException("mouse stop failed"),
        };
        using var recorder = new UnifiedRecorder(new FakeMonotonicClock(), keyboard, mouse);

        var error = Assert.Throws<AggregateException>(recorder.StartRecording);

        Assert.Equal(3, error.InnerExceptions.Count);
        Assert.Equal(1, keyboard.StopCount);
        Assert.Equal(1, mouse.StopCount);
        Assert.Equal(RecordingSessionState.Cancelled, keyboard.LastSession?.State);
        Assert.Equal(UnifiedRecorderState.Stopped, recorder.State);
    }

    [Fact]
    public void StopFailureStillStopsEveryComponentAndSession()
    {
        var keyboard = new FakeKeyboardRecorder
        {
            StopException = new InvalidOperationException("keyboard stop failed"),
        };
        var mouse = new FakeMouseRecorder
        {
            StopException = new InvalidOperationException("mouse stop failed"),
        };
        using var recorder = new UnifiedRecorder(new FakeMonotonicClock(), keyboard, mouse);
        recorder.StartRecording();
        var session = Assert.IsType<RecordingSession>(keyboard.LastSession);

        var error = Assert.Throws<AggregateException>(() => recorder.StopRecording());

        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.Equal(1, keyboard.StopCount);
        Assert.Equal(1, mouse.StopCount);
        Assert.Equal(RecordingSessionState.Stopped, session.State);
        Assert.Equal(UnifiedRecorderState.Stopped, recorder.State);
    }

    [Fact]
    public void CancelDiscardsSessionAndReturnsToStopped()
    {
        var keyboard = new FakeKeyboardRecorder();
        var mouse = new FakeMouseRecorder();
        using var recorder = new UnifiedRecorder(new FakeMonotonicClock(), keyboard, mouse);
        recorder.StartRecording();
        var session = Assert.IsType<RecordingSession>(keyboard.LastSession);
        session.AddKeyboardEvent(InputEventType.KeyboardKeyDown, 65, 30, 0);

        recorder.CancelRecording();

        Assert.Equal(RecordingSessionState.Cancelled, session.State);
        Assert.Equal(UnifiedRecorderState.Stopped, recorder.State);
        Assert.Equal(1, keyboard.StopCount);
        Assert.Equal(1, mouse.StopCount);
        Assert.Throws<InvalidOperationException>(session.Stop);
    }

    [Fact]
    public void CancelFailureStillRunsAllCleanup()
    {
        var keyboard = new FakeKeyboardRecorder
        {
            StopException = new InvalidOperationException("keyboard stop failed"),
        };
        var mouse = new FakeMouseRecorder
        {
            StopException = new InvalidOperationException("mouse stop failed"),
        };
        using var recorder = new UnifiedRecorder(new FakeMonotonicClock(), keyboard, mouse);
        recorder.StartRecording();
        var session = Assert.IsType<RecordingSession>(keyboard.LastSession);

        var error = Assert.Throws<AggregateException>(recorder.CancelRecording);

        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.Equal(RecordingSessionState.Cancelled, session.State);
        Assert.Equal(UnifiedRecorderState.Stopped, recorder.State);
    }

    [Fact]
    public void RestartCreatesIndependentSessionsAndResults()
    {
        var clock = new FakeMonotonicClock(1_000);
        using var keyboardSource = new FakeKeyboardEventSource();
        using var mouseSource = new FakeMouseEventSource();
        using var recorder = CreateRealRecorder(clock, keyboardSource, mouseSource);

        recorder.StartRecording();
        clock.AdvanceMicroseconds(100);
        keyboardSource.Raise(new KeyboardCaptureEvent(KeyboardTransition.KeyDown, 65, 30, 0, false));
        var first = recorder.StopRecording();

        clock.AdvanceMicroseconds(900);
        recorder.StartRecording();
        clock.AdvanceMicroseconds(200);
        keyboardSource.Raise(new KeyboardCaptureEvent(KeyboardTransition.KeyDown, 66, 48, 0, false));
        var second = recorder.StopRecording();

        Assert.Equal(65U, Assert.IsType<KeyboardInputEvent>(Assert.Single(first.Events)).VirtualKey);
        Assert.Equal(66U, Assert.IsType<KeyboardInputEvent>(Assert.Single(second.Events)).VirtualKey);
        Assert.Equal(100, first.DurationUs);
        Assert.Equal(200, second.DurationUs);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void RecordingAfterCancelUsesFreshSessionAndResetMouseSampler()
    {
        var clock = new FakeMonotonicClock();
        using var keyboardSource = new FakeKeyboardEventSource();
        using var mouseSource = new FakeMouseEventSource();
        using var recorder = CreateRealRecorder(clock, keyboardSource, mouseSource, 8_000);
        recorder.StartRecording();
        mouseSource.Raise(new MouseCaptureEvent(MouseCaptureKind.Move, 10, 10, MouseButton.None, 0, false));
        recorder.CancelRecording();

        recorder.StartRecording();
        mouseSource.Raise(new MouseCaptureEvent(MouseCaptureKind.Move, 20, 20, MouseButton.None, 0, false));
        var result = recorder.StopRecording();

        var recorded = Assert.IsType<MouseInputEvent>(Assert.Single(result.Events));
        Assert.Equal((20, 20), (recorded.X, recorded.Y));
    }

    [Fact]
    public void KeyboardAndMouseShareOneOrderedTimelineWithoutSorting()
    {
        var clock = new FakeMonotonicClock(1_000);
        using var keyboardSource = new FakeKeyboardEventSource();
        using var mouseSource = new FakeMouseEventSource();
        using var recorder = CreateRealRecorder(clock, keyboardSource, mouseSource, 0);
        recorder.StartRecording();

        clock.SetMicroseconds(1_100);
        mouseSource.Raise(new MouseCaptureEvent(MouseCaptureKind.Move, -50, 20, MouseButton.None, 0, false));
        clock.SetMicroseconds(1_150);
        keyboardSource.Raise(new KeyboardCaptureEvent(KeyboardTransition.KeyDown, 65, 30, 0, false));
        clock.SetMicroseconds(1_200);
        mouseSource.Raise(new MouseCaptureEvent(MouseCaptureKind.ButtonDown, 10, 20, MouseButton.Left, 0, false));
        keyboardSource.Raise(new KeyboardCaptureEvent(KeyboardTransition.KeyUp, 65, 30, 128, false));
        clock.SetMicroseconds(1_300);
        mouseSource.Raise(new MouseCaptureEvent(MouseCaptureKind.ButtonUp, 10, 20, MouseButton.Left, 0, false));

        var result = recorder.StopRecording();

        Assert.Equal(
            [InputEventType.MouseMove, InputEventType.KeyboardKeyDown, InputEventType.MouseButtonDown,
                InputEventType.KeyboardKeyUp, InputEventType.MouseButtonUp],
            result.Events.Select(input => input.EventType));
        Assert.Equal([100L, 150L, 200L, 200L, 300L], result.Events.Select(input => input.TimestampUs));
        Assert.IsType<MouseInputEvent>(result.Events[0]);
        Assert.IsType<KeyboardInputEvent>(result.Events[1]);
        Assert.IsType<MouseInputEvent>(result.Events[2]);
        Assert.IsType<KeyboardInputEvent>(result.Events[3]);
        Assert.IsType<MouseInputEvent>(result.Events[4]);
    }

    [Fact]
    public void InjectedEventsStayOutOfUnifiedTimeline()
    {
        var clock = new FakeMonotonicClock();
        using var keyboardSource = new FakeKeyboardEventSource();
        using var mouseSource = new FakeMouseEventSource();
        using var recorder = CreateRealRecorder(clock, keyboardSource, mouseSource, 0);
        recorder.StartRecording();

        keyboardSource.Raise(new KeyboardCaptureEvent(KeyboardTransition.KeyDown, 65, 30, 16, true));
        mouseSource.Raise(new MouseCaptureEvent(MouseCaptureKind.Move, 1, 2, MouseButton.None, 0, true));

        Assert.Empty(recorder.StopRecording().Events);
    }

    [Fact]
    public void DisposeWhileRecordingCancelsThenDisposesEveryComponent()
    {
        var operations = new List<string>();
        var keyboard = new FakeKeyboardRecorder(operations);
        var mouse = new FakeMouseRecorder(operations);
        var recorder = new UnifiedRecorder(new FakeMonotonicClock(), keyboard, mouse);
        recorder.StartRecording();
        var session = Assert.IsType<RecordingSession>(keyboard.LastSession);

        recorder.Dispose();
        recorder.Dispose();

        Assert.Equal(
            ["keyboard.start", "mouse.start", "keyboard.stop", "mouse.stop",
                "keyboard.dispose", "mouse.dispose"], operations);
        Assert.Equal(RecordingSessionState.Cancelled, session.State);
        Assert.Equal(UnifiedRecorderState.Disposed, recorder.State);
        Assert.Equal(1, keyboard.DisposeCount);
        Assert.Equal(1, mouse.DisposeCount);
        Assert.Throws<ObjectDisposedException>(recorder.StartRecording);
    }

    [Fact]
    public void PublicStateFollowsStopCancelAndDisposeTransitions()
    {
        var keyboard = new FakeKeyboardRecorder();
        var mouse = new FakeMouseRecorder();
        using var recorder = new UnifiedRecorder(new FakeMonotonicClock(), keyboard, mouse);
        Assert.Equal(UnifiedRecorderState.Stopped, recorder.State);

        recorder.StartRecording();
        Assert.Equal(UnifiedRecorderState.Recording, recorder.State);
        keyboard.OnStop = () => Assert.Equal(UnifiedRecorderState.Stopping, recorder.State);
        recorder.StopRecording();
        Assert.Equal(UnifiedRecorderState.Stopped, recorder.State);

        recorder.StartRecording();
        keyboard.OnStop = () => Assert.Equal(UnifiedRecorderState.Cancelling, recorder.State);
        recorder.CancelRecording();
        Assert.Equal(UnifiedRecorderState.Stopped, recorder.State);

        recorder.Dispose();
        Assert.Equal(UnifiedRecorderState.Disposed, recorder.State);
    }

    [Fact]
    public void DisposedRecorderRejectsEveryLifecycleOperation()
    {
        var recorder = new UnifiedRecorder(
            new FakeMonotonicClock(), new FakeKeyboardRecorder(), new FakeMouseRecorder());
        recorder.Dispose();

        Assert.Throws<ObjectDisposedException>(recorder.StartRecording);
        Assert.Throws<ObjectDisposedException>(() => recorder.StopRecording());
        Assert.Throws<ObjectDisposedException>(recorder.CancelRecording);
    }

    [Fact]
    public void DisposeAggregatesErrorsAfterAttemptingEveryCleanup()
    {
        var keyboard = new FakeKeyboardRecorder
        {
            StopException = new InvalidOperationException("keyboard stop failed"),
            DisposeException = new InvalidOperationException("keyboard dispose failed"),
        };
        var mouse = new FakeMouseRecorder
        {
            StopException = new InvalidOperationException("mouse stop failed"),
            DisposeException = new InvalidOperationException("mouse dispose failed"),
        };
        var recorder = new UnifiedRecorder(new FakeMonotonicClock(), keyboard, mouse);
        recorder.StartRecording();
        var session = Assert.IsType<RecordingSession>(keyboard.LastSession);

        var error = Assert.Throws<AggregateException>(recorder.Dispose);

        Assert.Equal(4, error.InnerExceptions.Count);
        Assert.Equal(RecordingSessionState.Cancelled, session.State);
        Assert.Equal(UnifiedRecorderState.Disposed, recorder.State);
        Assert.True(keyboard.IsDisposed);
        Assert.True(mouse.IsDisposed);
    }

    [Fact]
    public async Task ConcurrentStartsAreSerializedAndOnlyOneCanSucceed()
    {
        using var startEntered = new ManualResetEventSlim();
        using var releaseStart = new ManualResetEventSlim();
        var keyboard = new FakeKeyboardRecorder
        {
            OnStart = () =>
            {
                startEntered.Set();
                if (!releaseStart.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("Test did not release recorder startup.");
                }
            },
        };
        var mouse = new FakeMouseRecorder();
        using var recorder = new UnifiedRecorder(new FakeMonotonicClock(), keyboard, mouse);
        var firstStart = Task.Run(recorder.StartRecording);

        Assert.True(startEntered.Wait(TimeSpan.FromSeconds(10)), "First start did not enter the fake component.");
        var secondStart = Task.Run(recorder.StartRecording);
        Assert.Equal(UnifiedRecorderState.Starting, recorder.State);
        Assert.False(secondStart.IsCompleted);

        releaseStart.Set();
        await firstStart;
        await Assert.ThrowsAsync<InvalidOperationException>(() => secondStart);

        Assert.Equal(1, keyboard.StartCount);
        Assert.Equal(1, mouse.StartCount);
        recorder.CancelRecording();
    }

    [Fact]
    public async Task ConcurrentStopAndCancelLeaveOneConsistentStoppedState()
    {
        using var callersReady = new CountdownEvent(2);
        using var releaseCallers = new ManualResetEventSlim();
        var keyboard = new FakeKeyboardRecorder();
        var mouse = new FakeMouseRecorder();
        using var recorder = new UnifiedRecorder(new FakeMonotonicClock(), keyboard, mouse);
        recorder.StartRecording();

        var stopAttempt = Task.Run(() => AttemptAfterGate(
            callersReady, releaseCallers, () => recorder.StopRecording()));
        var cancelAttempt = Task.Run(() => AttemptAfterGate(
            callersReady, releaseCallers, recorder.CancelRecording));
        Assert.True(callersReady.Wait(TimeSpan.FromSeconds(10)), "Lifecycle callers did not become ready.");
        releaseCallers.Set();

        var errors = await Task.WhenAll(stopAttempt, cancelAttempt);

        Assert.Single(errors, error => error is null);
        Assert.Single(errors, error => error is InvalidOperationException);
        Assert.Equal(1, keyboard.StopCount);
        Assert.Equal(1, mouse.StopCount);
        Assert.Equal(UnifiedRecorderState.Stopped, recorder.State);
    }

    [Fact]
    public void ConstructorRejectsNullDependencies()
    {
        var clock = new FakeMonotonicClock();
        var keyboard = new FakeKeyboardRecorder();
        var mouse = new FakeMouseRecorder();

        Assert.Throws<ArgumentNullException>(() => new UnifiedRecorder(null!, keyboard, mouse));
        Assert.Throws<ArgumentNullException>(() => new UnifiedRecorder(clock, null!, mouse));
        Assert.Throws<ArgumentNullException>(() => new UnifiedRecorder(clock, keyboard, null!));
    }

    private static UnifiedRecorder CreateRealRecorder(
        FakeMonotonicClock clock,
        FakeKeyboardEventSource keyboardSource,
        FakeMouseEventSource mouseSource,
        long sampleIntervalUs = MouseMoveSampler.DefaultIntervalUs) =>
        new(
            clock,
            new KeyboardRecorder(keyboardSource),
            new MouseRecorder(mouseSource, new MouseMoveSampler(clock, sampleIntervalUs)));

    private static Exception? AttemptAfterGate(
        CountdownEvent callersReady,
        ManualResetEventSlim releaseCallers,
        Action operation)
    {
        callersReady.Signal();
        if (!releaseCallers.Wait(TimeSpan.FromSeconds(10)))
        {
            return new TimeoutException("Test did not release lifecycle callers.");
        }

        return Record.Exception(operation);
    }
}
