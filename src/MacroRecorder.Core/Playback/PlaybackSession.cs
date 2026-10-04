using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using MacroRecorder.Core.InputInjection;
using MacroRecorder.Core.Models;
using MacroRecorder.Core.Models.Input;

namespace MacroRecorder.Core.Playback;

/// <summary>Owns the runtime state, cancellation, and pressed-input state for one Macro playback.</summary>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The session owns and disposes its cancellation source when its mandatory completion task finishes.")]
public sealed class PlaybackSession
{
    private const double DefaultPlaybackSpeed = 1.0;
    private const double LongUpperExclusive = 9_223_372_036_854_775_808d;

    private readonly object stateSync = new();
    private readonly Macro macro;
    private readonly double playbackSpeed;
    private readonly IPlaybackScheduler scheduler;
    private readonly IInputInjector inputInjector;
    private readonly Action<PlaybackSession> completionCallback;
    private readonly long[] scaledEventTargetsUs;
    private readonly long scaledDurationUs;
    private readonly long playbackStartTimestampUs;
    private readonly CancellationTokenSource stopCancellation = new();
    private readonly PressedInputTracker pressedInputs = new();
    private readonly List<Exception> controlFailures = [];
    private TaskCompletionSource resumeSignal = CreateResumeSignal();
    private Task<PlaybackResult>? completion;
    private PlaybackState state = PlaybackState.Playing;
    private PlaybackCompletionReason requestedCompletionReason = PlaybackCompletionReason.Completed;
    private int currentEventIndex;
    private long totalPausedDurationUs;
    private long pauseStartedTimestampUs;
    private bool stopRequested;

    internal PlaybackSession(
        Macro macro,
        double playbackSpeed,
        IPlaybackScheduler scheduler,
        IInputInjector inputInjector,
        Action<PlaybackSession> completionCallback)
    {
        this.macro = macro;
        this.playbackSpeed = playbackSpeed;
        this.scheduler = scheduler;
        this.inputInjector = inputInjector;
        this.completionCallback = completionCallback;
        scaledEventTargetsUs = ScaleTimeline(macro, playbackSpeed);
        scaledDurationUs = ScaleTimestamp(macro.Recording.DurationUs, playbackSpeed);
        playbackStartTimestampUs = scheduler.GetTimestampMicroseconds();
    }

    public PlaybackState State
    {
        get
        {
            lock (stateSync)
            {
                return state;
            }
        }
    }

    public int CurrentEventIndex
    {
        get
        {
            lock (stateSync)
            {
                return currentEventIndex;
            }
        }
    }

    public double PlaybackSpeed => playbackSpeed;

    public long TotalPausedDurationUs
    {
        get
        {
            lock (stateSync)
            {
                return GetTotalPausedDurationLocked(scheduler.GetTimestampMicroseconds());
            }
        }
    }

    public Task<PlaybackResult> Completion
    {
        get
        {
            lock (stateSync)
            {
                return completion ??
                    throw new InvalidOperationException("The playback session has not started.");
            }
        }
    }

    public Task PauseAsync()
    {
        lock (stateSync)
        {
            if (state is PlaybackState.Idle or PlaybackState.Stopping)
            {
                throw new InvalidOperationException("A stopping or completed playback session cannot be paused.");
            }

            if (state == PlaybackState.Paused)
            {
                return Task.CompletedTask;
            }

            pauseStartedTimestampUs = scheduler.GetTimestampMicroseconds();
            resumeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            state = PlaybackState.Paused;
            return Task.CompletedTask;
        }
    }

