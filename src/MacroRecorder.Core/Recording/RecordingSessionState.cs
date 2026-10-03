namespace MacroRecorder.Core.Recording;

/// <summary>Runtime state only; this is not part of the persisted macro schema.</summary>
public enum RecordingSessionState
{
    Recording,
    Stopped,
    Cancelled,
}
