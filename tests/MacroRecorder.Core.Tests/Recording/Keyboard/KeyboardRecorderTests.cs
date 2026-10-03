using MacroRecorder.Core.Models.Input;
using MacroRecorder.Core.Recording;
using MacroRecorder.Core.Recording.Keyboard;
using MacroRecorder.Core.Tests.TestDoubles;

namespace MacroRecorder.Core.Tests.Recording.Keyboard;

public sealed class KeyboardRecorderTests
{
    [Fact]
    public void StartsAndStopsWithoutStoppingTheRecordingSession()
    {
        using var source = new FakeKeyboardEventSource();
        using var recorder = new KeyboardRecorder(source);
        var session = new RecordingSession(new FakeMonotonicClock());

        recorder.Start(session);

        Assert.Equal(KeyboardRecorderState.Running, recorder.State);
        Assert.True(source.IsRunning);

        recorder.Stop();

        Assert.Equal(KeyboardRecorderState.Stopped, recorder.State);
        Assert.False(source.IsRunning);
        Assert.Equal(RecordingSessionState.Recording, session.State);
        Assert.Empty(session.Stop().Events);
    }

    [Theory]
    [InlineData(KeyboardTransition.KeyDown, InputEventType.KeyboardKeyDown)]
    [InlineData(KeyboardTransition.KeyUp, InputEventType.KeyboardKeyUp)]
    public void NormalTransitionIsForwardedWithSessionTimestampAndRawFields(
        KeyboardTransition transition,
        InputEventType expectedType)
    {
        using var source = new FakeKeyboardEventSource();
        using var recorder = new KeyboardRecorder(source);
        var clock = new FakeMonotonicClock(1_000_000);
        var session = new RecordingSession(clock);
        recorder.Start(session);
        clock.AdvanceMicroseconds(250);

        source.Raise(new KeyboardCaptureEvent(transition, 65, 30, 0xA1U, false));
        recorder.Stop();
        var result = session.Stop();

        var recorded = Assert.IsType<KeyboardInputEvent>(Assert.Single(result.Events));
        Assert.Equal(250, recorded.TimestampUs);
        Assert.Equal(expectedType, recorded.EventType);
        Assert.Equal(65U, recorded.VirtualKey);
        Assert.Equal(30U, recorded.ScanCode);
        Assert.Equal(0xA1U, recorded.Flags);
    }

    [Fact]
    public void InjectedTransitionsAreIgnored()
    {
        using var source = new FakeKeyboardEventSource();
        using var recorder = new KeyboardRecorder(source);
        var session = new RecordingSession(new FakeMonotonicClock());
        recorder.Start(session);

        source.Raise(new KeyboardCaptureEvent(KeyboardTransition.KeyDown, 65, 30, 0x10, true));
        source.Raise(new KeyboardCaptureEvent(KeyboardTransition.KeyUp, 65, 30, 0x02, true));

        recorder.Stop();
        Assert.Empty(session.Stop().Events);
    }

    [Fact]
    public void ModifierSequencePreservesEveryTransitionAndOrder()
    {
        using var source = new FakeKeyboardEventSource();
        using var recorder = new KeyboardRecorder(source);
        var clock = new FakeMonotonicClock();
        var session = new RecordingSession(clock);
        recorder.Start(session);

        Raise(source, clock, KeyboardTransition.KeyDown, 160, 42);
        Raise(source, clock, KeyboardTransition.KeyDown, 65, 30);
        Raise(source, clock, KeyboardTransition.KeyUp, 65, 30);
        Raise(source, clock, KeyboardTransition.KeyUp, 160, 42);

        recorder.Stop();
        var events = session.Stop().Events;
        Assert.Equal([160U, 65U, 65U, 160U], events.Cast<KeyboardInputEvent>().Select(input => input.VirtualKey));
        Assert.Equal(
            [InputEventType.KeyboardKeyDown, InputEventType.KeyboardKeyDown,
                InputEventType.KeyboardKeyUp, InputEventType.KeyboardKeyUp],
            events.Select(input => input.EventType));
        Assert.Equal([10L, 20L, 30L, 40L], events.Select(input => input.TimestampUs));
    }

    [Fact]
    public void RepeatedKeyDownEventsAreNotDeduplicated()
    {
        using var source = new FakeKeyboardEventSource();
        using var recorder = new KeyboardRecorder(source);
        var clock = new FakeMonotonicClock();
        var session = new RecordingSession(clock);
        recorder.Start(session);

        Raise(source, clock, KeyboardTransition.KeyDown, 66, 48);
        Raise(source, clock, KeyboardTransition.KeyDown, 66, 48);
        Raise(source, clock, KeyboardTransition.KeyDown, 66, 48);
        Raise(source, clock, KeyboardTransition.KeyUp, 66, 48);

        recorder.Stop();
        var events = session.Stop().Events;
        Assert.Equal(4, events.Length);
        Assert.Equal(3, events.Count(input => input.EventType == InputEventType.KeyboardKeyDown));
        Assert.Equal(InputEventType.KeyboardKeyUp, events[^1].EventType);
    }

