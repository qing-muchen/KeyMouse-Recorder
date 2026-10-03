using System.Collections.Immutable;
using MacroRecorder.Core.Models.Input;
using MacroRecorder.Core.Time;

namespace MacroRecorder.Core.Recording;

/// <summary>
/// Collects one ordered input timeline. Construction starts recording immediately.
/// All state, timestamp generation, and collection access are serialized by a short lock.
/// </summary>
public sealed class RecordingSession
{
    private readonly object sync = new();
    private readonly IMonotonicClock clock;
    private readonly long startTimestampUs;
    private List<InputEvent>? events = [];
    private RecordingSessionState state = RecordingSessionState.Recording;
    private long lastTimestampUs;

    public RecordingSession(IMonotonicClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        this.clock = clock;
        startTimestampUs = clock.GetTimestampMicroseconds();
    }

    public RecordingSessionState State
    {
        get
        {
            lock (sync)
            {
                return state;
            }
        }
    }

    public KeyboardInputEvent AddKeyboardEvent(
        InputEventType eventType,
        uint virtualKey,
        uint scanCode,
        uint flags)
    {
        lock (sync)
        {
            var activeEvents = GetActiveEvents();
            var inputEvent = new KeyboardInputEvent(
                GetNextTimestampUs(), eventType, virtualKey, scanCode, flags);
            activeEvents.Add(inputEvent);
            return inputEvent;
        }
    }

    public MouseInputEvent AddMouseEvent(
        InputEventType eventType,
        int x,
        int y,
        MouseButton button,
        int wheelDelta)
    {
        lock (sync)
        {
            var activeEvents = GetActiveEvents();
            var inputEvent = new MouseInputEvent(
                GetNextTimestampUs(), eventType, x, y, button, wheelDelta);
            activeEvents.Add(inputEvent);
            return inputEvent;
        }
    }

    public RecordingResult Stop()
    {
        lock (sync)
        {
            var activeEvents = GetActiveEvents();
            var durationUs = GetNextTimestampUs();
            var result = new RecordingResult(durationUs, activeEvents.ToImmutableArray());

            events = null;
            state = RecordingSessionState.Stopped;
            return result;
        }
    }

    public void Cancel()
    {
        lock (sync)
        {
            var activeEvents = GetActiveEvents();
            activeEvents.Clear();
            events = null;
            state = RecordingSessionState.Cancelled;
        }
    }

    private List<InputEvent> GetActiveEvents()
    {
        if (state != RecordingSessionState.Recording || events is null)
        {
            throw new InvalidOperationException($"The recording session is {state} and can no longer be changed.");
        }

        return events;
    }

    private long GetNextTimestampUs()
    {
        var currentTimestampUs = clock.GetTimestampMicroseconds();
        var elapsed = (Int128)currentTimestampUs - startTimestampUs;
        long relativeTimestampUs;
        if (elapsed <= 0)
        {
            relativeTimestampUs = 0;
        }
        else if (elapsed > long.MaxValue)
        {
            relativeTimestampUs = long.MaxValue;
        }
        else
        {
            relativeTimestampUs = (long)elapsed;
        }

        // A production Stopwatch is monotonic. Clamping protects the persisted timeline if
        // a test double or unusual platform clock regresses, while preserving equal timestamps.
        lastTimestampUs = Math.Max(lastTimestampUs, relativeTimestampUs);
        return lastTimestampUs;
    }
}
