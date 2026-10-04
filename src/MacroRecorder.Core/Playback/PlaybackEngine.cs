using MacroRecorder.Core.InputInjection;
using MacroRecorder.Core.Models;
using MacroRecorder.Core.Validation;

namespace MacroRecorder.Core.Playback;

/// <summary>Plays one validated Macro at its recorded 1.0x timeline.</summary>
public sealed class PlaybackEngine
{
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

    public async Task<PlaybackResult> PlayAsync(Macro macro)
    {
        ArgumentNullException.ThrowIfNull(macro);
        BeginPlayback(macro);

        try
        {
            var playbackStartTimestampUs = scheduler.GetTimestampMicroseconds();
            var injectedEventCount = 0;

            foreach (var inputEvent in macro.Events)
            {
                await scheduler
                    .WaitUntilElapsedAsync(playbackStartTimestampUs, inputEvent.TimestampUs)
                    .ConfigureAwait(false);
                inputInjector.Inject(inputEvent);
                injectedEventCount++;
            }

            await scheduler
                .WaitUntilElapsedAsync(playbackStartTimestampUs, macro.Recording.DurationUs)
                .ConfigureAwait(false);

            var actualElapsedUs = GetSafeElapsed(
                scheduler.GetTimestampMicroseconds(),
                playbackStartTimestampUs);
            return new PlaybackResult(
                injectedEventCount,
                macro.Recording.DurationUs,
                actualElapsedUs);
        }
        finally
        {
            EndPlayback();
        }
    }

    private void BeginPlayback(Macro macro)
    {
        lock (stateSync)
        {
            if (state == PlaybackState.Playing)
            {
                throw new InvalidOperationException("A Macro is already playing.");
            }

            var validationResult = MacroValidator.Validate(macro);
            if (!validationResult.IsValid)
            {
                throw new PlaybackValidationException(validationResult);
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
