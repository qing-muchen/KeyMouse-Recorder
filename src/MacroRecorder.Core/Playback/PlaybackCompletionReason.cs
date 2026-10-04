namespace MacroRecorder.Core.Playback;

/// <summary>Describes how a playback session reached its terminal state.</summary>
public enum PlaybackCompletionReason
{
    Completed,
    Stopped,
    EmergencyStopped,
}
