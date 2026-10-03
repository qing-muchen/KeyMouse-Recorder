namespace MacroRecorder.Core.Diagnostics;

/// <summary>
/// Logs operational events only. Deliberately excludes free-form input content.
/// Ownership and disposal of a concrete logger belong to the composition root.
/// </summary>
public interface IAppLogger
{
    void Write(AppLogLevel level, AppLogEvent eventId, Exception? exception = null);
}
