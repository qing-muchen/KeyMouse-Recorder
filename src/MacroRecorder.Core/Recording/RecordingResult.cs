using System.Collections.Immutable;
using MacroRecorder.Core.Models.Input;

namespace MacroRecorder.Core.Recording;

/// <summary>
/// Immutable output from a stopped session. An application service can later combine it
/// with user-supplied and environment metadata to construct a Macro.
/// </summary>
public sealed record RecordingResult
{
    public RecordingResult(long durationUs, ImmutableArray<InputEvent> events)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(durationUs);
        if (events.IsDefault)
        {
            throw new ArgumentException("Events must be an initialized immutable array.", nameof(events));
        }

        DurationUs = durationUs;
        Events = events;
    }

    public long DurationUs { get; }
    public ImmutableArray<InputEvent> Events { get; }
    public int EventCount => Events.Length;
}
