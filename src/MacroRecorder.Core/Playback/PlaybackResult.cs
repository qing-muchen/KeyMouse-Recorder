namespace MacroRecorder.Core.Playback;

/// <summary>Immutable summary of one successfully completed playback.</summary>
public sealed record PlaybackResult
{
    public PlaybackResult(int injectedEventCount, long scheduledDurationUs, long actualElapsedUs)
        : this(injectedEventCount, scheduledDurationUs, actualElapsedUs, 1.0, 0)
    {
    }

    public PlaybackResult(
        int injectedEventCount,
        long scheduledDurationUs,
        long actualElapsedUs,
        double playbackSpeed)
        : this(injectedEventCount, scheduledDurationUs, actualElapsedUs, playbackSpeed, 0)
    {
    }

    public PlaybackResult(
        int injectedEventCount,
        long scheduledDurationUs,
        long actualElapsedUs,
        double playbackSpeed,
        long totalPausedDurationUs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(injectedEventCount);
        ArgumentOutOfRangeException.ThrowIfNegative(scheduledDurationUs);
        ArgumentOutOfRangeException.ThrowIfNegative(actualElapsedUs);
        ArgumentOutOfRangeException.ThrowIfNegative(totalPausedDurationUs);
        if (!double.IsFinite(playbackSpeed) || playbackSpeed <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playbackSpeed),
                playbackSpeed,
                "Playback speed must be a finite value greater than zero.");
        }

        InjectedEventCount = injectedEventCount;
        ScheduledDurationUs = scheduledDurationUs;
        ActualElapsedUs = actualElapsedUs;
        PlaybackSpeed = playbackSpeed;
        TotalPausedDurationUs = totalPausedDurationUs;
    }

    public int InjectedEventCount { get; }

    public long ScheduledDurationUs { get; }

    public long ActualElapsedUs { get; }

    public double PlaybackSpeed { get; }

    public long TotalPausedDurationUs { get; }
}