    [Fact]
    public void StopPreventsLaterSourceEvents()
    {
        using var source = new FakeKeyboardEventSource();
        using var recorder = new KeyboardRecorder(source);
        var session = new RecordingSession(new FakeMonotonicClock());
        recorder.Start(session);
        source.Raise(new KeyboardCaptureEvent(KeyboardTransition.KeyDown, 65, 30, 0, false));

        recorder.Stop();
        source.Raise(new KeyboardCaptureEvent(KeyboardTransition.KeyUp, 65, 30, 0, false));

        Assert.Single(session.Stop().Events);
    }

    [Fact]
    public void StartTwiceIsRejectedAndStopIsIdempotent()
    {
        using var source = new FakeKeyboardEventSource();
        using var recorder = new KeyboardRecorder(source);
        var session = new RecordingSession(new FakeMonotonicClock());
        recorder.Start(session);

        Assert.Throws<InvalidOperationException>(() => recorder.Start(session));
        recorder.Stop();
        recorder.Stop();

        Assert.Equal(1, source.StartCount);
        Assert.Equal(1, source.StopCount);
    }

    [Fact]
    public void StartFailureRollsBackSubscriptionAndState()
    {
        using var source = new FakeKeyboardEventSource { StartException = new InvalidOperationException("start failed") };
        using var recorder = new KeyboardRecorder(source);
        var session = new RecordingSession(new FakeMonotonicClock());

        var exception = Assert.Throws<InvalidOperationException>(() => recorder.Start(session));

        Assert.Equal("start failed", exception.Message);
        Assert.Equal(KeyboardRecorderState.Stopped, recorder.State);
        source.Raise(new KeyboardCaptureEvent(KeyboardTransition.KeyDown, 65, 30, 0, false));
        Assert.Empty(session.Stop().Events);
    }

    [Fact]
    public void RestartUsesANewSessionWithoutCrossContamination()
    {
        using var source = new FakeKeyboardEventSource();
        using var recorder = new KeyboardRecorder(source);
        var sessionA = new RecordingSession(new FakeMonotonicClock());
        recorder.Start(sessionA);
        source.Raise(new KeyboardCaptureEvent(KeyboardTransition.KeyDown, 65, 30, 0, false));
        recorder.Stop();

        var sessionB = new RecordingSession(new FakeMonotonicClock());
        recorder.Start(sessionB);
        source.Raise(new KeyboardCaptureEvent(KeyboardTransition.KeyDown, 66, 48, 0, false));
        recorder.Stop();

        Assert.Equal(65U, Assert.IsType<KeyboardInputEvent>(Assert.Single(sessionA.Stop().Events)).VirtualKey);
        Assert.Equal(66U, Assert.IsType<KeyboardInputEvent>(Assert.Single(sessionB.Stop().Events)).VirtualKey);
        Assert.Equal(2, source.StartCount);
        Assert.Equal(2, source.StopCount);
    }

    [Fact]
    public void DisposeStopsCaptureIsIdempotentAndPreventsRestart()
    {
        using var source = new FakeKeyboardEventSource();
        var recorder = new KeyboardRecorder(source);
        var session = new RecordingSession(new FakeMonotonicClock());
        recorder.Start(session);

        recorder.Dispose();
        recorder.Dispose();

        Assert.Equal(KeyboardRecorderState.Disposed, recorder.State);
        Assert.True(source.IsDisposed);
        Assert.False(source.IsRunning);
        Assert.Throws<ObjectDisposedException>(() => recorder.Start(session));
        Assert.Equal(RecordingSessionState.Recording, session.State);
    }

    [Fact]
    public void StartingWithAFinishedSessionIsRejected()
    {
        using var source = new FakeKeyboardEventSource();
        using var recorder = new KeyboardRecorder(source);
        var session = new RecordingSession(new FakeMonotonicClock());
        session.Stop();

        Assert.Throws<ArgumentException>(() => recorder.Start(session));
        Assert.Equal(KeyboardRecorderState.Stopped, recorder.State);
        Assert.False(source.IsRunning);
    }

    [Fact]
    public async Task StopWaitsForAnInFlightCallbackBeforeReturning()
    {
        using var source = new FakeKeyboardEventSource();
        using var recorder = new KeyboardRecorder(source);
        using var clock = new BlockingMonotonicClock(1_000);
        var session = new RecordingSession(clock);
        recorder.Start(session);
        clock.SetMicroseconds(2_000);
        clock.BlockNextRead();

        var callbackTask = Task.Run(() => source.Raise(
            new KeyboardCaptureEvent(KeyboardTransition.KeyDown, 65, 30, 0, false)));
        Assert.True(clock.WaitUntilReadEntered(TimeSpan.FromSeconds(10)), "Callback did not enter the clock read.");
        var stopTask = Task.Run(recorder.Stop);

        clock.ReleaseRead();
        await callbackTask;
        await stopTask;

        Assert.Single(session.Stop().Events);
        Assert.Equal(KeyboardRecorderState.Stopped, recorder.State);
    }

    private static void Raise(
        FakeKeyboardEventSource source,
        FakeMonotonicClock clock,
        KeyboardTransition transition,
        uint virtualKey,
        uint scanCode)
    {
        clock.AdvanceMicroseconds(10);
        source.Raise(new KeyboardCaptureEvent(transition, virtualKey, scanCode, 0, false));
    }
}
