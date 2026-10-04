namespace MacroRecorder.Core.Playback;

/// <summary>
/// Supplies monotonic playback time and waits for an elapsed target measured from one playback start.
/// Target values are absolute within that playback, not delays from the previous event.
/// </summary>
public interface IPlaybackScheduler
{
    long GetTimestampMicroseconds();

    Task WaitUntilElapsedAsync(long playbackStartTimestampUs, long targetElapsedUs);
}
