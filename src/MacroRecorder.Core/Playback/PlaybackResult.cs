namespace MacroRecorder.Core.Playback;

/// <summary>Immutable summary of one successfully completed playback.</summary>
public sealed record PlaybackResult
{
    public PlaybackResult(int injectedEventCount, long scheduledDurationUs, long actualElapsedUs)
        : this(injectedEventCount, scheduledDurationUs, actualElapsedUs, 1.0)
    {
    }

    public PlaybackResult(
        int injectedEventCount,
        long scheduledDurationUs,
        long actualElapsedUs,
        double playbackSpeed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(injectedEventCount);
        ArgumentOutOfRangeException.ThrowIfNegative(scheduledDurationUs);
        ArgumentOutOfRangeException.ThrowIfNegative(actualElapsedUs);
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
    }

    public int InjectedEventCount { get; }

    public long ScheduledDurationUs { get; }

    public long ActualElapsedUs { get; }

    public double PlaybackSpeed { get; }
}
