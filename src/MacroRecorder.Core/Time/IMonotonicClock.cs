namespace MacroRecorder.Core.Time;

/// <summary>
/// Supplies a monotonically non-decreasing timestamp in microseconds.
/// The timestamp may use any origin and is unrelated to wall-clock time.
/// </summary>
public interface IMonotonicClock
{
    long GetTimestampMicroseconds();
}
