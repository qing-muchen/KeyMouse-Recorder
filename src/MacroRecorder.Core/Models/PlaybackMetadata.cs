namespace MacroRecorder.Core.Models;

/// <summary>Saved playback preference, not the state of a playback session.</summary>
public sealed record PlaybackMetadata(double DefaultSpeed = 1.0);
