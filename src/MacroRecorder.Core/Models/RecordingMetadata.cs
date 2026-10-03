namespace MacroRecorder.Core.Models;

/// <summary>
/// Recording summary in microseconds. EventCount is retained for future library previews;
/// its agreement with the event collection is checked by the future validator.
/// </summary>
public sealed record RecordingMetadata(long DurationUs, int EventCount);
