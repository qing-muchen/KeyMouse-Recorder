using System.Collections.Immutable;
using MacroRecorder.Core.InputInjection;
using MacroRecorder.Core.Models;
using MacroRecorder.Core.Models.Input;
using MacroRecorder.Core.Playback;
using MacroRecorder.Core.Schema;

namespace MacroRecorder.Core.Tests.Playback;

public sealed class PlaybackStopTests
{
    [Fact]
    public async Task StopDuringWaitCancelsTimelineReleasesKeyAndRestoresIdle()
    {
        var scheduler = new StopTestScheduler();
        var injector = new StopTestInjector();
        var engine = new PlaybackEngine(scheduler, injector);
        var session = engine.StartPlayback(CreateMacro([KeyDown(0, 65, 30)], 1_000));
        await injector.WaitForCountAsync(1);
        await scheduler.WaitForRequestCountAsync(2);

        var result = await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(PlaybackCompletionReason.Stopped, result.CompletionReason);
        Assert.Equal(1, result.InjectedEventCount);
        Assert.Equal(1, result.ReleasedInputCount);
        Assert.Equal(PlaybackState.Idle, session.State);
        Assert.Equal(PlaybackState.Idle, engine.State);
        Assert.True(scheduler.CancellationCount > 0);
        Assert.Collection(
            injector.Events,
            input => Assert.Equal(InputEventType.KeyboardKeyDown, input.EventType),
            input => Assert.Equal(InputEventType.KeyboardKeyUp, input.EventType));
    }

    [Fact]
    public async Task StopPreventsEveryFutureMacroEvent()
    {
        var scheduler = new StopTestScheduler();
        var injector = new StopTestInjector();
        var engine = new PlaybackEngine(scheduler, injector);
        var events = ImmutableArray.Create<InputEvent>(
            KeyDown(0, 65, 30),
            KeyUp(500, 65, 30),
            MouseMove(1_000, 20, 30));
        var session = engine.StartPlayback(CreateMacro(events, 1_000));
        await injector.WaitForCountAsync(1);

        await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        scheduler.AdvanceBy(10_000);

        Assert.Equal(1, session.CurrentEventIndex);
        Assert.DoesNotContain(injector.Events, input => input is MouseInputEvent);
        Assert.DoesNotContain(
            injector.Events,
            input => input.TimestampUs == 500 && input.EventType == InputEventType.KeyboardKeyUp);
    }

