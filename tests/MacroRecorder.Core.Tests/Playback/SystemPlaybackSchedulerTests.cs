using MacroRecorder.Infrastructure.Playback;

namespace MacroRecorder.Core.Tests.Playback;

public sealed class SystemPlaybackSchedulerTests
{
    [Fact]
    public void ConstructorRejectsNullClock()
    {
        Assert.Throws<ArgumentNullException>(() => new SystemPlaybackScheduler(null!));
    }

    [Fact]
    public async Task NegativeElapsedTargetIsRejected()
    {
        var scheduler = new SystemPlaybackScheduler();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => scheduler.WaitUntilElapsedAsync(scheduler.GetTimestampMicroseconds(), -1));
    }

    [Fact]
    public async Task ZeroElapsedTargetCompletesImmediately()
    {
        var scheduler = new SystemPlaybackScheduler();
        var start = scheduler.GetTimestampMicroseconds();

        await scheduler.WaitUntilElapsedAsync(start, 0);

        Assert.True(scheduler.GetTimestampMicroseconds() >= start);
    }

    [Fact]
    public async Task ShortRealWaitEventuallyReachesTarget()
    {
        var scheduler = new SystemPlaybackScheduler();
        var start = scheduler.GetTimestampMicroseconds();

        await scheduler.WaitUntilElapsedAsync(start, 20_000);

        Assert.True(scheduler.GetTimestampMicroseconds() - start >= 20_000);
    }
}
