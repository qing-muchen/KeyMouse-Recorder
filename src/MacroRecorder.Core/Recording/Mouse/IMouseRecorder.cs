using System.Diagnostics.CodeAnalysis;

namespace MacroRecorder.Core.Recording.Mouse;

/// <summary>Restartable mouse recording component coordinated by a recording lifecycle owner.</summary>
public interface IMouseRecorder : IDisposable
{
    void Start(RecordingSession session);

    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
        Justification = "Stop is the established lifecycle verb on the existing C# recorder API.")]
    void Stop();
}
