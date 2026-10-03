using MacroRecorder.Core.Models.Input;
using MacroRecorder.Core.Recording;
using MacroRecorder.Core.Tests.TestDoubles;

namespace MacroRecorder.Core.Tests.Recording;

public sealed class RecordingSessionConcurrencyTests
{
    [Fact]
    public async Task ConcurrentAddsKeepEveryEventAndANonDecreasingTimeline()
    {
        const int eventCount = 256;
        var clock = new FakeMonotonicClock(10_000);
        var session = new RecordingSession(clock);
        clock.SetMicroseconds(11_000);

        var tasks = Enumerable.Range(0, eventCount)
            .Select(index => Task.Run(() => session.AddKeyboardEvent(
                InputEventType.KeyboardKeyDown, (uint)index, (uint)(index + 1), (uint)(index + 2))))
            .ToArray();

        await Task.WhenAll(tasks);
        clock.SetMicroseconds(12_000);
        var result = session.Stop();

        Assert.Equal(eventCount, result.EventCount);
        Assert.Equal(eventCount, result.Events.Select(input =>
            Assert.IsType<KeyboardInputEvent>(input).VirtualKey).Distinct().Count());
        Assert.All(result.Events, input => Assert.Equal(1_000, input.TimestampUs));
        Assert.True(result.Events.Zip(result.Events.Skip(1),
            (left, right) => left.TimestampUs <= right.TimestampUs).All(value => value));
        Assert.Equal(2_000, result.DurationUs);
    }

    [Fact]
    public async Task StopOwnsTheBoundaryAndAWaitingAddIsRejectedWithoutPartialData()
    {
        using var clock = new BlockingMonotonicClock(1_000);
        var session = new RecordingSession(clock);
        clock.SetMicroseconds(2_000);
        clock.BlockNextRead();

        var stopTask = Task.Run(session.Stop);
        Assert.True(clock.WaitUntilReadEntered(TimeSpan.FromSeconds(10)), "Stop did not enter the clock read.");

        var addTask = Task.Run(() =>
            session.AddKeyboardEvent(InputEventType.KeyboardKeyDown, 65, 30, 0));

        clock.ReleaseRead();
        var result = await stopTask;
        await Assert.ThrowsAsync<InvalidOperationException>(() => addTask);

        Assert.Equal(RecordingSessionState.Stopped, session.State);
        Assert.Equal(1_000, result.DurationUs);
        Assert.Empty(result.Events);
    }
}
