using System.Collections.Immutable;
using MacroRecorder.Core.InputInjection;
using MacroRecorder.Core.Models;
using MacroRecorder.Core.Models.Input;
using MacroRecorder.Core.Playback;
using MacroRecorder.Core.Schema;

namespace MacroRecorder.Core.Tests.Playback;

public sealed class PlaybackSessionTests
{
    [Fact]
    public async Task StartPlaybackReturnsPlayingSessionAndCompletionReturnsIdle()
    {
        var (engine, scheduler, injector) = CreateEngine();

        var session = engine.StartPlayback(CreateMacro([KeyDown(100)], 100));

        Assert.Equal(PlaybackState.Playing, session.State);
        Assert.Equal(PlaybackState.Playing, engine.State);
        Assert.Equal(0, session.CurrentEventIndex);
        await scheduler.WaitForRequestCountAsync(1);
        scheduler.AdvanceBy(100);

        var result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, result.InjectedEventCount);
        Assert.Equal(1, session.CurrentEventIndex);
        Assert.Equal(PlaybackState.Idle, session.State);
        Assert.Equal(PlaybackState.Idle, engine.State);
        Assert.Single(injector.InjectedEvents);
    }

    [Fact]
    public async Task PauseFreezesTimelineUntilResumeAndPreservesRemainingTime()
    {
        var events = ImmutableArray.Create<InputEvent>(KeyDown(0), KeyUp(500));
        var (engine, scheduler, injector) = CreateEngine();
        var session = engine.StartPlayback(CreateMacro(events, 500));
        await injector.WaitForInjectedCountAsync(1);
        await scheduler.WaitForRequestCountAsync(2);
        scheduler.AdvanceBy(200);

        await session.PauseAsync();

        Assert.Equal(PlaybackState.Paused, session.State);
        Assert.Equal(PlaybackState.Paused, engine.State);
        scheduler.AdvanceBy(1_000);
        Assert.Single(injector.InjectedEvents);
        Assert.False(session.Completion.IsCompleted);

        await session.ResumeAsync();

        Assert.Equal(PlaybackState.Playing, session.State);
        await scheduler.WaitForRequestCountAsync(3);
        Assert.Equal([0, 500, 1_500], scheduler.RequestedTargets);
        scheduler.AdvanceBy(299);
        Assert.Single(injector.InjectedEvents);
        scheduler.AdvanceBy(1);

        var result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(events, injector.InjectedEvents);
        Assert.Equal(500, result.ScheduledDurationUs);
        Assert.Equal(1_500, result.ActualElapsedUs);
        Assert.Equal(1_000, result.TotalPausedDurationUs);
    }

    [Fact]
    public async Task RepeatedPauseIsIdempotentAndDoesNotResetPauseStart()
    {
        var (engine, scheduler, injector) = CreateEngine();
        var session = engine.StartPlayback(CreateMacro([KeyDown(500)], 500));
        await scheduler.WaitForRequestCountAsync(1);
        scheduler.AdvanceBy(100);

        await session.PauseAsync();
        scheduler.AdvanceBy(250);
        await session.PauseAsync();
        scheduler.AdvanceBy(250);

        Assert.Equal(500, session.TotalPausedDurationUs);
        Assert.Empty(injector.InjectedEvents);
        await session.ResumeAsync();
        await scheduler.WaitForRequestCountAsync(2);
        Assert.Equal(1_000, scheduler.RequestedTargets[^1]);
        scheduler.AdvanceBy(400);

        var result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(500, result.TotalPausedDurationUs);
    }

    [Fact]
    public async Task SeparatePauseCyclesAccumulateTheirDurations()
    {
        var (engine, scheduler, _) = CreateEngine();
        var session = engine.StartPlayback(CreateMacro([KeyDown(500)], 500));
        await scheduler.WaitForRequestCountAsync(1);
        scheduler.AdvanceBy(100);

        await session.PauseAsync();
        scheduler.AdvanceBy(100);
        await session.ResumeAsync();
        await session.PauseAsync();
        scheduler.AdvanceBy(200);
        await session.ResumeAsync();
        scheduler.AdvanceBy(100);
        await scheduler.WaitForRequestCountAsync(2);

        Assert.Equal(800, scheduler.RequestedTargets[^1]);
        scheduler.AdvanceBy(300);
        var result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(300, result.TotalPausedDurationUs);
        Assert.Equal(800, result.ActualElapsedUs);
    }

    [Fact]
    public async Task ResumeWhilePlayingIsIdempotent()
    {
        var (engine, scheduler, _) = CreateEngine();
        var session = engine.StartPlayback(CreateMacro([KeyDown(100)], 100));
        await scheduler.WaitForRequestCountAsync(1);

        await session.ResumeAsync();
        await session.ResumeAsync();

        Assert.Equal(PlaybackState.Playing, session.State);
        Assert.Equal(0, session.TotalPausedDurationUs);
        scheduler.AdvanceBy(100);
        var result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, result.TotalPausedDurationUs);
    }

    [Fact]
    public async Task SpeedScalingAndPauseFreezeUseTheSameActiveTimeline()
    {
        var (engine, scheduler, injector) = CreateEngine();
        var session = engine.StartPlayback(CreateMacro([KeyDown(1_000)], 1_000), 2.0);
        await scheduler.WaitForRequestCountAsync(1);
        Assert.Equal(500, scheduler.RequestedTargets[0]);
        scheduler.AdvanceBy(200);

        await session.PauseAsync();
        scheduler.AdvanceBy(1_000);
        await session.ResumeAsync();
        await scheduler.WaitForRequestCountAsync(2);

        Assert.Equal(1_500, scheduler.RequestedTargets[1]);
        scheduler.AdvanceBy(299);
        Assert.Empty(injector.InjectedEvents);
        scheduler.AdvanceBy(1);
        var result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(500, result.ScheduledDurationUs);
        Assert.Equal(1_500, result.ActualElapsedUs);
        Assert.Equal(1_000, result.TotalPausedDurationUs);
        Assert.Equal(2.0, result.PlaybackSpeed);
    }

    [Fact]
    public async Task CompletedSessionRejectsPauseAndResume()
    {
        var (engine, scheduler, _) = CreateEngine();
        var session = engine.StartPlayback(CreateMacro([KeyDown(1)], 1));
        await scheduler.WaitForRequestCountAsync(1);
        scheduler.AdvanceBy(1);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.PauseAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResumeAsync());
    }

    [Fact]
    public async Task ConcurrentPauseAndResumeCallsRemainAtomic()
    {
        var (engine, scheduler, _) = CreateEngine();
        var session = engine.StartPlayback(CreateMacro([KeyDown(1_000)], 1_000));
        await scheduler.WaitForRequestCountAsync(1);
        scheduler.AdvanceBy(100);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var controls = Enumerable.Range(0, 32)
            .Select(index => ControlWhenReleasedAsync(session, start.Task, pause: index % 2 == 0))
            .ToArray();
        start.SetResult();
        await Task.WhenAll(controls).WaitAsync(TimeSpan.FromSeconds(2));
        await session.ResumeAsync();

        Assert.Equal(PlaybackState.Playing, session.State);
        Assert.InRange(session.TotalPausedDurationUs, 0, scheduler.CurrentTimestampUs);
        scheduler.AdvanceBy(1_000);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(PlaybackState.Idle, session.State);
    }

    [Fact]
    public async Task EngineRejectsSecondPlaybackWhileSessionIsPaused()
    {
        var macro = CreateMacro([KeyDown(100)], 100);
        var (engine, scheduler, _) = CreateEngine();
        var session = engine.StartPlayback(macro);
        await scheduler.WaitForRequestCountAsync(1);
        await session.PauseAsync();

        Assert.Throws<InvalidOperationException>(() => engine.StartPlayback(macro));
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.PlayAsync(macro));

        await session.ResumeAsync();
        scheduler.AdvanceBy(100);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task PauseClockFailureLeavesSessionPlaying()
    {
        var (engine, scheduler, _) = CreateEngine();
        var session = engine.StartPlayback(CreateMacro([KeyDown(100)], 100));
        await scheduler.WaitForRequestCountAsync(1);
        scheduler.ThrowOnNextTimestampRead = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.PauseAsync());

        Assert.Equal(PlaybackState.Playing, session.State);
        scheduler.AdvanceBy(100);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ResumeClockFailureReopensGateAndDoesNotLeaveSessionPaused()
    {
        var (engine, scheduler, _) = CreateEngine();
        var session = engine.StartPlayback(CreateMacro([KeyDown(100)], 100));
        await scheduler.WaitForRequestCountAsync(1);
        await session.PauseAsync();
        scheduler.ThrowOnNextTimestampRead = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResumeAsync());

        Assert.Equal(PlaybackState.Playing, session.State);
        scheduler.AdvanceBy(100);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task InjectorFailureAfterResumeRestoresSessionAndEngineToIdle()
    {
        var (engine, scheduler, injector) = CreateEngine();
        injector.ExceptionToThrow = new InputInjectionException(1, 0, 5);
        injector.ThrowOnIndex = 0;
        var session = engine.StartPlayback(CreateMacro([KeyDown(100)], 100));
        await scheduler.WaitForRequestCountAsync(1);
        await session.PauseAsync();
        scheduler.AdvanceBy(500);
        await session.ResumeAsync();
        await scheduler.WaitForRequestCountAsync(2);
        scheduler.AdvanceBy(100);

        await Assert.ThrowsAsync<InputInjectionException>(() => session.Completion);
        Assert.Equal(PlaybackState.Idle, session.State);
        Assert.Equal(PlaybackState.Idle, engine.State);
    }

    [Fact]
    public async Task PauseAndResumeDoNotModifyMacro()
    {
        var events = ImmutableArray.Create<InputEvent>(KeyDown(100), KeyUp(200));
        var macro = CreateMacro(events, 200);
        var snapshot = macro with { };
        var (engine, scheduler, _) = CreateEngine();
        var session = engine.StartPlayback(macro);
        await scheduler.WaitForRequestCountAsync(1);
        await session.PauseAsync();
        scheduler.AdvanceBy(50);
        await session.ResumeAsync();
        scheduler.AdvanceBy(250);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(snapshot, macro);
        Assert.Equal(events, macro.Events);
    }

    [Fact]
    public void PlaybackResultRejectsNegativePausedDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackResult(0, 0, 0, 1.0, -1));
    }

    [Fact]
    public void BackwardCompatiblePlaybackResultConstructorsUseZeroPausedDuration()
    {
        Assert.Equal(0, new PlaybackResult(0, 0, 0).TotalPausedDurationUs);
        Assert.Equal(0, new PlaybackResult(0, 0, 0, 2.0).TotalPausedDurationUs);
    }

    private static async Task ControlWhenReleasedAsync(
        PlaybackSession session,
        Task release,
        bool pause)
    {
        await release;
        if (pause)
        {
            await session.PauseAsync();
        }
        else
        {
            await session.ResumeAsync();
        }
    }

    private static SessionTestContext CreateEngine()
    {
        var scheduler = new ManualPlaybackScheduler();
        var injector = new ObservingInputInjector();
        return new SessionTestContext(new PlaybackEngine(scheduler, injector), scheduler, injector);
    }

    private static Macro CreateMacro(ImmutableArray<InputEvent> events, long durationUs) => new()
    {
        SchemaVersion = MacroSchema.CurrentVersion,
        Id = Guid.Parse("c50791b9-6728-41f3-b76a-bc3e77edb8ec"),
        Name = "Pause and resume test",
        Description = "Synthetic events only",
        CreatedAt = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero),
        Recording = new RecordingMetadata(durationUs, events.Length),
        Environment = new EnvironmentMetadata(1920, 1080, 1.0),
        Playback = new PlaybackMetadata(1.0),
        Events = events,
    };

    private static KeyboardInputEvent KeyDown(long timestampUs) =>
        new(timestampUs, InputEventType.KeyboardKeyDown, 65, 30, 0);

    private static KeyboardInputEvent KeyUp(long timestampUs) =>
        new(timestampUs, InputEventType.KeyboardKeyUp, 65, 30, 0);

    private sealed record SessionTestContext(
        PlaybackEngine Engine,
        ManualPlaybackScheduler Scheduler,
        ObservingInputInjector Injector);

    private sealed class ManualPlaybackScheduler : IPlaybackScheduler
    {
        private readonly object sync = new();
        private readonly List<PendingWait> pendingWaits = [];
        private TaskCompletionSource requestChanged = CreateSignal();
        private long currentTimestampUs;

        public List<long> RequestedTargets { get; } = [];

        public long CurrentTimestampUs
        {
            get
            {
                lock (sync)
                {
                    return currentTimestampUs;
                }
            }
        }

        public bool ThrowOnNextTimestampRead { get; set; }

        public long GetTimestampMicroseconds()
        {
            lock (sync)
            {
                if (ThrowOnNextTimestampRead)
                {
                    ThrowOnNextTimestampRead = false;
                    throw new InvalidOperationException("Synthetic clock failure.");
                }

                return currentTimestampUs;
            }
        }

        public Task WaitUntilElapsedAsync(long playbackStartTimestampUs, long targetElapsedUs)
        {
            lock (sync)
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                RequestedTargets.Add(targetElapsedUs);
                var previousSignal = requestChanged;
                requestChanged = CreateSignal();
                previousSignal.TrySetResult();

                if (GetElapsed(currentTimestampUs, playbackStartTimestampUs) >= targetElapsedUs)
                {
                    completion.SetResult();
                }
                else
                {
                    pendingWaits.Add(new PendingWait(playbackStartTimestampUs, targetElapsedUs, completion));
                }

                return completion.Task;
            }
        }

        public void AdvanceBy(long elapsedUs)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(elapsedUs);

            lock (sync)
            {
                currentTimestampUs = AddSaturating(currentTimestampUs, elapsedUs);
                for (var index = pendingWaits.Count - 1; index >= 0; index--)
                {
                    var pending = pendingWaits[index];
                    if (GetElapsed(currentTimestampUs, pending.PlaybackStartTimestampUs) >= pending.TargetElapsedUs)
                    {
                        pendingWaits.RemoveAt(index);
                        pending.Completion.TrySetResult();
                    }
                }
            }
        }

        public async Task WaitForRequestCountAsync(int expectedCount)
        {
            while (true)
            {
                Task changed;
                lock (sync)
                {
                    if (RequestedTargets.Count >= expectedCount)
                    {
                        return;
                    }

                    changed = requestChanged.Task;
                }

                await changed.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }

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
            return sum >= long.MaxValue ? long.MaxValue : (long)sum;
        }

        private static TaskCompletionSource CreateSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed record PendingWait(
            long PlaybackStartTimestampUs,
            long TargetElapsedUs,
            TaskCompletionSource Completion);
    }

    private sealed class ObservingInputInjector : IInputInjector
    {
        private readonly object sync = new();
        private TaskCompletionSource injected = CreateSignal();

        public List<InputEvent> InjectedEvents { get; } = [];

        public int? ThrowOnIndex { get; set; }

        public Exception ExceptionToThrow { get; set; } = new InvalidOperationException("Synthetic injection failure.");

        public void Inject(InputEvent inputEvent)
        {
            lock (sync)
            {
                if (ThrowOnIndex == InjectedEvents.Count)
                {
                    throw ExceptionToThrow;
                }

                InjectedEvents.Add(inputEvent);
                var previousSignal = injected;
                injected = CreateSignal();
                previousSignal.TrySetResult();
            }
        }

        public async Task WaitForInjectedCountAsync(int expectedCount)
        {
            while (true)
            {
                Task changed;
                lock (sync)
                {
                    if (InjectedEvents.Count >= expectedCount)
                    {
                        return;
                    }

                    changed = injected.Task;
                }

                await changed.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }

        private static TaskCompletionSource CreateSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
