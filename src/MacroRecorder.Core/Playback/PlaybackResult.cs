namespace MacroRecorder.Core.Playback;

/// <summary>Immutable summary of one successfully completed playback.</summary>
public sealed record PlaybackResult
{
    public PlaybackResult(int injectedEventCount, long scheduledDurationUs, long actualElapsedUs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(injectedEventCount);
        ArgumentOutOfRangeException.ThrowIfNegative(scheduledDurationUs);
        ArgumentOutOfRangeException.ThrowIfNegative(actualElapsedUs);

        InjectedEventCount = injectedEventCount;
        ScheduledDurationUs = scheduledDurationUs;
        ActualElapsedUs = actualElapsedUs;
    }

    public int InjectedEventCount { get; }

    public long ScheduledDurationUs { get; }

    public long ActualElapsedUs { get; }
}
