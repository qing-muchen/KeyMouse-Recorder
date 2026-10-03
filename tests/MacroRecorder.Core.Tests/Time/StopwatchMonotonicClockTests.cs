using MacroRecorder.Infrastructure.Time;

namespace MacroRecorder.Core.Tests.Time;

public sealed class StopwatchMonotonicClockTests
{
    [Fact]
    public void RepeatedReadsAreNonNegativeAndNeverRegress()
    {
        var clock = new StopwatchMonotonicClock();
        var previous = clock.GetTimestampMicroseconds();
        Assert.True(previous >= 0);

        for (var index = 0; index < 10_000; index++)
        {
            var current = clock.GetTimestampMicroseconds();
            Assert.True(current >= previous, $"Clock regressed from {previous} to {current} microseconds.");
            previous = current;
        }
    }
}
