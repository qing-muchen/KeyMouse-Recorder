using System.Diagnostics.CodeAnalysis;

namespace MacroRecorder.Core.Recording.Keyboard;

/// <summary>Restartable keyboard recording component coordinated by a recording lifecycle owner.</summary>
public interface IKeyboardRecorder : IDisposable
{
    void Start(RecordingSession session);

    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
        Justification = "Stop is the established lifecycle verb on the existing C# recorder API.")]
    void Stop();
}
