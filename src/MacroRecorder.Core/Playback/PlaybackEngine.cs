using MacroRecorder.Core.InputInjection;
using MacroRecorder.Core.Models;
using MacroRecorder.Core.Validation;

namespace MacroRecorder.Core.Playback;

/// <summary>Plays one validated Macro at a positive finite runtime speed.</summary>
public sealed class PlaybackEngine
{
    private const double DefaultPlaybackSpeed = 1.0;
    private const double LongUpperExclusive = 9_223_372_036_854_775_808d;

    private readonly object stateSync = new();
    private readonly IPlaybackScheduler scheduler;
    private readonly IInputInjector inputInjector;
    private PlaybackState state;

    public PlaybackEngine(IPlaybackScheduler scheduler, IInputInjector inputInjector)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(inputInjector);
        this.scheduler = scheduler;
        this.inputInjector = inputInjector;
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

    public Task<PlaybackResult> PlayAsync(Macro macro) =>
        PlayAsync(macro, DefaultPlaybackSpeed);

    public async Task<PlaybackResult> PlayAsync(Macro macro, double speed)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ValidateSpeed(speed);
        ValidateMacro(macro);
        BeginPlayback();

        try
        {
            var scaledEventTargetsUs = ScaleTimeline(macro, speed);
            var scaledDurationUs = ScaleTimestamp(macro.Recording.DurationUs, speed);
            var playbackStartTimestampUs = scheduler.GetTimestampMicroseconds();
            var injectedEventCount = 0;

            for (var index = 0; index < macro.Events.Length; index++)
            {
                await scheduler
                    .WaitUntilElapsedAsync(playbackStartTimestampUs, scaledEventTargetsUs[index])
                    .ConfigureAwait(false);
                inputInjector.Inject(macro.Events[index]);
                injectedEventCount++;
            }

            await scheduler
                .WaitUntilElapsedAsync(playbackStartTimestampUs, scaledDurationUs)
                .ConfigureAwait(false);

            var actualElapsedUs = GetSafeElapsed(
                scheduler.GetTimestampMicroseconds(),
                playbackStartTimestampUs);
            return new PlaybackResult(
                injectedEventCount,
                scaledDurationUs,
                actualElapsedUs,
                speed);
        }
        finally
        {
            EndPlayback();
        }
    }

    private static void ValidateSpeed(double speed)
    {
        if (!double.IsFinite(speed) || speed <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(speed),
                speed,
                "Playback speed must be a finite value greater than zero.");
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

    private static void ValidateMacro(Macro macro)
    {
        var validationResult = MacroValidator.Validate(macro);
        if (!validationResult.IsValid)
        {
            throw new PlaybackValidationException(validationResult);
        }
    }

    private void BeginPlayback()
    {
        lock (stateSync)
        {
            if (state == PlaybackState.Playing)
            {
                throw new InvalidOperationException("A Macro is already playing.");
            }

            state = PlaybackState.Playing;
        }
    }

    private void EndPlayback()
    {
        lock (stateSync)
        {
            state = PlaybackState.Idle;
        }
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
}
