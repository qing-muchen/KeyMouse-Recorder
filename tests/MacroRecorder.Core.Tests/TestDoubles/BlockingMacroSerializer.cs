using MacroRecorder.Core.Models;
using MacroRecorder.Infrastructure.Persistence;

namespace MacroRecorder.Core.Tests.TestDoubles;

internal sealed class BlockingMacroSerializer : IMacroSerializer
{
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => started.Task;

    public async ValueTask SerializeAsync(
        Stream destination,
        Macro macro,
        CancellationToken cancellationToken = default)
    {
        await destination.WriteAsync("{\"partial\":"u8.ToArray(), cancellationToken);
        started.TrySetResult();
        await release.Task.WaitAsync(cancellationToken);
    }

    public ValueTask<Macro> DeserializeAsync(
        Stream source,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This test double only exercises deterministic save cancellation.");

    public void Release() => release.TrySetResult();
}
