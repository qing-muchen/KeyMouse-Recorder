using System.Text;
using System.Text.Json;
using MacroRecorder.Core.Models;
using MacroRecorder.Core.Schema;
using MacroRecorder.Core.Tests.Models;
using MacroRecorder.Infrastructure.Persistence;

namespace MacroRecorder.Core.Tests.Persistence;

public sealed class MacroJsonSerializerTests
{
    private readonly MacroJsonSerializer serializer = new();

    [Fact]
    public async Task StreamRoundTripPreservesMixedMacroAndLeavesStreamsOpen()
    {
        var original = DomainTestData.CreateMacro() with
        {
            Name = "测试宏",
            Description = "键鼠录制示例",
        };
        using var stream = new MemoryStream();

        await serializer.SerializeAsync(stream, original);
        Assert.True(stream.CanWrite);
        stream.Position = 0;
        var restored = await serializer.DeserializeAsync(stream);

        Assert.True(stream.CanRead);
        DomainTestData.AssertMacroEquivalent(original, restored);
    }

    [Fact]
    public async Task SerializationUsesReadableStableSchemaContract()
    {
        using var stream = new MemoryStream();
        await serializer.SerializeAsync(stream, DomainTestData.CreateMacro());
        var json = Encoding.UTF8.GetString(stream.ToArray());
        using var document = JsonDocument.Parse(json);

        Assert.Contains(Environment.NewLine, json, StringComparison.Ordinal);
        Assert.Equal(MacroSchema.CurrentVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("SchemaVersion", out _));
        Assert.Equal("keyboard", document.RootElement.GetProperty("events")[0].GetProperty("kind").GetString());
        Assert.Equal(
            "KeyboardKeyDown",
            document.RootElement.GetProperty("events")[0].GetProperty("eventType").GetString());
        Assert.Equal("mouse", document.RootElement.GetProperty("events")[2].GetProperty("kind").GetString());
        Assert.DoesNotContain("$type", json, StringComparison.Ordinal);
        Assert.DoesNotContain("MacroRecorder.", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PublicKeyToken", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedSchemaVersionIsPreservedForFutureValidation()
    {
        var original = DomainTestData.CreateMacro() with { SchemaVersion = 999 };
        using var stream = new MemoryStream();

        await serializer.SerializeAsync(stream, original);
        stream.Position = 0;
        var restored = await serializer.DeserializeAsync(stream);

        Assert.Equal(999, restored.SchemaVersion);
        DomainTestData.AssertMacroEquivalent(original, restored);
    }

    [Fact]
    public async Task JsonNullIsRejectedInsteadOfReturningNullMacro()
    {
        using var stream = new MemoryStream("null"u8.ToArray());

        await Assert.ThrowsAsync<JsonException>(() => serializer.DeserializeAsync(stream).AsTask());
    }

    [Fact]
    public async Task PreCancelledSerializationAndDeserializationAreRejected()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var destination = new MemoryStream();
        using var source = new MemoryStream("{}"u8.ToArray());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            serializer.SerializeAsync(destination, DomainTestData.CreateMacro(), cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            serializer.DeserializeAsync(source, cancellation.Token).AsTask());
    }

    [Fact]
    public async Task NullArgumentsAreRejected()
    {
        using var stream = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            serializer.SerializeAsync(null!, DomainTestData.CreateMacro()).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            serializer.SerializeAsync(stream, null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            serializer.DeserializeAsync(null!).AsTask());
    }

    [Fact]
    public void ProductionOptionsAreReadOnly()
    {
        Assert.True(MacroJsonSerializer.SerializerOptions.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() =>
            MacroJsonSerializer.SerializerOptions.WriteIndented = false);
    }
}
