using System.Collections.Immutable;
using MacroRecorder.Core.InputInjection;
using MacroRecorder.Core.Models;
using MacroRecorder.Core.Models.Input;
using MacroRecorder.Core.Playback;
using MacroRecorder.Core.Schema;
using MacroRecorder.Core.Validation;

namespace MacroRecorder.Core.Tests.Playback;

public sealed class PlaybackEngineTests
{
    [Fact]
    public void ConstructorRejectsNullDependencies()
    {
        var scheduler = new FakePlaybackScheduler();
        var injector = new FakeInputInjector(scheduler);

        Assert.Throws<ArgumentNullException>(() => new PlaybackEngine(null!, injector));
        Assert.Throws<ArgumentNullException>(() => new PlaybackEngine(scheduler, null!));
    }

    [Fact]
    public void InitialStateIsIdle()
    {
        var (engine, _, _) = CreateEngine();

        Assert.Equal(PlaybackState.Idle, engine.State);
    }

    [Fact]
    public async Task EmptyZeroDurationMacroCompletesWithoutInjection()
    {
        var (engine, scheduler, injector) = CreateEngine();

        var result = await engine.PlayAsync(CreateMacro([], 0));

        Assert.Empty(injector.InjectedEvents);
        Assert.Equal([0], scheduler.RequestedTargets);
        Assert.Equal(0, result.InjectedEventCount);
        Assert.Equal(0, result.ScheduledDurationUs);
        Assert.Equal(0, result.ActualElapsedUs);
        Assert.Equal(PlaybackState.Idle, engine.State);
    }

    [Fact]
    public async Task EmptyPositiveDurationMacroPreservesFullDuration()
    {
        var (engine, scheduler, injector) = CreateEngine();

        var result = await engine.PlayAsync(CreateMacro([], 5_000_000));

        Assert.Empty(injector.InjectedEvents);
        Assert.Equal([5_000_000], scheduler.RequestedTargets);
        Assert.Equal(5_000_000, result.ActualElapsedUs);
    }

    [Fact]
    public async Task KeyboardOnlyMacroPlaysInOriginalOrder()
    {
        var events = ImmutableArray.Create<InputEvent>(KeyDown(0), KeyUp(50_000));
        var (engine, scheduler, injector) = CreateEngine();

        var result = await engine.PlayAsync(CreateMacro(events, 100_000));

        Assert.Equal(events, injector.InjectedEvents);
        Assert.Equal([0, 50_000, 100_000], scheduler.RequestedTargets);
        Assert.Equal(2, result.InjectedEventCount);
    }

    [Fact]
    public async Task MouseOnlyMacroPlaysInOriginalOrder()
    {
        var events = ImmutableArray.Create<InputEvent>(
            Move(0, 100, 200),
            Button(100_000, InputEventType.MouseButtonDown),
            Button(150_000, InputEventType.MouseButtonUp),
            Wheel(250_000));
        var (engine, _, injector) = CreateEngine();

        await engine.PlayAsync(CreateMacro(events, 300_000));

        Assert.Equal(events, injector.InjectedEvents);
    }

    [Fact]
    public async Task MixedMacroPreservesArrayOrder()
    {
        var events = ImmutableArray.Create<InputEvent>(
            Move(0, 10, 20),
            KeyDown(10_000),
            Button(10_000, InputEventType.MouseButtonDown),
            KeyUp(10_000),
            Button(20_000, InputEventType.MouseButtonUp));
        var (engine, _, injector) = CreateEngine();

        await engine.PlayAsync(CreateMacro(events, 25_000));

        Assert.Equal(events, injector.InjectedEvents);
    }

    [Fact]
    public async Task SameTimestampEventsRemainInOriginalOrder()
    {
        var first = KeyDown(100);
        var second = Button(100, InputEventType.MouseButtonDown);
        var third = KeyUp(100);
        var events = ImmutableArray.Create<InputEvent>(first, second, third);
        var (engine, scheduler, injector) = CreateEngine();

        await engine.PlayAsync(CreateMacro(events, 100));

        Assert.Equal([first, second, third], injector.InjectedEvents);
        Assert.Equal([100, 100, 100, 100], scheduler.RequestedTargets);
    }

    [Fact]
    public async Task TimestampZeroIsRequestedFromPlaybackStart()
    {
        var (engine, scheduler, injector) = CreateEngine(initialTimestampUs: 45_000);

        await engine.PlayAsync(CreateMacro([KeyDown(0)], 0));

        Assert.Equal([0, 0], scheduler.RequestedTargets);
        Assert.Equal([0], injector.InjectionElapsedTimes);
    }