    public Task ResumeAsync()
    {
        lock (stateSync)
        {
            if (state is PlaybackState.Idle or PlaybackState.Stopping)
            {
                throw new InvalidOperationException("A stopping or completed playback session cannot be resumed.");
            }

            if (state == PlaybackState.Playing)
            {
                return Task.CompletedTask;
            }

            try
            {
                var resumeTimestampUs = scheduler.GetTimestampMicroseconds();
                totalPausedDurationUs = AddSaturating(
                    totalPausedDurationUs,
                    GetSafeElapsed(resumeTimestampUs, pauseStartedTimestampUs));
                pauseStartedTimestampUs = 0;
            }
            finally
            {
                state = PlaybackState.Playing;
                resumeSignal.TrySetResult();
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>Ends playback, releases held inputs, and returns the session completion.</summary>
    public Task<PlaybackResult> StopAsync() => RequestStop(PlaybackCompletionReason.Stopped);

    /// <summary>Immediately interrupts playback and prioritizes input-state cleanup.</summary>
    public Task<PlaybackResult> EmergencyStopAsync() =>
        RequestStop(PlaybackCompletionReason.EmergencyStopped);

    internal void Start()
    {
        lock (stateSync)
        {
            if (completion is not null)
            {
                throw new InvalidOperationException("The playback session has already started.");
            }

            completion = RunAsync();
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Playback and cleanup failures must be combined after every held input has been released.")]
    private async Task<PlaybackResult> RunAsync()
    {
        await Task.Yield();

        Exception? playbackFailure = null;
        try
        {
            for (var index = 0; index < macro.Events.Length; index++)
            {
                await InjectWhenDueAsync(macro.Events[index], scaledEventTargetsUs[index])
                    .ConfigureAwait(false);
            }

            await WaitForCompletionDueAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopCancellation.IsCancellationRequested)
        {
            // Stop and emergency stop are successful terminal paths, not playback failures.
        }
        catch (Exception exception)
        {
            playbackFailure = exception;
        }

        TransitionToStoppingForFinalization();
        var cleanup = ShouldReleasePressedInputs()
            ? pressedInputs.ReleaseAll(inputInjector)
            : pressedInputs.Clear();
        var failures = CollectFailures(playbackFailure, cleanup.Failures);
        PlaybackResult? result = null;

        try
        {
            result = CreateResult(cleanup.ReleasedInputCount);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        finally
        {
            TransitionToIdle();
            stopCancellation.Dispose();
            completionCallback(this);
        }

        ThrowIfFailed(failures);
        return result ?? throw new InvalidOperationException("Playback completed without producing a result.");
    }

    private async Task InjectWhenDueAsync(InputEvent inputEvent, long targetElapsedUs)
    {
        while (true)
        {
            await WaitUntilTimelineAsync(targetElapsedUs).ConfigureAwait(false);

            lock (stateSync)
            {
                stopCancellation.Token.ThrowIfCancellationRequested();
                if (state == PlaybackState.Paused ||
                    GetActiveElapsedLocked(scheduler.GetTimestampMicroseconds()) < targetElapsedUs)
                {
                    continue;
                }

                inputInjector.Inject(inputEvent);
                pressedInputs.Track(inputEvent);
                currentEventIndex++;
                return;
            }
        }
    }

    private async Task WaitForCompletionDueAsync()
    {
        while (true)
        {
            await WaitUntilTimelineAsync(scaledDurationUs).ConfigureAwait(false);

            lock (stateSync)
            {
                stopCancellation.Token.ThrowIfCancellationRequested();
                var completionTimestampUs = scheduler.GetTimestampMicroseconds();
                if (state == PlaybackState.Paused ||
                    GetActiveElapsedLocked(completionTimestampUs) < scaledDurationUs)
                {
                    continue;
                }

                return;
            }
        }
    }

    private async Task WaitUntilTimelineAsync(long targetElapsedUs)
    {
        while (true)
        {
            stopCancellation.Token.ThrowIfCancellationRequested();
            Task? resumeTask;
            long schedulerTargetUs;

            lock (stateSync)
            {
                stopCancellation.Token.ThrowIfCancellationRequested();
                if (state == PlaybackState.Paused)
                {
                    resumeTask = resumeSignal.Task;
                    schedulerTargetUs = 0;
                }
                else
                {
                    resumeTask = null;
                    schedulerTargetUs = GetSchedulerTarget(targetElapsedUs);
                }
            }

            if (resumeTask is not null)
            {
                await resumeTask.WaitAsync(stopCancellation.Token).ConfigureAwait(false);
                continue;
            }

            await scheduler
                .WaitUntilElapsedAsync(
                    playbackStartTimestampUs,
                    schedulerTargetUs,
                    stopCancellation.Token)
                .ConfigureAwait(false);

            lock (stateSync)
            {
                stopCancellation.Token.ThrowIfCancellationRequested();
                if (state == PlaybackState.Playing &&
                    GetActiveElapsedLocked(scheduler.GetTimestampMicroseconds()) >= targetElapsedUs)
                {
                    return;
                }
            }
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A stop must still cancel playback and run cleanup when pause accounting fails.")]
    private Task<PlaybackResult> RequestStop(PlaybackCompletionReason completionReason)
    {
        Task<PlaybackResult> completionTask;
        var shouldSignalCancellation = false;

        lock (stateSync)
        {
            if (state == PlaybackState.Idle)
            {
                throw new InvalidOperationException("A completed playback session cannot be stopped.");
            }

            completionTask = completion ??
                throw new InvalidOperationException("The playback session has not started.");

            if (state == PlaybackState.Stopping)
            {
                if (stopRequested && completionReason == PlaybackCompletionReason.EmergencyStopped)
                {
                    requestedCompletionReason = completionReason;
                }

                return completionTask;
            }

            stopRequested = true;
            requestedCompletionReason = completionReason;

            if (state == PlaybackState.Paused)
            {
                try
                {
                    totalPausedDurationUs = AddSaturating(
                        totalPausedDurationUs,
                        GetSafeElapsed(scheduler.GetTimestampMicroseconds(), pauseStartedTimestampUs));
                    pauseStartedTimestampUs = 0;
                }
                catch (Exception exception)
                {
                    controlFailures.Add(exception);
                }
            }

            state = PlaybackState.Stopping;
            resumeSignal.TrySetResult();
            shouldSignalCancellation = true;
        }

        if (shouldSignalCancellation)
        {
            SignalStopCancellation();
        }

        return completionTask;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Cancellation callback failures must not prevent playback cleanup.")]
    private void SignalStopCancellation()
    {
        try
        {
            stopCancellation.Cancel();
        }
        catch (Exception exception)
        {
            lock (stateSync)
            {
                controlFailures.Add(exception);
            }
        }
    }

    private PlaybackResult CreateResult(int releasedInputCount)
    {
        lock (stateSync)
        {
            return new PlaybackResult(
                currentEventIndex,
                scaledDurationUs,
                GetSafeElapsed(scheduler.GetTimestampMicroseconds(), playbackStartTimestampUs),
                playbackSpeed,
                totalPausedDurationUs,
                requestedCompletionReason,
                releasedInputCount);
        }
    }

    private List<Exception> CollectFailures(
        Exception? playbackFailure,
        IReadOnlyCollection<Exception> cleanupFailures)
    {
        List<Exception> failures = [];
        if (playbackFailure is not null)
        {
            failures.Add(playbackFailure);
        }

        failures.AddRange(cleanupFailures);
        lock (stateSync)
        {
            failures.AddRange(controlFailures);
        }

        return failures;
    }

    private void TransitionToStoppingForFinalization()
    {
        lock (stateSync)
        {
            state = PlaybackState.Stopping;
            resumeSignal.TrySetResult();
        }
    }

    private bool ShouldReleasePressedInputs()
    {
        lock (stateSync)
        {
            return stopRequested;
        }
    }

    private void TransitionToIdle()
    {
        lock (stateSync)
        {
            state = PlaybackState.Idle;
            resumeSignal.TrySetResult();
        }
    }

    private long GetSchedulerTarget(long targetElapsedUs)
    {
        var adjustedTarget = (Int128)targetElapsedUs + totalPausedDurationUs;
        if (adjustedTarget > long.MaxValue)
        {
            throw new PlaybackTimingException(targetElapsedUs, playbackSpeed);
        }

        return (long)adjustedTarget;
    }

    private long GetActiveElapsedLocked(long currentTimestampUs)
    {
        var wallElapsed = (Int128)currentTimestampUs - playbackStartTimestampUs;
        var pausedDuration = GetTotalPausedDurationLocked(currentTimestampUs);
        var activeElapsed = wallElapsed - pausedDuration;
        if (activeElapsed <= 0)
        {
            return 0;
        }

        return activeElapsed >= long.MaxValue ? long.MaxValue : (long)activeElapsed;
    }

    private long GetTotalPausedDurationLocked(long currentTimestampUs)
    {
        if (state != PlaybackState.Paused)
        {
            return totalPausedDurationUs;
        }

        return AddSaturating(
            totalPausedDurationUs,
            GetSafeElapsed(currentTimestampUs, pauseStartedTimestampUs));
    }

    private static long[] ScaleTimeline(Macro macro, double speed)
    {
        var scaledTargetsUs = new long[macro.Events.Length];
        for (var index = 0; index < macro.Events.Length; index++)
        {
            scaledTargetsUs[index] = ScaleTimestamp(macro.Events[index].TimestampUs, speed);
        }

        return scaledTargetsUs;
    }

    private static long ScaleTimestamp(long timestampUs, double speed)
    {
        if (timestampUs == 0 || speed == DefaultPlaybackSpeed)
        {
            return timestampUs;
        }

        var scaledTimestampUs = timestampUs / speed;
        if (!double.IsFinite(scaledTimestampUs) || scaledTimestampUs >= LongUpperExclusive)
        {
            throw new PlaybackTimingException(timestampUs, speed);
        }

        return checked((long)Math.Round(scaledTimestampUs, MidpointRounding.AwayFromZero));
    }

    private static long GetSafeElapsed(long currentTimestampUs, long startTimestampUs)
    {
        var elapsed = (Int128)currentTimestampUs - startTimestampUs;
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

    [DoesNotReturn]
    private static void ThrowFailure(Exception failure)
    {
        ExceptionDispatchInfo.Capture(failure).Throw();
        throw new InvalidOperationException("Unreachable code.");
    }

    private static void ThrowIfFailed(List<Exception> failures)
    {
        if (failures.Count == 1)
        {
            ThrowFailure(failures.First());
        }

        if (failures.Count > 1)
        {
            throw new AggregateException("Playback and input cleanup encountered multiple failures.", failures);
        }
    }

    private static TaskCompletionSource CreateResumeSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }

}
