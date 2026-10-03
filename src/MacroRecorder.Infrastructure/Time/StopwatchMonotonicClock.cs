using System.Diagnostics;
using MacroRecorder.Core.Time;

namespace MacroRecorder.Infrastructure.Time;

/// <summary>
/// Uses the platform's Stopwatch counter and exposes elapsed whole microseconds from this
/// clock instance's origin. Macro timelines therefore never depend on Stopwatch frequency.
/// </summary>
public sealed class StopwatchMonotonicClock : IMonotonicClock
{
    private readonly long originTimestamp = Stopwatch.GetTimestamp();

    public long GetTimestampMicroseconds()
    {
        var elapsed = Stopwatch.GetElapsedTime(originTimestamp, Stopwatch.GetTimestamp());
        return elapsed.Ticks / TimeSpan.TicksPerMicrosecond;
    }
}