    [Fact]
    public async Task FutureTimestampsAndIdleTailUseAbsoluteElapsedTargets()
    {
        var events = ImmutableArray.Create<InputEvent>(KeyDown(100_000), KeyUp(250_000));
        var (engine, scheduler, _) = CreateEngine(initialTimestampUs: 77_000);

        var result = await engine.PlayAsync(CreateMacro(events, 500_000));

        Assert.Equal([100_000, 250_000, 500_000], scheduler.RequestedTargets);
        Assert.Equal(500_000, result.ScheduledDurationUs);
        Assert.Equal(500_000, result.ActualElapsedUs);
    }

    [Fact]
    public async Task DefaultSpeedDoesNotScalePhaseTenTargets()
    {
        var (engine, scheduler, _) = CreateEngine();
        var macro = CreateMacro([KeyDown(100_000)], 200_000) with
        {
            Playback = new PlaybackMetadata(4.0),
        };

        await engine.PlayAsync(macro);

        Assert.Equal([100_000, 200_000], scheduler.RequestedTargets);
        Assert.Equal(4.0, macro.Playback.DefaultSpeed);
    }

    [Theory]
    [InlineData(0.25, 400_000, 800_000, 1_600_000)]
    [InlineData(1.0, 100_000, 200_000, 400_000)]
    [InlineData(2.0, 50_000, 100_000, 200_000)]
    [InlineData(4.0, 25_000, 50_000, 100_000)]
    [InlineData(0.5, 200_000, 400_000, 800_000)]
    public async Task RuntimeSpeedScalesAbsoluteEventAndDurationTargets(
        double speed,
        long firstTargetUs,
        long secondTargetUs,
        long durationTargetUs)
    {
        var events = ImmutableArray.Create<InputEvent>(KeyDown(100_000), KeyUp(200_000));
        var (engine, scheduler, injector) = CreateEngine();

        var result = await engine.PlayAsync(CreateMacro(events, 400_000), speed);

        Assert.Equal([firstTargetUs, secondTargetUs, durationTargetUs], scheduler.RequestedTargets);
        Assert.Equal(events, injector.InjectedEvents);
        Assert.Equal(durationTargetUs, result.ScheduledDurationUs);
        Assert.Equal(durationTargetUs, result.ActualElapsedUs);
        Assert.Equal(speed, result.PlaybackSpeed);
    }

    [Theory]
    [InlineData(0.01, 10_000)]
    [InlineData(100.0, 1)]
    public async Task PositiveFiniteSpeedsAreNotArtificiallyRangeLimited(
        double speed,
        long expectedTargetUs)
    {
        var (engine, scheduler, _) = CreateEngine();

        var result = await engine.PlayAsync(CreateMacro([KeyDown(100)], 100), speed);

        Assert.Equal([expectedTargetUs, expectedTargetUs], scheduler.RequestedTargets);
        Assert.Equal(speed, result.PlaybackSpeed);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task InvalidSpeedIsRejectedBeforeMacroValidationSchedulerOrInjector(double speed)
    {
        var invalidMacro = CreateMacro([], 0) with
        {
            SchemaVersion = MacroSchema.CurrentVersion + 1,
        };
        var (engine, scheduler, injector) = CreateEngine();

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => engine.PlayAsync(invalidMacro, speed));

        Assert.Equal("speed", exception.ParamName);
        Assert.Equal(0, scheduler.GetTimestampCallCount);
        Assert.Empty(scheduler.RequestedTargets);
        Assert.Empty(injector.InjectedEvents);
        Assert.Equal(PlaybackState.Idle, engine.State);
    }

    [Fact]
    public async Task ExplicitSpeedPreservesSameTimestampOrder()
    {
        var first = KeyDown(100);
        var second = Button(100, InputEventType.MouseButtonDown);
        var third = KeyUp(100);
        var events = ImmutableArray.Create<InputEvent>(first, second, third);
        var (engine, scheduler, injector) = CreateEngine();

        await engine.PlayAsync(CreateMacro(events, 100), 2.0);

        Assert.Equal([first, second, third], injector.InjectedEvents);
        Assert.Equal([50, 50, 50, 50], scheduler.RequestedTargets);
    }

