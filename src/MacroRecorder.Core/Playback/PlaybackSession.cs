using MacroRecorder.Core.InputInjection;
using MacroRecorder.Core.Models;
using MacroRecorder.Core.Models.Input;

namespace MacroRecorder.Core.Playback;

/// <summary>Owns the runtime state and pause gate for one Macro playback.</summary>
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
    private TaskCompletionSource resumeSignal = CreateResumeSignal();
    private Task<PlaybackResult>? completion;
    private PlaybackState state = PlaybackState.Playing;
    private int currentEventIndex;
    private long totalPausedDurationUs;
    private long pauseStartedTimestampUs;

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
            if (state == PlaybackState.Idle)
            {
                throw new InvalidOperationException("A completed playback session cannot be paused.");
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
            if (state == PlaybackState.Idle)
            {
                throw new InvalidOperationException("A completed playback session cannot be resumed.");
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

    private async Task<PlaybackResult> RunAsync()
    {
        await Task.Yield();

        try
        {
            for (var index = 0; index < macro.Events.Length; index++)
            {
                await InjectWhenDueAsync(macro.Events[index], scaledEventTargetsUs[index])
                    .ConfigureAwait(false);
            }

            return await CompleteWhenDueAsync().ConfigureAwait(false);
        }
        catch
        {
            TransitionToIdle();
            throw;
        }
        finally
        {
            completionCallback(this);
        }
    }

    private async Task InjectWhenDueAsync(InputEvent inputEvent, long targetElapsedUs)
    {
        while (true)
        {
            await WaitUntilTimelineAsync(targetElapsedUs).ConfigureAwait(false);

            lock (stateSync)
            {
                if (state == PlaybackState.Paused ||
                    GetActiveElapsedLocked(scheduler.GetTimestampMicroseconds()) < targetElapsedUs)
                {
                    continue;
                }

                inputInjector.Inject(inputEvent);
                currentEventIndex++;
                return;
            }
        }
    }

    private async Task<PlaybackResult> CompleteWhenDueAsync()
    {
        while (true)
        {
            await WaitUntilTimelineAsync(scaledDurationUs).ConfigureAwait(false);

            lock (stateSync)
            {
                var completionTimestampUs = scheduler.GetTimestampMicroseconds();
                if (state == PlaybackState.Paused ||
                    GetActiveElapsedLocked(completionTimestampUs) < scaledDurationUs)
                {
                    continue;
                }

                var result = new PlaybackResult(
                    currentEventIndex,
                    scaledDurationUs,
                    GetSafeElapsed(completionTimestampUs, playbackStartTimestampUs),
                    playbackSpeed,
                    totalPausedDurationUs);
                state = PlaybackState.Idle;
                return result;
            }
        }
    }

    private async Task WaitUntilTimelineAsync(long targetElapsedUs)
    {
        while (true)
        {
            Task? resumeTask;
            long schedulerTargetUs;

            lock (stateSync)
            {
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
                await resumeTask.ConfigureAwait(false);
                continue;
            }

            await scheduler
                .WaitUntilElapsedAsync(playbackStartTimestampUs, schedulerTargetUs)
                .ConfigureAwait(false);

            lock (stateSync)
            {
                if (state == PlaybackState.Playing &&
                    GetActiveElapsedLocked(scheduler.GetTimestampMicroseconds()) >= targetElapsedUs)
                {
                    return;
                }
            }
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

    private void TransitionToIdle()
    {
        lock (stateSync)
        {
            state = PlaybackState.Idle;
            resumeSignal.TrySetResult();
        }
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

    private static TaskCompletionSource CreateResumeSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }
}
