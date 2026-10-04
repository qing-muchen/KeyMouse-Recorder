using MacroRecorder.Core.Playback;
using MacroRecorder.Core.Time;
using MacroRecorder.Infrastructure.Time;

namespace MacroRecorder.Infrastructure.Playback;

/// <summary>Uses monotonic time and asynchronous coarse waits followed by short yields.</summary>
public sealed class SystemPlaybackScheduler : IPlaybackScheduler
{
    private const long FineWaitThresholdUs = 2_000;
    private const int MaximumDelayMilliseconds = int.MaxValue - 1;
    private readonly IMonotonicClock clock;

    public SystemPlaybackScheduler()
        : this(new StopwatchMonotonicClock())
    {
    }

    public SystemPlaybackScheduler(IMonotonicClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        this.clock = clock;
    }

    public long GetTimestampMicroseconds() => clock.GetTimestampMicroseconds();

    public Task WaitUntilElapsedAsync(long playbackStartTimestampUs, long targetElapsedUs) =>
        WaitUntilElapsedAsync(playbackStartTimestampUs, targetElapsedUs, CancellationToken.None);

    public async Task WaitUntilElapsedAsync(
        long playbackStartTimestampUs,
        long targetElapsedUs,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetElapsedUs);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentTimestampUs = clock.GetTimestampMicroseconds();
            var elapsedUs = (Int128)currentTimestampUs - playbackStartTimestampUs;
            if (elapsedUs >= targetElapsedUs)
            {
                return;
            }

            var remainingUs = (Int128)targetElapsedUs - elapsedUs;
            if (remainingUs > FineWaitThresholdUs)
            {
                var coarseDelayUs = remainingUs - 1_000;
                var delayMilliseconds = (int)Int128.Min(
                    coarseDelayUs / 1_000,
                    MaximumDelayMilliseconds);
                await Task.Delay(Math.Max(1, delayMilliseconds), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await Task.Yield();
            }
        }
    }
}