    [Fact]
    public async Task ExplicitSpeedKeepsTimestampZeroAtZero()
    {
        var (engine, scheduler, injector) = CreateEngine(initialTimestampUs: 45_000);

        await engine.PlayAsync(CreateMacro([KeyDown(0)], 0), 4.0);

        Assert.Equal([0, 0], scheduler.RequestedTargets);
        Assert.Equal([0], injector.InjectionElapsedTimes);
    }

    [Fact]
    public async Task SpeedScalingRoundsFractionalMicrosecondsAwayFromZero()
    {
        var (engine, scheduler, _) = CreateEngine();

        await engine.PlayAsync(CreateMacro([KeyDown(1)], 1), 2.0);

        Assert.Equal([1, 1], scheduler.RequestedTargets);
    }

    [Fact]
    public async Task LateEventBehaviorUsesScaledAbsoluteTargets()
    {
        var events = ImmutableArray.Create<InputEvent>(KeyDown(100), KeyUp(110), KeyDown(120));
        var (engine, scheduler, injector) = CreateEngine();
        scheduler.AdvanceBeforeWait.Enqueue(75);

        await engine.PlayAsync(CreateMacro(events, 150), 2.0);

        Assert.Equal(events, injector.InjectedEvents);
        Assert.Equal([75, 75, 75], injector.InjectionElapsedTimes);
        Assert.Equal([50, 55, 60, 75], scheduler.RequestedTargets);
    }

    [Fact]
    public async Task EmptyMacroDurationIsScaled()
    {
        var (engine, scheduler, injector) = CreateEngine();

        var result = await engine.PlayAsync(CreateMacro([], 10_000_000), 2.0);

        Assert.Empty(injector.InjectedEvents);
        Assert.Equal([5_000_000], scheduler.RequestedTargets);
        Assert.Equal(5_000_000, result.ScheduledDurationUs);
        Assert.Equal(5_000_000, result.ActualElapsedUs);
    }

    [Fact]
    public async Task LargeTimestampCanBeSafelyScaled()
    {
        const long expectedTargetUs = 4_611_686_018_427_387_904;
        var (engine, scheduler, _) = CreateEngine();

        var result = await engine.PlayAsync(
            CreateMacro([KeyDown(long.MaxValue)], long.MaxValue),
            2.0);

        Assert.Equal([expectedTargetUs, expectedTargetUs], scheduler.RequestedTargets);
        Assert.Equal(expectedTargetUs, result.ScheduledDurationUs);
        Assert.DoesNotContain(scheduler.RequestedTargets, static target => target < 0);
    }

    [Fact]
    public async Task UnrepresentableScaledTimestampFailsBeforeSchedulerOrInjector()
    {
        var macro = CreateMacro([KeyDown(long.MaxValue)], long.MaxValue);
        var (engine, scheduler, injector) = CreateEngine();

        var exception = await Assert.ThrowsAsync<PlaybackTimingException>(
            () => engine.PlayAsync(macro, 0.01));

        Assert.Equal(long.MaxValue, exception.OriginalTimestampUs);
        Assert.Equal(0.01, exception.PlaybackSpeed);
        Assert.Equal(0, scheduler.GetTimestampCallCount);
        Assert.Empty(scheduler.RequestedTargets);
        Assert.Empty(injector.InjectedEvents);
        Assert.Equal(PlaybackState.Idle, engine.State);
    }

    [Fact]
    public async Task ExplicitRuntimeSpeedDoesNotModifyMacroOrPlaybackDefaults()
    {
        var events = ImmutableArray.Create<InputEvent>(KeyDown(10), KeyUp(20));
        var macro = CreateMacro(events, 30) with
        {
            Playback = new PlaybackMetadata(1.5),
        };
        var snapshot = macro with { };
        var (engine, _, _) = CreateEngine();

        await engine.PlayAsync(macro, 4.0);

        Assert.Equal(snapshot, macro);
        Assert.Equal(events, macro.Events);
        Assert.Equal(1.5, macro.Playback.DefaultSpeed);
    }

