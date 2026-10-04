using MacroRecorder.Core.InputInjection;
using MacroRecorder.Core.Models;
using MacroRecorder.Core.Validation;

namespace MacroRecorder.Core.Playback;

/// <summary>Creates one controllable playback session at a time.</summary>
public sealed class PlaybackEngine
{
    private const double DefaultPlaybackSpeed = 1.0;

    private readonly object stateSync = new();
    private readonly IPlaybackScheduler scheduler;
    private readonly IInputInjector inputInjector;
    private PlaybackSession? activeSession;

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
                return activeSession?.State ?? PlaybackState.Idle;
            }
        }
    }

    public Task<PlaybackResult> PlayAsync(Macro macro) =>
        StartPlayback(macro, DefaultPlaybackSpeed).Completion;

    public Task<PlaybackResult> PlayAsync(Macro macro, double speed) =>
        StartPlayback(macro, speed).Completion;

    public PlaybackSession StartPlayback(Macro macro) =>
        StartPlayback(macro, DefaultPlaybackSpeed);

    public PlaybackSession StartPlayback(Macro macro, double speed)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ValidateSpeed(speed);
        ValidateMacro(macro);

        lock (stateSync)
        {
            if (activeSession is not null)
            {
                throw new InvalidOperationException("A Macro is already playing.");
            }

            var session = new PlaybackSession(
                macro,
                speed,
                scheduler,
                inputInjector,
                OnSessionCompleted);
            activeSession = session;
            session.Start();
            return session;
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

    private static void ValidateMacro(Macro macro)
    {
        var validationResult = MacroValidator.Validate(macro);
        if (!validationResult.IsValid)
        {
            throw new PlaybackValidationException(validationResult);
        }
    }

    private void OnSessionCompleted(PlaybackSession completedSession)
    {
        lock (stateSync)
        {
            if (ReferenceEquals(activeSession, completedSession))
            {
                activeSession = null;
            }
        }
    }
}
