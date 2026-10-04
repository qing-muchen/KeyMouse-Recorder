using MacroRecorder.Core.Models;

namespace MacroRecorder.Infrastructure.Persistence;

/// <summary>Transforms complete Macro definitions to and from streams without owning either stream.</summary>
public interface IMacroSerializer
{
    ValueTask SerializeAsync(
        Stream destination,
        Macro macro,
        CancellationToken cancellationToken = default);

    ValueTask<Macro> DeserializeAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}