    [Fact]
    public async Task RepeatedPlaybackCanUseDifferentRuntimeSpeeds()
    {
        var events = ImmutableArray.Create<InputEvent>(KeyDown(100), KeyUp(200));
        var macro = CreateMacro(events, 400);
        var (engine, scheduler, injector) = CreateEngine();

        var fastResult = await engine.PlayAsync(macro, 2.0);
        var slowResult = await engine.PlayAsync(macro, 0.5);

        Assert.Equal([50, 100, 200, 200, 400, 800], scheduler.RequestedTargets);
        Assert.Equal([.. events, .. events], injector.InjectedEvents);
        Assert.Equal(2.0, fastResult.PlaybackSpeed);
        Assert.Equal(0.5, slowResult.PlaybackSpeed);
        Assert.Equal(PlaybackState.Idle, engine.State);
    }

    [Fact]
    public async Task SchedulerOvershootDoesNotAccumulateDrift()
    {
        var events = ImmutableArray.Create<InputEvent>(KeyDown(100), KeyUp(200), KeyDown(300));
        var (engine, scheduler, injector) = CreateEngine();
        scheduler.OvershootByWait.Enqueue(50);
        scheduler.OvershootByWait.Enqueue(0);
        scheduler.OvershootByWait.Enqueue(0);
        scheduler.OvershootByWait.Enqueue(0);

        await engine.PlayAsync(CreateMacro(events, 300));

        Assert.Equal([100, 200, 300, 300], scheduler.RequestedTargets);
        Assert.Equal([150, 200, 300], injector.InjectionElapsedTimes);
    }

    [Fact]
    public async Task LateEventsInjectImmediatelyAndPreserveOrder()
    {
        var events = ImmutableArray.Create<InputEvent>(KeyDown(100), KeyUp(110), KeyDown(120));
        var (engine, scheduler, injector) = CreateEngine();
        scheduler.AdvanceBeforeWait.Enqueue(150);

        await engine.PlayAsync(CreateMacro(events, 150));

        Assert.Equal(events, injector.InjectedEvents);
        Assert.Equal([150, 150, 150], injector.InjectionElapsedTimes);
        Assert.Equal([100, 110, 120, 150], scheduler.RequestedTargets);
    }

    [Fact]
    public async Task EightHourMacroCompletesInstantlyWithFakeScheduler()
    {
        const long eightHoursUs = 8L * 60 * 60 * 1_000_000;
        var (engine, scheduler, _) = CreateEngine();

        var result = await engine.PlayAsync(CreateMacro([KeyDown(eightHoursUs)], eightHoursUs));

        Assert.Equal([eightHoursUs, eightHoursUs], scheduler.RequestedTargets);
        Assert.Equal(eightHoursUs, result.ActualElapsedUs);
    }

    [Fact]
    public async Task LongMaxTimelineDoesNotWrap()
    {
        var (engine, scheduler, _) = CreateEngine(initialTimestampUs: -1);

        var result = await engine.PlayAsync(CreateMacro([KeyDown(long.MaxValue)], long.MaxValue));

        Assert.Equal([long.MaxValue, long.MaxValue], scheduler.RequestedTargets);
        Assert.Equal(long.MaxValue, result.ScheduledDurationUs);
        Assert.Equal(long.MaxValue, result.ActualElapsedUs);
        Assert.DoesNotContain(scheduler.RequestedTargets, static target => target < 0);
    }

    [Theory]
    [MemberData(nameof(InvalidMacros))]
    public async Task InvalidMacroIsRejectedBeforeSchedulerOrInjector(Macro macro, string expectedCode)
    {
        var (engine, scheduler, injector) = CreateEngine();

        var exception = await Assert.ThrowsAsync<PlaybackValidationException>(() => engine.PlayAsync(macro));

        Assert.Contains(exception.ValidationResult.Issues, issue => issue.Code == expectedCode);
        Assert.False(exception.ValidationResult.IsValid);
        Assert.Equal(0, scheduler.GetTimestampCallCount);
        Assert.Empty(scheduler.RequestedTargets);
        Assert.Empty(injector.InjectedEvents);
        Assert.Equal(PlaybackState.Idle, engine.State);
    }

    public static TheoryData<Macro, string> InvalidMacros => new()
    {
        {
            CreateMacro([], 0) with { SchemaVersion = MacroSchema.CurrentVersion + 1 },
            ValidationCodes.SchemaVersionUnsupported
        },
        {
            CreateMacro([KeyDown(200), KeyUp(100)], 200),
            ValidationCodes.TimestampDecreasing
        },
        {
            CreateMacro([new MouseInputEvent(0, InputEventType.MouseMove, 0, 0, MouseButton.Left, 0)], 0),
            ValidationCodes.MouseButtonMustBeNone
        },
    };

