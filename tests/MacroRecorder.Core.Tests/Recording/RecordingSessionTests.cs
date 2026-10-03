using System.Collections.Immutable;
using MacroRecorder.Core.Models.Input;
using MacroRecorder.Core.Recording;
using MacroRecorder.Core.Tests.TestDoubles;

namespace MacroRecorder.Core.Tests.Recording;

public sealed class RecordingSessionTests
{
    [Fact]
    public void ConstructionStartsRecordingImmediately()
    {
        var session = new RecordingSession(new FakeMonotonicClock(1_000_000));

        Assert.Equal(RecordingSessionState.Recording, session.State);
    }

    [Fact]
    public void EmptySessionUsesStopTimeForDuration()
    {
        var clock = new FakeMonotonicClock(1_000_000);
        var session = new RecordingSession(clock);
        clock.AdvanceMicroseconds(5_000);

        var result = session.Stop();

        Assert.Equal(RecordingSessionState.Stopped, session.State);
        Assert.Equal(5_000, result.DurationUs);
        Assert.Equal(0, result.EventCount);
        Assert.Empty(result.Events);
        Assert.False(result.Events.IsDefault);
    }

    [Theory]
    [InlineData(InputEventType.KeyboardKeyDown)]
    [InlineData(InputEventType.KeyboardKeyUp)]
    public void KeyboardEventReceivesRelativeTimestampAndPreservesRawFields(InputEventType eventType)
    {
        var clock = new FakeMonotonicClock(1_000_000);
        var session = new RecordingSession(clock);
        clock.AdvanceMicroseconds(250);

        var added = session.AddKeyboardEvent(eventType, 65, 30, uint.MaxValue);
        var result = session.Stop();

        var recorded = Assert.IsType<KeyboardInputEvent>(Assert.Single(result.Events));
        Assert.Same(added, recorded);
        Assert.Equal(250, recorded.TimestampUs);
        Assert.Equal(eventType, recorded.EventType);
        Assert.Equal(65U, recorded.VirtualKey);
        Assert.Equal(30U, recorded.ScanCode);
        Assert.Equal(uint.MaxValue, recorded.Flags);
    }

    [Theory]
    [InlineData(InputEventType.MouseMove, MouseButton.None, 0)]
    [InlineData(InputEventType.MouseButtonDown, MouseButton.Left, 0)]
    [InlineData(InputEventType.MouseButtonUp, MouseButton.XButton2, 0)]
    [InlineData(InputEventType.MouseVerticalWheel, MouseButton.None, 15)]
    [InlineData(InputEventType.MouseHorizontalWheel, MouseButton.None, -30)]
    public void MouseEventReceivesRelativeTimestampAndPreservesFields(
        InputEventType eventType,
        MouseButton button,
        int wheelDelta)
    {
        var clock = new FakeMonotonicClock(800_000);
        var session = new RecordingSession(clock);
        clock.AdvanceMicroseconds(125);

        var added = session.AddMouseEvent(eventType, -500, 200, button, wheelDelta);
        var result = session.Stop();

        var recorded = Assert.IsType<MouseInputEvent>(Assert.Single(result.Events));
        Assert.Same(added, recorded);
        Assert.Equal(125, recorded.TimestampUs);
        Assert.Equal(eventType, recorded.EventType);
        Assert.Equal(-500, recorded.X);
        Assert.Equal(200, recorded.Y);
        Assert.Equal(button, recorded.Button);
        Assert.Equal(wheelDelta, recorded.WheelDelta);
    }

    [Fact]
    public void MultipleEventsPreserveAddOrderAndAllowEqualTimestamps()
    {
        var clock = new FakeMonotonicClock(10_000);
        var session = new RecordingSession(clock);

        clock.AdvanceMicroseconds(100);
        var keyDown = session.AddKeyboardEvent(InputEventType.KeyboardKeyDown, 65, 30, 0);
        clock.AdvanceMicroseconds(50);
        var move = session.AddMouseEvent(InputEventType.MouseMove, 10, 20, MouseButton.None, 0);
        var mouseDown = session.AddMouseEvent(InputEventType.MouseButtonDown, 10, 20, MouseButton.Left, 0);
        clock.AdvanceMicroseconds(350);
        var keyUp = session.AddKeyboardEvent(InputEventType.KeyboardKeyUp, 65, 30, 128);

        var result = session.Stop();

        Assert.Collection(result.Events,
            input => Assert.Same(keyDown, input),
            input => Assert.Same(move, input),
            input => Assert.Same(mouseDown, input),
            input => Assert.Same(keyUp, input));
        Assert.Equal([100L, 150L, 150L, 500L], result.Events.Select(input => input.TimestampUs));
    }

