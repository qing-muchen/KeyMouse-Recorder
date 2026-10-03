using MacroRecorder.Core.Models.Input;
using MacroRecorder.Core.Recording;
using MacroRecorder.Core.Recording.Mouse;
using MacroRecorder.Core.Tests.TestDoubles;

namespace MacroRecorder.Core.Tests.Recording.Mouse;

public sealed class MouseRecorderTests
{
    [Fact]
    public void StartsAndStopsWithoutStoppingTheRecordingSession()
    {
        using var source = new FakeMouseEventSource();
        using var recorder = CreateRecorder(source, new FakeMonotonicClock());
        var session = new RecordingSession(new FakeMonotonicClock());

        recorder.Start(session);
        Assert.Equal(MouseRecorderState.Running, recorder.State);
        Assert.True(source.IsRunning);

        recorder.Stop();

        Assert.Equal(MouseRecorderState.Stopped, recorder.State);
        Assert.False(source.IsRunning);
        Assert.Equal(RecordingSessionState.Recording, session.State);
        Assert.Empty(session.Stop().Events);
    }

    [Theory]
    [InlineData(MouseCaptureKind.Move, MouseButton.None, 0, InputEventType.MouseMove)]
    [InlineData(MouseCaptureKind.ButtonDown, MouseButton.Left, 0, InputEventType.MouseButtonDown)]
    [InlineData(MouseCaptureKind.ButtonUp, MouseButton.Right, 0, InputEventType.MouseButtonUp)]
    [InlineData(MouseCaptureKind.VerticalWheel, MouseButton.None, -60, InputEventType.MouseVerticalWheel)]
    [InlineData(MouseCaptureKind.HorizontalWheel, MouseButton.None, 120, InputEventType.MouseHorizontalWheel)]
    public void MapsCaptureKindsAndPreservesCoordinates(
        MouseCaptureKind kind,
        MouseButton button,
        int wheelDelta,
        InputEventType expectedType)
    {
        using var source = new FakeMouseEventSource();
        var clock = new FakeMonotonicClock(1_000_000);
        using var recorder = CreateRecorder(source, clock, 0);
        var session = new RecordingSession(clock);
        recorder.Start(session);
        clock.AdvanceMicroseconds(250);

        source.Raise(new MouseCaptureEvent(kind, -500, 200, button, wheelDelta, false));
        recorder.Stop();
        var recorded = Assert.IsType<MouseInputEvent>(Assert.Single(session.Stop().Events));

        Assert.Equal(250, recorded.TimestampUs);
        Assert.Equal(expectedType, recorded.EventType);
        Assert.Equal(-500, recorded.X);
        Assert.Equal(200, recorded.Y);
        Assert.Equal(button, recorded.Button);
        Assert.Equal(wheelDelta, recorded.WheelDelta);
    }

    [Theory]
    [InlineData(MouseButton.Left)]
    [InlineData(MouseButton.Right)]
    [InlineData(MouseButton.Middle)]
    [InlineData(MouseButton.XButton1)]
    [InlineData(MouseButton.XButton2)]
    public void PreservesEveryButtonDownAndUp(MouseButton button)
    {
        using var source = new FakeMouseEventSource();
        using var recorder = CreateRecorder(source, new FakeMonotonicClock());
        var session = new RecordingSession(new FakeMonotonicClock());
        recorder.Start(session);

        source.Raise(new MouseCaptureEvent(MouseCaptureKind.ButtonDown, 10, 20, button, 0, false));
        source.Raise(new MouseCaptureEvent(MouseCaptureKind.ButtonUp, 30, 40, button, 0, false));
        recorder.Stop();
        var events = session.Stop().Events.Cast<MouseInputEvent>().ToArray();

        Assert.Equal(2, events.Length);
        Assert.Equal(InputEventType.MouseButtonDown, events[0].EventType);
        Assert.Equal(InputEventType.MouseButtonUp, events[1].EventType);
        Assert.Equal(button, events[0].Button);
        Assert.Equal(button, events[1].Button);
        Assert.Equal((10, 20), (events[0].X, events[0].Y));
        Assert.Equal((30, 40), (events[1].X, events[1].Y));
    }

    [Fact]
    public void InjectedEventsAreIgnoredWithoutAdvancingSampler()
    {
        using var source = new FakeMouseEventSource();
        var clock = new FakeMonotonicClock();
        using var recorder = CreateRecorder(source, clock, 8_000);
        var session = new RecordingSession(clock);
        recorder.Start(session);

        source.Raise(new MouseCaptureEvent(MouseCaptureKind.Move, 1, 1, MouseButton.None, 0, true));
        source.Raise(new MouseCaptureEvent(MouseCaptureKind.ButtonDown, 1, 1, MouseButton.Left, 0, true));
        source.Raise(new MouseCaptureEvent(MouseCaptureKind.Move, 2, 2, MouseButton.None, 0, false));

        recorder.Stop();
        var recorded = Assert.IsType<MouseInputEvent>(Assert.Single(session.Stop().Events));
        Assert.Equal((2, 2), (recorded.X, recorded.Y));
    }