    [Fact]
    public async Task WarningOnlyMacroIsAllowed()
    {
        var macro = CreateMacro([], 25) with
        {
            CreatedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var (engine, scheduler, _) = CreateEngine();

        var result = await engine.PlayAsync(macro);

        Assert.Equal(25, result.ActualElapsedUs);
        Assert.Equal([25], scheduler.RequestedTargets);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public async Task InjectorFailureStopsPlaybackAndRestoresIdle(int throwOnIndex, int expectedInjectedCount)
    {
        var events = ImmutableArray.Create<InputEvent>(KeyDown(0), KeyUp(100), KeyDown(200));
        var (engine, scheduler, injector) = CreateEngine();
        var failure = new InvalidOperationException("injection failed");
        injector.ThrowOnIndex = throwOnIndex;
        injector.ExceptionToThrow = failure;

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.PlayAsync(CreateMacro(events, 300), 2.0));

        Assert.Same(failure, actual);
        Assert.Equal(expectedInjectedCount, injector.InjectedEvents.Count);
        Assert.DoesNotContain(150, scheduler.RequestedTargets);
        Assert.Equal(PlaybackState.Idle, engine.State);
    }

    [Fact]
    public async Task EngineCanPlayAgainAfterInjectionFailure()
    {
        var (engine, _, injector) = CreateEngine();
        injector.ThrowOnIndex = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.PlayAsync(CreateMacro([KeyDown(0)], 0)));
        injector.ThrowOnIndex = null;
        var result = await engine.PlayAsync(CreateMacro([KeyUp(0)], 0));

        Assert.Equal(1, result.InjectedEventCount);
        Assert.Equal(InputEventType.KeyboardKeyUp, injector.InjectedEvents[^1].EventType);
    }

    [Fact]
    public async Task InputInjectionExceptionPropagatesUnchanged()
    {
        var (engine, _, injector) = CreateEngine();
        var failure = new InputInjectionException(1, 0, 5);
        injector.ThrowOnIndex = 0;
        injector.ExceptionToThrow = failure;

        var actual = await Assert.ThrowsAsync<InputInjectionException>(
            () => engine.PlayAsync(CreateMacro([KeyDown(0)], 100)));

        Assert.Same(failure, actual);
        Assert.Equal(PlaybackState.Idle, engine.State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task SchedulerFailurePropagatesAndRestoresIdle(int throwOnWaitIndex)
    {
        var (engine, scheduler, injector) = CreateEngine();
        var failure = new TimeoutException("scheduler failed");
        scheduler.ThrowOnWaitIndex = throwOnWaitIndex;
        scheduler.ExceptionToThrow = failure;
        var macro = CreateMacro([KeyDown(0)], 100);

        var actual = await Assert.ThrowsAsync<TimeoutException>(() => engine.PlayAsync(macro));

        Assert.Same(failure, actual);
        Assert.Equal(throwOnWaitIndex == 0 ? 0 : 1, injector.InjectedEvents.Count);
        Assert.Equal(PlaybackState.Idle, engine.State);
    }

    [Fact]
    public async Task ActualElapsedReflectsSchedulerOvershoot()
    {
        var (engine, scheduler, _) = CreateEngine();
        scheduler.OvershootByWait.Enqueue(0);
        scheduler.OvershootByWait.Enqueue(25);

        var result = await engine.PlayAsync(CreateMacro([KeyDown(50)], 100));

        Assert.Equal(125, result.ActualElapsedUs);
        Assert.Equal(100, result.ScheduledDurationUs);
    }

    [Fact]
    public async Task ClockRegressionClampsActualElapsedToZero()
    {
        var (engine, scheduler, _) = CreateEngine(initialTimestampUs: 1_000);
        scheduler.DoNotAdvance = true;
        scheduler.FinalTimestampOverride = 900;

        var result = await engine.PlayAsync(CreateMacro([], 0));

        Assert.Equal(0, result.ActualElapsedUs);
    }

    [Fact]
    public async Task ConcurrentPlayIsRejectedWhileFirstPlaybackContinues()
    {
        var scheduler = new BlockingPlaybackScheduler();
        var injector = new FakeInputInjector(scheduler);
        var engine = new PlaybackEngine(scheduler, injector);
        var macro = CreateMacro([KeyDown(100)], 100);

        var firstPlayback = engine.PlayAsync(macro);
        await scheduler.WaitEntered.Task;
        Assert.Equal(PlaybackState.Playing, engine.State);

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.PlayAsync(macro, 2.0));
        Assert.False(firstPlayback.IsCompleted);

        scheduler.Release.TrySetResult();
        var result = await firstPlayback;
        Assert.Equal(1, result.InjectedEventCount);
        Assert.Equal(PlaybackState.Idle, engine.State);
    }

    [Fact]
    public async Task SameMacroCanBePlayedThreeTimesWithoutMutation()
    {
        var events = ImmutableArray.Create<InputEvent>(KeyDown(10), KeyUp(20));
        var macro = CreateMacro(events, 30);
        var snapshot = macro with { };
        var (engine, scheduler, injector) = CreateEngine();

        for (var iteration = 0; iteration < 3; iteration++)
        {
            var result = await engine.PlayAsync(macro);
            Assert.Equal(2, result.InjectedEventCount);
        }

        Assert.Equal([10, 20, 30, 10, 20, 30, 10, 20, 30], scheduler.RequestedTargets);
        Assert.Equal([.. events, .. events, .. events], injector.InjectedEvents);
        Assert.Equal(snapshot, macro);
        Assert.Equal(events, macro.Events);
    }

    [Fact]
    public async Task EngineCanPlayDifferentMacrosSequentially()
    {
        var first = CreateMacro([KeyDown(0)], 10);
        var second = CreateMacro([Move(0, -100, 200), Wheel(20)], 30);
        var (engine, _, injector) = CreateEngine();

        await engine.PlayAsync(first);
        await engine.PlayAsync(second);

        Assert.Equal([.. first.Events, .. second.Events], injector.InjectedEvents);
        Assert.Equal(PlaybackState.Idle, engine.State);
    }

    [Fact]
    public async Task NullMacroIsRejectedWithoutChangingState()
    {
        var (engine, scheduler, injector) = CreateEngine();

        await Assert.ThrowsAsync<ArgumentNullException>(() => engine.PlayAsync(null!));

        Assert.Equal(PlaybackState.Idle, engine.State);
        Assert.Equal(0, scheduler.GetTimestampCallCount);
        Assert.Empty(injector.InjectedEvents);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void PlaybackResultRejectsNegativeValues(int eventCount, long scheduledDuration, long actualElapsed)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackResult(eventCount, scheduledDuration, actualElapsed));
    }

