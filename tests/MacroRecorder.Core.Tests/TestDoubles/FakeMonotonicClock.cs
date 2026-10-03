using MacroRecorder.Core.Time;

namespace MacroRecorder.Core.Tests.TestDoubles;

internal sealed class FakeMonotonicClock(long initialMicroseconds = 0) : IMonotonicClock
{
    private long currentMicroseconds = initialMicroseconds;

    public long GetTimestampMicroseconds() => Interlocked.Read(ref currentMicroseconds);

    public void SetMicroseconds(long value) => Interlocked.Exchange(ref currentMicroseconds, value);

    public long AdvanceMicroseconds(long value) => Interlocked.Add(ref currentMicroseconds, value);
}