    [Fact]
    public void MouseMoveSamplingKeepsFirstIntervalAndLaterMoves()
    {
        using var source = new FakeMouseEventSource();
        var clock = new FakeMonotonicClock(1_000_000);
        using var recorder = CreateRecorder(source, clock, 8_000);
        var session = new RecordingSession(clock);
        recorder.Start(session);

        RaiseMove(source, clock, 1_000_000, 0);
        RaiseMove(source, clock, 1_002_000, 1);
        RaiseMove(source, clock, 1_007_999, 2);
        RaiseMove(source, clock, 1_008_000, 3);
        RaiseMove(source, clock, 1_009_000, 4);
        RaiseMove(source, clock, 1_016_000, 5);

        recorder.Stop();
        var events = session.Stop().Events.Cast<MouseInputEvent>().ToArray();
        Assert.Equal(3, events.Length);
        Assert.Equal([0, 3, 5], events.Select(input => input.X));
    }

    [Fact]
    public void EqualCoordinatesAreRecordedWhenSamplingIntervalHasElapsed()
    {
        using var source = new FakeMouseEventSource();
        var clock = new FakeMonotonicClock();
        using var recorder = CreateRecorder(source, clock, 8_000);
        var session = new RecordingSession(clock);
        recorder.Start(session);

        source.Raise(new MouseCaptureEvent(MouseCaptureKind.Move, 100, 100, MouseButton.None, 0, false));
        clock.AdvanceMicroseconds(8_000);
        source.Raise(new MouseCaptureEvent(MouseCaptureKind.Move, 100, 100, MouseButton.None, 0, false));

        recorder.Stop();
        var events = session.Stop().Events.Cast<MouseInputEvent>().ToArray();
        Assert.Equal(2, events.Length);
        Assert.All(events, input => Assert.Equal((100, 100), (input.X, input.Y)));
    }

    [Fact]
    public void ButtonAndWheelBypassMoveSampler()
    {
        using var source = new FakeMouseEventSource();
        var clock = new FakeMonotonicClock();
        using var recorder = CreateRecorder(source, clock, 8_000);
        var session = new RecordingSession(clock);
        recorder.Start(session);

        source.Raise(new MouseCaptureEvent(MouseCaptureKind.Move, 0, 0, MouseButton.None, 0, false));
        source.Raise(new MouseCaptureEvent(MouseCaptureKind.Move, 1, 1, MouseButton.None, 0, false));
        source.Raise(new MouseCaptureEvent(MouseCaptureKind.ButtonDown, 2, 2, MouseButton.Left, 0, false));
        source.Raise(new MouseCaptureEvent(MouseCaptureKind.ButtonUp, 3, 3, MouseButton.Left, 0, false));
        source.Raise(new MouseCaptureEvent(MouseCaptureKind.VerticalWheel, 4, 4, MouseButton.None, -120, false));
        source.Raise(new MouseCaptureEvent(MouseCaptureKind.HorizontalWheel, 5, 5, MouseButton.None, 60, false));

        recorder.Stop();
        var events = session.Stop().Events.Cast<MouseInputEvent>().ToArray();
        Assert.Equal(5, events.Length);
        Assert.Equal(
            [InputEventType.MouseMove, InputEventType.MouseButtonDown, InputEventType.MouseButtonUp,
                InputEventType.MouseVerticalWheel, InputEventType.MouseHorizontalWheel],
            events.Select(input => input.EventType));
    }

    [Fact]
    public void DragPreservesDownSampledMovesAndUp()
    {
        using var source = new FakeMouseEventSource();
        var clock = new FakeMonotonicClock();
        using var recorder = CreateRecorder(source, clock, 8_000);
        var session = new RecordingSession(clock);
        recorder.Start(session);

        source.Raise(new MouseCaptureEvent(MouseCaptureKind.ButtonDown, 100, 100, MouseButton.Left, 0, false));
        RaiseMove(source, clock, 0, 110);
        RaiseMove(source, clock, 2_000, 120);
        RaiseMove(source, clock, 8_000, 200);
        RaiseMove(source, clock, 16_000, 290);
        source.Raise(new MouseCaptureEvent(MouseCaptureKind.ButtonUp, 300, 300, MouseButton.Left, 0, false));

        recorder.Stop();
        var events = session.Stop().Events.Cast<MouseInputEvent>().ToArray();
        Assert.Equal(5, events.Length);
        Assert.Equal(InputEventType.MouseButtonDown, events[0].EventType);
        Assert.All(events[1..^1], input => Assert.Equal(InputEventType.MouseMove, input.EventType));
        Assert.Equal(InputEventType.MouseButtonUp, events[^1].EventType);
        Assert.Equal((300, 300), (events[^1].X, events[^1].Y));
    }