    [Fact]
    public void PlaybackResultPreservesBackwardCompatibleDefaultSpeed()
    {
        var result = new PlaybackResult(2, 100, 105);

        Assert.Equal(1.0, result.PlaybackSpeed);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void PlaybackResultRejectsInvalidSpeed(double speed)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackResult(0, 0, 0, speed));

        Assert.Equal("playbackSpeed", exception.ParamName);
    }

    private static EngineContext CreateEngine(long initialTimestampUs = 0)
    {
        var scheduler = new FakePlaybackScheduler(initialTimestampUs);
        var injector = new FakeInputInjector(scheduler);
        return new EngineContext(new PlaybackEngine(scheduler, injector), scheduler, injector);
    }

    private static Macro CreateMacro(ImmutableArray<InputEvent> events, long durationUs) => new()
    {
        SchemaVersion = MacroSchema.CurrentVersion,
        Id = Guid.Parse("d4f0e9f1-c893-46ed-9a79-5d9f25b2e437"),
        Name = "Playback test",
        Description = "Non-sensitive test events",
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Recording = new RecordingMetadata(durationUs, events.Length),
        Environment = new EnvironmentMetadata(1920, 1080, 1.0),
        Playback = new PlaybackMetadata(1.0),
        Events = events,
    };

    private static KeyboardInputEvent KeyDown(long timestampUs) =>
        new(timestampUs, InputEventType.KeyboardKeyDown, 65, 30, 0);

    private static KeyboardInputEvent KeyUp(long timestampUs) =>
        new(timestampUs, InputEventType.KeyboardKeyUp, 65, 30, 0);

    private static MouseInputEvent Move(long timestampUs, int x, int y) =>
        new(timestampUs, InputEventType.MouseMove, x, y, MouseButton.None, 0);

    private static MouseInputEvent Button(long timestampUs, InputEventType eventType) =>
        new(timestampUs, eventType, 100, 200, MouseButton.Left, 0);

    private static MouseInputEvent Wheel(long timestampUs) =>
        new(timestampUs, InputEventType.MouseVerticalWheel, 100, 200, MouseButton.None, 120);

    private sealed record EngineContext(
        PlaybackEngine Engine,
        FakePlaybackScheduler Scheduler,
        FakeInputInjector Injector);

    private class FakePlaybackScheduler(long initialTimestampUs = 0) : IPlaybackScheduler
    {
        public long CurrentTimestampUs { get; protected set; } = initialTimestampUs;

        public long LastPlaybackStartTimestampUs { get; private set; }

        public int GetTimestampCallCount { get; private set; }

        public List<long> RequestedTargets { get; } = [];

        public Queue<long> OvershootByWait { get; } = new();

        public Queue<long> AdvanceBeforeWait { get; } = new();

        public int? ThrowOnWaitIndex { get; set; }

        public Exception ExceptionToThrow { get; set; } = new InvalidOperationException("scheduler failed");

        public bool DoNotAdvance { get; set; }

        public long? FinalTimestampOverride { get; set; }

        public virtual long GetTimestampMicroseconds()
        {
            GetTimestampCallCount++;
            if (FinalTimestampOverride.HasValue && RequestedTargets.Count > 0)
            {
                return FinalTimestampOverride.Value;
            }

            return CurrentTimestampUs;
        }

        public virtual Task WaitUntilElapsedAsync(long playbackStartTimestampUs, long targetElapsedUs)
        {
            LastPlaybackStartTimestampUs = playbackStartTimestampUs;
            var waitIndex = RequestedTargets.Count;
            RequestedTargets.Add(targetElapsedUs);
            if (ThrowOnWaitIndex == waitIndex)
            {
                throw ExceptionToThrow;
            }

            if (AdvanceBeforeWait.TryDequeue(out var advanceUs))
            {
                CurrentTimestampUs = AddSaturating(CurrentTimestampUs, advanceUs);
            }

            if (!DoNotAdvance)
            {
                var currentElapsed = GetElapsed(CurrentTimestampUs, playbackStartTimestampUs);
                if (currentElapsed < targetElapsedUs)
                {
                    CurrentTimestampUs = AddSaturating(playbackStartTimestampUs, targetElapsedUs);
                }

                if (OvershootByWait.TryDequeue(out var overshootUs))
                {
                    CurrentTimestampUs = AddSaturating(CurrentTimestampUs, overshootUs);
                }
            }

            return Task.CompletedTask;
        }

        public virtual Task WaitUntilElapsedAsync(
            long playbackStartTimestampUs,
            long targetElapsedUs,
            CancellationToken cancellationToken) =>
            WaitUntilElapsedAsync(playbackStartTimestampUs, targetElapsedUs).WaitAsync(cancellationToken);

        public long GetCurrentElapsed(long playbackStartTimestampUs) =>
            GetElapsed(CurrentTimestampUs, playbackStartTimestampUs);

        private static long GetElapsed(long current, long start)
        {
            var elapsed = (Int128)current - start;
            if (elapsed <= 0)
            {
                return 0;
            }

            return elapsed >= long.MaxValue ? long.MaxValue : (long)elapsed;
        }

        private static long AddSaturating(long left, long right)
        {
            var sum = (Int128)left + right;
            if (sum > long.MaxValue)
            {
                return long.MaxValue;
            }

            return sum < long.MinValue ? long.MinValue : (long)sum;
        }
    }

    private sealed class FakeInputInjector(FakePlaybackScheduler scheduler) : IInputInjector
    {
        public List<InputEvent> InjectedEvents { get; } = [];

        public List<long> InjectionElapsedTimes { get; } = [];

        public int? ThrowOnIndex { get; set; }

        public Exception ExceptionToThrow { get; set; } = new InvalidOperationException("injection failed");

        public void Inject(InputEvent inputEvent)
        {
            if (ThrowOnIndex == InjectedEvents.Count)
            {
                throw ExceptionToThrow;
            }

            InjectedEvents.Add(inputEvent);
            InjectionElapsedTimes.Add(scheduler.GetCurrentElapsed(scheduler.LastPlaybackStartTimestampUs));
        }
    }

    private sealed class BlockingPlaybackScheduler : FakePlaybackScheduler
    {
        public TaskCompletionSource WaitEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task WaitUntilElapsedAsync(long playbackStartTimestampUs, long targetElapsedUs)
        {
            WaitEntered.TrySetResult();
            await Release.Task;
            await base.WaitUntilElapsedAsync(playbackStartTimestampUs, targetElapsedUs);
        }
    }
}