    [Fact]
    public async Task StopWhilePausedIncludesCurrentPauseAndCannotResume()
    {
        var scheduler = new StopTestScheduler();
        var injector = new StopTestInjector();
        var engine = new PlaybackEngine(scheduler, injector);
        var session = engine.StartPlayback(CreateMacro([KeyDown(1_000, 65, 30)], 1_000));
        await scheduler.WaitForRequestCountAsync(1);
        scheduler.AdvanceBy(100);
        await session.PauseAsync();
        scheduler.AdvanceBy(400);

        var result = await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(400, result.TotalPausedDurationUs);
        Assert.Empty(injector.Events);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResumeAsync());
    }

    [Fact]
    public async Task ConcurrentStopCallsShareCompletionAndCompletedStopIsRejected()
    {
        var scheduler = new StopTestScheduler { BlockCancellation = true };
        var injector = new StopTestInjector();
        var engine = new PlaybackEngine(scheduler, injector);
        var session = engine.StartPlayback(CreateMacro([KeyDown(0, 65, 30)], 1_000));
        await injector.WaitForCountAsync(1);

        var firstStop = session.StopAsync();
        await scheduler.CancellationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondStop = session.StopAsync();

        Assert.Same(firstStop, secondStop);
        Assert.Equal(PlaybackState.Stopping, session.State);
        scheduler.AllowCancellation();
        await firstStop.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StopAsync());
    }

    [Fact]
    public async Task StopReleasesMultipleKeysInReversePressOrder()
    {
        var scheduler = new StopTestScheduler();
        var injector = new StopTestInjector();
        var engine = new PlaybackEngine(scheduler, injector);
        var events = ImmutableArray.Create<InputEvent>(
            KeyDown(0, 17, 29),
            KeyDown(0, 16, 42),
            KeyDown(0, 65, 30));
        var session = engine.StartPlayback(CreateMacro(events, 1_000));
        await injector.WaitForCountAsync(3);

        var result = await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(3, result.ReleasedInputCount);
        Assert.Equal(
            [17u, 16u, 65u, 65u, 16u, 17u],
            injector.Events.Cast<KeyboardInputEvent>().Select(input => input.VirtualKey));
        Assert.All(injector.Events.Skip(3), input => Assert.Equal(InputEventType.KeyboardKeyUp, input.EventType));
    }

    [Fact]
    public async Task RepeatedKeyDownRequiresOnlyOneCleanupRelease()
    {
        var scheduler = new StopTestScheduler();
        var injector = new StopTestInjector();
        var engine = new PlaybackEngine(scheduler, injector);
        var session = engine.StartPlayback(
            CreateMacro([KeyDown(0, 65, 30), KeyDown(0, 65, 30)], 1_000));
        await injector.WaitForCountAsync(2);

        var result = await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, result.ReleasedInputCount);
        Assert.Equal(3, injector.Events.Count);
    }

    [Fact]
    public async Task InputReleasedByMacroIsNotReleasedAgainOnStop()
    {
        var scheduler = new StopTestScheduler();
        var injector = new StopTestInjector();
        var engine = new PlaybackEngine(scheduler, injector);
        var events = ImmutableArray.Create<InputEvent>(
            KeyDown(0, 65, 30),
            KeyUp(0, 65, 30));
        var session = engine.StartPlayback(CreateMacro(events, 1_000));
        await injector.WaitForCountAsync(2);

        var result = await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, result.ReleasedInputCount);
        Assert.Equal(events, injector.Events);
    }

    [Fact]
    public async Task StopReleasesMouseButtonAtLatestKnownPosition()
    {
        var scheduler = new StopTestScheduler();
        var injector = new StopTestInjector();
        var engine = new PlaybackEngine(scheduler, injector);
        var events = ImmutableArray.Create<InputEvent>(
            MouseButtonDown(0, 10, 15, MouseButton.Left),
            MouseMove(0, 80, 90));
        var session = engine.StartPlayback(CreateMacro(events, 1_000));
        await injector.WaitForCountAsync(2);

        var result = await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, result.ReleasedInputCount);
        var release = Assert.IsType<MouseInputEvent>(injector.Events[^1]);
        Assert.Equal(InputEventType.MouseButtonUp, release.EventType);
        Assert.Equal(MouseButton.Left, release.Button);
        Assert.Equal(80, release.X);
        Assert.Equal(90, release.Y);
    }

    [Fact]
    public async Task StopReleasesMultipleMouseButtonsInReversePressOrder()
    {
        var scheduler = new StopTestScheduler();
        var injector = new StopTestInjector();
        var engine = new PlaybackEngine(scheduler, injector);
        var events = ImmutableArray.Create<InputEvent>(
            MouseButtonDown(0, 10, 15, MouseButton.Left),
            MouseButtonDown(0, 10, 15, MouseButton.XButton1));
        var session = engine.StartPlayback(CreateMacro(events, 1_000));
        await injector.WaitForCountAsync(2);

        var result = await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, result.ReleasedInputCount);
        Assert.Equal(
            [MouseButton.Left, MouseButton.XButton1, MouseButton.XButton1, MouseButton.Left],
            injector.Events.Cast<MouseInputEvent>().Select(input => input.Button));
    }

    [Fact]
    public async Task EmergencyStopInterruptsWaitAndReportsEmergencyReason()
    {
        var scheduler = new StopTestScheduler();
        var injector = new StopTestInjector();
        var engine = new PlaybackEngine(scheduler, injector);
        var session = engine.StartPlayback(CreateMacro([KeyDown(5_000, 65, 30)], 5_000));
        await scheduler.WaitForRequestCountAsync(1);

        var result = await session.EmergencyStopAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(PlaybackCompletionReason.EmergencyStopped, result.CompletionReason);
        Assert.Empty(injector.Events);
        Assert.Equal(PlaybackState.Idle, session.State);
    }

    [Fact]
    public async Task EmergencyStopWhilePausedReleasesHeldInput()
    {
        var scheduler = new StopTestScheduler();
        var injector = new StopTestInjector();
        var engine = new PlaybackEngine(scheduler, injector);
        var session = engine.StartPlayback(CreateMacro([KeyDown(0, 65, 30)], 5_000));
        await injector.WaitForCountAsync(1);
        await session.PauseAsync();

        var result = await session.EmergencyStopAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(PlaybackCompletionReason.EmergencyStopped, result.CompletionReason);
        Assert.Equal(1, result.ReleasedInputCount);
        Assert.Equal(InputEventType.KeyboardKeyUp, injector.Events[^1].EventType);
    }

    [Fact]
    public async Task EmergencyStopEscalatesAnInFlightNormalStop()
    {
        var scheduler = new StopTestScheduler { BlockCancellation = true };
        var injector = new StopTestInjector();
        var engine = new PlaybackEngine(scheduler, injector);
        var session = engine.StartPlayback(CreateMacro([KeyDown(0, 65, 30)], 5_000));
        await injector.WaitForCountAsync(1);

        var stop = session.StopAsync();
        await scheduler.CancellationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var emergencyStop = session.EmergencyStopAsync();
        scheduler.AllowCancellation();

        var result = await stop.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Same(stop, emergencyStop);
        Assert.Equal(PlaybackCompletionReason.EmergencyStopped, result.CompletionReason);
    }

    [Fact]
    public async Task CleanupFailureRestoresIdleAndPropagatesOriginalFailure()
    {
        var scheduler = new StopTestScheduler();
        var injector = new StopTestInjector
        {
            ThrowWhen = input => input.EventType == InputEventType.KeyboardKeyUp,
            ExceptionToThrow = new InputInjectionException(1, 0, 5),
        };
        var engine = new PlaybackEngine(scheduler, injector);
        var session = engine.StartPlayback(CreateMacro([KeyDown(0, 65, 30)], 5_000));
        await injector.WaitForCountAsync(1);

        await Assert.ThrowsAsync<InputInjectionException>(() => session.StopAsync());

        Assert.Equal(PlaybackState.Idle, session.State);
        Assert.Equal(PlaybackState.Idle, engine.State);
    }

    [Fact]
    public async Task StopControlFailureStillReleasesHeldInputAndRestoresIdle()
    {
        var scheduler = new StopTestScheduler();
        var injector = new StopTestInjector();
        var engine = new PlaybackEngine(scheduler, injector);
        var session = engine.StartPlayback(CreateMacro([KeyDown(0, 65, 30)], 5_000));
        await injector.WaitForCountAsync(1);
        await session.PauseAsync();
        scheduler.ThrowOnNextTimestampRead = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StopAsync());

        Assert.Equal(InputEventType.KeyboardKeyUp, injector.Events[^1].EventType);
        Assert.Equal(PlaybackState.Idle, session.State);
        Assert.Equal(PlaybackState.Idle, engine.State);
    }

    [Fact]
    public async Task MultipleCleanupFailuresAreAggregatedAfterEveryReleaseAttempt()
    {
        var scheduler = new StopTestScheduler();
        var injector = new StopTestInjector
        {
            ThrowWhen = input => input.EventType == InputEventType.KeyboardKeyUp,
        };
        var engine = new PlaybackEngine(scheduler, injector);
        var session = engine.StartPlayback(
            CreateMacro([KeyDown(0, 17, 29), KeyDown(0, 65, 30)], 5_000));
        await injector.WaitForCountAsync(2);

        var exception = await Assert.ThrowsAsync<AggregateException>(() => session.StopAsync());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Equal(2, injector.ReleaseAttemptCount);
        Assert.Equal(PlaybackState.Idle, session.State);
    }

    [Fact]
    public async Task ConcurrentStopAndResumeEndsIdleWithoutFurtherInjection()
    {
        var scheduler = new StopTestScheduler();
        var injector = new StopTestInjector();
        var engine = new PlaybackEngine(scheduler, injector);
        var session = engine.StartPlayback(CreateMacro([KeyDown(1_000, 65, 30)], 1_000));
        await scheduler.WaitForRequestCountAsync(1);
        await session.PauseAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var stop = StopWhenReleasedAsync(session, start.Task);
        var resume = ResumeWhenReleasedAsync(session, start.Task);
        start.SetResult();
        await Task.WhenAll(stop, resume).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(PlaybackCompletionReason.Stopped, (await stop).CompletionReason);
        Assert.Equal(PlaybackState.Idle, session.State);
        Assert.Empty(injector.Events);
    }

    [Fact]
    public async Task ConcurrentStopAndPauseEndsIdleWithoutFurtherInjection()
    {
        var scheduler = new StopTestScheduler();
        var injector = new StopTestInjector();
        var engine = new PlaybackEngine(scheduler, injector);
        var session = engine.StartPlayback(CreateMacro([KeyDown(1_000, 65, 30)], 1_000));
        await scheduler.WaitForRequestCountAsync(1);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var stop = StopWhenReleasedAsync(session, start.Task);
        var pause = PauseWhenReleasedAsync(session, start.Task);
        start.SetResult();
        await Task.WhenAll(stop, pause).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(PlaybackCompletionReason.Stopped, (await stop).CompletionReason);
        Assert.Equal(PlaybackState.Idle, session.State);
        Assert.Empty(injector.Events);
    }

    [Fact]
    public async Task StopDoesNotModifyMacroOrItsEvents()
    {
        var events = ImmutableArray.Create<InputEvent>(KeyDown(0, 65, 30), KeyUp(1_000, 65, 30));
        var macro = CreateMacro(events, 1_000);
        var snapshot = macro with { };
        var scheduler = new StopTestScheduler();
        var injector = new StopTestInjector();
        var session = new PlaybackEngine(scheduler, injector).StartPlayback(macro);
        await injector.WaitForCountAsync(1);

        await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(snapshot, macro);
        Assert.Equal(events, macro.Events);
    }

    [Fact]
    public async Task NaturalCompletionRetainsPhaseTenInjectionSemantics()
    {
        var scheduler = new StopTestScheduler();
        var injector = new StopTestInjector();
        var session = new PlaybackEngine(scheduler, injector)
            .StartPlayback(CreateMacro([KeyDown(10, 65, 30)], 10));
        await scheduler.WaitForRequestCountAsync(1);
        scheduler.AdvanceBy(10);

        var result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(PlaybackCompletionReason.Completed, result.CompletionReason);
        Assert.Equal(0, result.ReleasedInputCount);
        Assert.Single(injector.Events);
    }

    [Fact]
    public void PlaybackResultRejectsUndefinedReasonAndNegativeReleaseCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackResult(0, 0, 0, 1, 0, (PlaybackCompletionReason)99, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackResult(0, 0, 0, 1, 0, PlaybackCompletionReason.Stopped, -1));
    }

    private static async Task<PlaybackResult> StopWhenReleasedAsync(PlaybackSession session, Task start)
    {
        await start;
        return await session.StopAsync();
    }

    private static async Task ResumeWhenReleasedAsync(PlaybackSession session, Task start)
    {
        await start;
        try
        {
            await session.ResumeAsync();
        }
        catch (InvalidOperationException)
        {
            // Stop won the valid state transition race.
        }
    }

    private static async Task PauseWhenReleasedAsync(PlaybackSession session, Task start)
    {
        await start;
        try
        {
            await session.PauseAsync();
        }
        catch (InvalidOperationException)
        {
            // Stop won the valid state transition race.
        }
    }

    private static Macro CreateMacro(ImmutableArray<InputEvent> events, long durationUs) => new()
    {
        SchemaVersion = MacroSchema.CurrentVersion,
        Id = Guid.Parse("fd198e7d-a065-4745-85b8-00f77ccce8fe"),
        Name = "Stop test",
        Description = "Synthetic input only",
        CreatedAt = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero),
        Recording = new RecordingMetadata(durationUs, events.Length),
        Environment = new EnvironmentMetadata(1920, 1080, 1.0),
        Playback = new PlaybackMetadata(1.0),
        Events = events,
    };

    private static KeyboardInputEvent KeyDown(long timestampUs, uint virtualKey, uint scanCode) =>
        new(timestampUs, InputEventType.KeyboardKeyDown, virtualKey, scanCode, 0);

    private static KeyboardInputEvent KeyUp(long timestampUs, uint virtualKey, uint scanCode) =>
        new(timestampUs, InputEventType.KeyboardKeyUp, virtualKey, scanCode, 0);

    private static MouseInputEvent MouseMove(long timestampUs, int x, int y) =>
        new(timestampUs, InputEventType.MouseMove, x, y, MouseButton.None, 0);

    private static MouseInputEvent MouseButtonDown(
        long timestampUs,
        int x,
        int y,
        MouseButton button) =>
        new(timestampUs, InputEventType.MouseButtonDown, x, y, button, 0);

    private sealed class StopTestScheduler : IPlaybackScheduler
    {
        private readonly object sync = new();
        private readonly List<PendingWait> pendingWaits = [];
        private readonly List<long> requestedTargets = [];
        private TaskCompletionSource requestChanged = CreateSignal();
        private long currentTimestampUs;
        private int cancellationCount;

        public int CancellationCount => Volatile.Read(ref cancellationCount);

        public bool BlockCancellation { get; init; }

        public bool ThrowOnNextTimestampRead { get; set; }

        public TaskCompletionSource CancellationStarted { get; } = CreateSignal();

        private TaskCompletionSource CancellationAllowed { get; } = CreateSignal();

        public long GetTimestampMicroseconds()
        {
            lock (sync)
            {
                if (ThrowOnNextTimestampRead)
                {
                    ThrowOnNextTimestampRead = false;
                    throw new InvalidOperationException("Synthetic timestamp failure.");
                }

                return currentTimestampUs;
            }
        }

        public Task WaitUntilElapsedAsync(long playbackStartTimestampUs, long targetElapsedUs)
        {
            lock (sync)
            {
                var completion = CreateSignal();
                requestedTargets.Add(targetElapsedUs);
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

        public async Task WaitUntilElapsedAsync(
            long playbackStartTimestampUs,
            long targetElapsedUs,
            CancellationToken cancellationToken)
        {
            try
            {
                await WaitUntilElapsedAsync(playbackStartTimestampUs, targetElapsedUs)
                    .WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref cancellationCount);
                if (BlockCancellation)
                {
                    CancellationStarted.TrySetResult();
                    await CancellationAllowed.Task;
                }

                throw;
            }
        }

        public void AllowCancellation() => CancellationAllowed.TrySetResult();

        public void AdvanceBy(long elapsedUs)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(elapsedUs);
            lock (sync)
            {
                currentTimestampUs += elapsedUs;
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
                    if (requestedTargets.Count >= expectedCount)
                    {
                        return;
                    }

                    changed = requestChanged.Task;
                }

                await changed.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }

        private static long GetElapsed(long current, long start) => Math.Max(0, current - start);

        private static TaskCompletionSource CreateSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed record PendingWait(
            long PlaybackStartTimestampUs,
            long TargetElapsedUs,
            TaskCompletionSource Completion);
    }

    private sealed class StopTestInjector : IInputInjector
    {
        private readonly object sync = new();
        private TaskCompletionSource injected = CreateSignal();

        public List<InputEvent> Events { get; } = [];

        public Func<InputEvent, bool>? ThrowWhen { get; init; }

        public Exception ExceptionToThrow { get; init; } = new InvalidOperationException("Synthetic cleanup failure.");

        public int ReleaseAttemptCount { get; private set; }

        public void Inject(InputEvent inputEvent)
        {
            var isRelease = inputEvent.EventType is
                InputEventType.KeyboardKeyUp or InputEventType.MouseButtonUp;
            if (isRelease)
            {
                ReleaseAttemptCount++;
            }

            if (ThrowWhen?.Invoke(inputEvent) == true)
            {
                throw ExceptionToThrow;
            }

            lock (sync)
            {
                Events.Add(inputEvent);
                var previousSignal = injected;
                injected = CreateSignal();
                previousSignal.TrySetResult();
            }
        }

        public async Task WaitForCountAsync(int expectedCount)
        {
            while (true)
            {
                Task changed;
                lock (sync)
                {
                    if (Events.Count >= expectedCount)
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
