using MacroRecorder.Core.Time;

namespace MacroRecorder.Core.Recording.Mouse;

/// <summary>Applies deterministic time-based throttling only to mouse-move observations.</summary>
public sealed class MouseMoveSampler
{
    public const long DefaultIntervalUs = 8_000;

    private readonly object sync = new();
    private readonly IMonotonicClock clock;
    private readonly long intervalUs;
    private bool hasAcceptedSample;
    private long lastAcceptedTimestampUs;

    public MouseMoveSampler(IMonotonicClock clock, long intervalUs = DefaultIntervalUs)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfNegative(intervalUs);

        this.clock = clock;
        this.intervalUs = intervalUs;
    }

    public long IntervalUs => intervalUs;

    public bool ShouldRecord()
    {
        lock (sync)
        {
            var currentTimestampUs = clock.GetTimestampMicroseconds();
            if (!hasAcceptedSample ||
                intervalUs == 0 ||
                currentTimestampUs < lastAcceptedTimestampUs ||
                (Int128)currentTimestampUs - lastAcceptedTimestampUs >= intervalUs)
            {
                hasAcceptedSample = true;
                lastAcceptedTimestampUs = currentTimestampUs;
                return true;
            }

            return false;
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            hasAcceptedSample = false;
            lastAcceptedTimestampUs = 0;
        }
    }
}