    [Fact]
    public void StopPreventsLaterSourceEvents()
    {
        using var source = new FakeMouseEventSource();
        using var recorder = CreateRecorder(source, new FakeMonotonicClock());
        var session = new RecordingSession(new FakeMonotonicClock());
        recorder.Start(session);
        source.Raise(new MouseCaptureEvent(MouseCaptureKind.ButtonDown, 1, 2, MouseButton.Left, 0, false));

        recorder.Stop();
        source.Raise(new MouseCaptureEvent(MouseCaptureKind.ButtonUp, 1, 2, MouseButton.Left, 0, false));

        Assert.Single(session.Stop().Events);
    }

    [Fact]
    public void RestartUsesNewSessionAndResetsSampler()
    {
        using var source = new FakeMouseEventSource();
        var clock = new FakeMonotonicClock();
        using var recorder = CreateRecorder(source, clock, 8_000);
        var sessionA = new RecordingSession(clock);
        recorder.Start(sessionA);
        source.Raise(new MouseCaptureEvent(MouseCaptureKind.Move, 10, 10, MouseButton.None, 0, false));
        recorder.Stop();

        var sessionB = new RecordingSession(clock);
        recorder.Start(sessionB);
        source.Raise(new MouseCaptureEvent(MouseCaptureKind.Move, 20, 20, MouseButton.None, 0, false));
        recorder.Stop();

        Assert.Equal(10, Assert.IsType<MouseInputEvent>(Assert.Single(sessionA.Stop().Events)).X);
        Assert.Equal(20, Assert.IsType<MouseInputEvent>(Assert.Single(sessionB.Stop().Events)).X);
        Assert.Equal(2, source.StartCount);
        Assert.Equal(2, source.StopCount);
    }

    [Fact]
    public void StartTwiceIsRejectedAndStopIsIdempotent()
    {
        using var source = new FakeMouseEventSource();
        using var recorder = CreateRecorder(source, new FakeMonotonicClock());
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
        using var source = new FakeMouseEventSource { StartException = new InvalidOperationException("start failed") };
        using var recorder = CreateRecorder(source, new FakeMonotonicClock());
        var session = new RecordingSession(new FakeMonotonicClock());

        Assert.Throws<InvalidOperationException>(() => recorder.Start(session));

        Assert.Equal(MouseRecorderState.Stopped, recorder.State);
        source.Raise(new MouseCaptureEvent(MouseCaptureKind.Move, 1, 1, MouseButton.None, 0, false));
        Assert.Empty(session.Stop().Events);
    }

    [Fact]
    public void DisposeStopsCaptureIsIdempotentAndPreventsRestart()
    {
        using var source = new FakeMouseEventSource();
        var recorder = CreateRecorder(source, new FakeMonotonicClock());
        var session = new RecordingSession(new FakeMonotonicClock());
        recorder.Start(session);

        recorder.Dispose();
        recorder.Dispose();

        Assert.Equal(MouseRecorderState.Disposed, recorder.State);
        Assert.True(source.IsDisposed);
        Assert.False(source.IsRunning);
        Assert.Throws<ObjectDisposedException>(() => recorder.Start(session));
        Assert.Equal(RecordingSessionState.Recording, session.State);
    }

    [Fact]
    public void StartingWithFinishedSessionIsRejected()
    {
        using var source = new FakeMouseEventSource();
        using var recorder = CreateRecorder(source, new FakeMonotonicClock());
        var session = new RecordingSession(new FakeMonotonicClock());
        session.Stop();

        Assert.Throws<ArgumentException>(() => recorder.Start(session));
        Assert.Equal(MouseRecorderState.Stopped, recorder.State);
        Assert.False(source.IsRunning);
    }

    [Fact]
    public async Task StopWaitsForInFlightCallbackBeforeReturning()
    {
        using var source = new FakeMouseEventSource();
        using var clock = new BlockingMonotonicClock(1_000);
        using var recorder = CreateRecorder(source, clock);
        var session = new RecordingSession(clock);
        recorder.Start(session);
        clock.SetMicroseconds(2_000);
        clock.BlockNextRead();

        var callbackTask = Task.Run(() => source.Raise(
            new MouseCaptureEvent(MouseCaptureKind.Move, 10, 20, MouseButton.None, 0, false)));
        Assert.True(clock.WaitUntilReadEntered(TimeSpan.FromSeconds(10)), "Callback did not enter the sampler clock read.");
        var stopTask = Task.Run(recorder.Stop);

        clock.ReleaseRead();
        await callbackTask;
        await stopTask;

        Assert.Single(session.Stop().Events);
        Assert.Equal(MouseRecorderState.Stopped, recorder.State);
    }

    private static MouseRecorder CreateRecorder(
        FakeMouseEventSource source,
        MacroRecorder.Core.Time.IMonotonicClock clock,
        long intervalUs = MouseMoveSampler.DefaultIntervalUs) =>
        new(source, new MouseMoveSampler(clock, intervalUs));

    private static void RaiseMove(FakeMouseEventSource source, FakeMonotonicClock clock, long timestampUs, int x)
    {
        clock.SetMicroseconds(timestampUs);
        source.Raise(new MouseCaptureEvent(MouseCaptureKind.Move, x, x, MouseButton.None, 0, false));
    }
}
