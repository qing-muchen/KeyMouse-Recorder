namespace MacroRecorder.Core.Playback;

/// <summary>Raised when a speed-scaled timeline value cannot be represented in microseconds.</summary>
public sealed class PlaybackTimingException : Exception
{
    public PlaybackTimingException(long originalTimestampUs, double playbackSpeed)
        : base("The speed-scaled playback timestamp cannot be represented as a 64-bit microsecond value.")
    {
        OriginalTimestampUs = originalTimestampUs;
        PlaybackSpeed = playbackSpeed;
    }

    public long OriginalTimestampUs { get; }

    public double PlaybackSpeed { get; }
}
