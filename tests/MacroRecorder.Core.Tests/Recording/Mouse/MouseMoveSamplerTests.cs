using MacroRecorder.Core.Recording.Mouse;
using MacroRecorder.Core.Tests.TestDoubles;

namespace MacroRecorder.Core.Tests.Recording.Mouse;

public sealed class MouseMoveSamplerTests
{
    [Fact]
    public void DefaultIntervalIsEightMilliseconds()
    {
        var sampler = new MouseMoveSampler(new FakeMonotonicClock());

        Assert.Equal(8_000, sampler.IntervalUs);
    }

    [Fact]
    public void FirstMoveIsAccepted()
    {
        var sampler = new MouseMoveSampler(new FakeMonotonicClock(1_000_000));

        Assert.True(sampler.ShouldRecord());
    }

    [Fact]
    public void SamplingUsesLastAcceptedMoveAsBaseline()
    {
        var clock = new FakeMonotonicClock(1_000_000);
        var sampler = new MouseMoveSampler(clock, 8_000);

        Assert.True(sampler.ShouldRecord());
        clock.SetMicroseconds(1_002_000);
        Assert.False(sampler.ShouldRecord());
        clock.SetMicroseconds(1_007_999);
        Assert.False(sampler.ShouldRecord());
        clock.SetMicroseconds(1_008_000);
        Assert.True(sampler.ShouldRecord());
        clock.SetMicroseconds(1_009_000);
        Assert.False(sampler.ShouldRecord());
        clock.SetMicroseconds(1_016_000);
        Assert.True(sampler.ShouldRecord());
    }

    [Fact]
    public void ZeroIntervalAcceptsEveryMove()
    {
        var clock = new FakeMonotonicClock(100);
        var sampler = new MouseMoveSampler(clock, 0);

        Assert.True(sampler.ShouldRecord());
        Assert.True(sampler.ShouldRecord());
        Assert.True(sampler.ShouldRecord());
    }

    [Fact]
    public void NegativeIntervalIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MouseMoveSampler(new FakeMonotonicClock(), -1));
    }

    [Fact]
    public void ClockRegressionAcceptsMoveAndResetsBaseline()
    {
        var clock = new FakeMonotonicClock(10_000);
        var sampler = new MouseMoveSampler(clock, 1_000);
        Assert.True(sampler.ShouldRecord());

        clock.SetMicroseconds(9_000);
        Assert.True(sampler.ShouldRecord());
        clock.SetMicroseconds(9_999);
        Assert.False(sampler.ShouldRecord());
        clock.SetMicroseconds(10_000);
        Assert.True(sampler.ShouldRecord());
    }

    [Fact]
    public void ResetMakesNextMoveAFirstSample()
    {
        var clock = new FakeMonotonicClock(10_000);
        var sampler = new MouseMoveSampler(clock, 8_000);
        Assert.True(sampler.ShouldRecord());
        Assert.False(sampler.ShouldRecord());

        sampler.Reset();

        Assert.True(sampler.ShouldRecord());
    }
}
