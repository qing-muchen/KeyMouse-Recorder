namespace MacroRecorder.Core.Playback;

/// <summary>Immutable summary of one playback that completed or was safely stopped.</summary>
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
        : this(
            injectedEventCount,
            scheduledDurationUs,
            actualElapsedUs,
            playbackSpeed,
            totalPausedDurationUs,
            PlaybackCompletionReason.Completed,
            0)
    {
    }

    public PlaybackResult(
        int injectedEventCount,
        long scheduledDurationUs,
        long actualElapsedUs,
        double playbackSpeed,
        long totalPausedDurationUs,
        PlaybackCompletionReason completionReason,
        int releasedInputCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(injectedEventCount);
        ArgumentOutOfRangeException.ThrowIfNegative(scheduledDurationUs);
        ArgumentOutOfRangeException.ThrowIfNegative(actualElapsedUs);
        ArgumentOutOfRangeException.ThrowIfNegative(totalPausedDurationUs);
        ArgumentOutOfRangeException.ThrowIfNegative(releasedInputCount);
        if (!double.IsFinite(playbackSpeed) || playbackSpeed <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playbackSpeed),
                playbackSpeed,
                "Playback speed must be a finite value greater than zero.");
        }

        if (!Enum.IsDefined(completionReason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(completionReason),
                completionReason,
                "Playback completion reason must be defined.");
        }

        InjectedEventCount = injectedEventCount;
        ScheduledDurationUs = scheduledDurationUs;
        ActualElapsedUs = actualElapsedUs;
        PlaybackSpeed = playbackSpeed;
        TotalPausedDurationUs = totalPausedDurationUs;
        CompletionReason = completionReason;
        ReleasedInputCount = releasedInputCount;
    }

    public int InjectedEventCount { get; }

    public long ScheduledDurationUs { get; }

    public long ActualElapsedUs { get; }

    public double PlaybackSpeed { get; }

    public long TotalPausedDurationUs { get; }

    public PlaybackCompletionReason CompletionReason { get; }

    /// <summary>Number of synthesized key-up or mouse-up events successfully injected during cleanup.</summary>
    public int ReleasedInputCount { get; }
}