    [Fact]
    public void ClockRegressionClampsEventsAndDurationToANonDecreasingTimeline()
    {
        var clock = new FakeMonotonicClock(1_000);
        var session = new RecordingSession(clock);
        clock.SetMicroseconds(1_200);
        session.AddKeyboardEvent(InputEventType.KeyboardKeyDown, 65, 30, 0);
        clock.SetMicroseconds(1_100);
        session.AddKeyboardEvent(InputEventType.KeyboardKeyUp, 65, 30, 128);
        clock.SetMicroseconds(long.MinValue);

        var result = session.Stop();

        Assert.Equal([200L, 200L], result.Events.Select(input => input.TimestampUs));
        Assert.Equal(200, result.DurationUs);
    }

    [Fact]
    public void StopDurationIncludesIdleTimeAfterTheLastEvent()
    {
        var clock = new FakeMonotonicClock();
        var session = new RecordingSession(clock);
        clock.SetMicroseconds(1_000_000);
        session.AddMouseEvent(InputEventType.MouseButtonDown, 1, 2, MouseButton.Left, 0);
        clock.SetMicroseconds(5_000_000);

        var result = session.Stop();

        Assert.Equal(1_000_000, result.Events[0].TimestampUs);
        Assert.Equal(5_000_000, result.DurationUs);
    }

    [Fact]
    public void ResultEventsAreFrozenAndCannotBeChangedThroughAListInterface()
    {
        var session = new RecordingSession(new FakeMonotonicClock());
        var added = session.AddKeyboardEvent(InputEventType.KeyboardKeyDown, 65, 30, 0);

        var result = session.Stop();
        IList<InputEvent> mutableView = result.Events;

        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView.Clear());
        Assert.Same(added, Assert.Single(result.Events));
    }

    [Fact]
    public void StopRejectsAllFurtherMutationAndASecondStop()
    {
        var session = new RecordingSession(new FakeMonotonicClock());
        session.Stop();

        Assert.Throws<InvalidOperationException>(() =>
            session.AddKeyboardEvent(InputEventType.KeyboardKeyDown, 65, 30, 0));
        Assert.Throws<InvalidOperationException>(() =>
            session.AddMouseEvent(InputEventType.MouseMove, 0, 0, MouseButton.None, 0));
        Assert.Throws<InvalidOperationException>(session.Stop);
        Assert.Throws<InvalidOperationException>(session.Cancel);
        Assert.Equal(RecordingSessionState.Stopped, session.State);
    }

    [Fact]
    public void CancelDiscardsEventsAndRejectsAllFurtherOperations()
    {
        var session = new RecordingSession(new FakeMonotonicClock());
        session.AddKeyboardEvent(InputEventType.KeyboardKeyDown, 65, 30, 0);

        session.Cancel();

        Assert.Equal(RecordingSessionState.Cancelled, session.State);
        Assert.Throws<InvalidOperationException>(() =>
            session.AddKeyboardEvent(InputEventType.KeyboardKeyUp, 65, 30, 128));
        Assert.Throws<InvalidOperationException>(() =>
            session.AddMouseEvent(InputEventType.MouseMove, 0, 0, MouseButton.None, 0));
        Assert.Throws<InvalidOperationException>(session.Stop);
        Assert.Throws<InvalidOperationException>(session.Cancel);
    }

    [Fact]
    public void EightHourSessionKeepsExactMicroseconds()
    {
        const long eightHoursUs = 8L * 60 * 60 * 1_000_000;
        var clock = new FakeMonotonicClock(5_000_000_000);
        var session = new RecordingSession(clock);
        clock.AdvanceMicroseconds(eightHoursUs);
        session.AddKeyboardEvent(InputEventType.KeyboardKeyDown, 65, 30, 0);

        var result = session.Stop();

        Assert.Equal(eightHoursUs, result.Events[0].TimestampUs);
        Assert.Equal(eightHoursUs, result.DurationUs);
    }

    [Fact]
    public void LargeClockOriginIsSubtractedWithoutLosingPrecision()
    {
        var clock = new FakeMonotonicClock(long.MaxValue - 1_000);
        var session = new RecordingSession(clock);
        clock.SetMicroseconds(long.MaxValue);

        var added = session.AddKeyboardEvent(InputEventType.KeyboardKeyDown, 65, 30, 0);

        Assert.Equal(1_000, added.TimestampUs);
        Assert.Equal(1_000, session.Stop().DurationUs);
    }

    [Fact]
    public void ExtremeClockDeltaSaturatesInsteadOfOverflowing()
    {
        var clock = new FakeMonotonicClock(long.MinValue);
        var session = new RecordingSession(clock);
        clock.SetMicroseconds(long.MaxValue);

        var added = session.AddKeyboardEvent(InputEventType.KeyboardKeyDown, 65, 30, 0);

        Assert.Equal(long.MaxValue, added.TimestampUs);
        Assert.Equal(long.MaxValue, session.Stop().DurationUs);
    }

    [Fact]
    public void RecordingResultRejectsInvalidConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RecordingResult(-1, ImmutableArray<InputEvent>.Empty));
        Assert.Throws<ArgumentException>(() =>
            new RecordingResult(0, default));
    }
}
