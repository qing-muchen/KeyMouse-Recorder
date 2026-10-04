using MacroRecorder.Core.Models;
using MacroRecorder.Infrastructure.Persistence;

namespace MacroRecorder.Core.Tests.TestDoubles;

internal sealed class ThrowingMacroSerializer(Exception exception) : IMacroSerializer
{
    public async ValueTask SerializeAsync(
        Stream destination,
        Macro macro,
        CancellationToken cancellationToken = default)
    {
        await destination.WriteAsync("{\"partial\":"u8.ToArray(), cancellationToken);
        throw exception;
    }

    public ValueTask<Macro> DeserializeAsync(
        Stream source,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This test double only exercises serialization failure.");
}
