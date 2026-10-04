using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroRecorder.Core.Models;
using MacroRecorder.Core.Models.Input;
using MacroRecorder.Core.Tests.Models;
using MacroRecorder.Core.Tests.TestDoubles;
using MacroRecorder.Infrastructure.Persistence;

namespace MacroRecorder.Core.Tests.Persistence;

public sealed class MacroFileStoreTests
{
    private readonly MacroFileStore store = new();

    [Fact]
    public async Task SaveCreatesMissingDirectoriesAndLoadsUnicodeMacroFromPathWithSpaces()
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "nested folder", "测试宏.json");
        var original = DomainTestData.CreateMacro() with
        {
            Name = "测试宏",
            Description = "键鼠录制示例",
        };

        await store.SaveAsync(path, original);
        var restored = await store.LoadAsync(path);
        var bytes = await File.ReadAllBytesAsync(path);

        Assert.True(File.Exists(path));
        Assert.False(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
        DomainTestData.AssertMacroEquivalent(original, restored);
        AssertNoTempFiles(System.IO.Path.GetDirectoryName(path)!);
    }

    [Fact]
    public async Task RepeatedLoadsAreEquivalentAndNeverAlterSourceBytes()
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "repeatable.json");
        var original = DomainTestData.CreateMacro();
        await store.SaveAsync(path, original);
        var before = await File.ReadAllBytesAsync(path);

        var first = await store.LoadAsync(path);
        var second = await store.LoadAsync(path);
        var third = await store.LoadAsync(path);
        var after = await File.ReadAllBytesAsync(path);

        DomainTestData.AssertMacroEquivalent(original, first);
        DomainTestData.AssertMacroEquivalent(original, second);
        DomainTestData.AssertMacroEquivalent(original, third);
        Assert.Equal(before, after);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task SaveOverwritesExistingMacroAtomically()
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "overwrite.json");
        var first = DomainTestData.CreateMacro() with { Name = "Macro A" };
        var second = DomainTestData.CreateMacro() with
        {
            Id = Guid.Parse("ccab607e-1767-43f3-bf5a-23a66ae79513"),
            Name = "Macro B",
            UpdatedAt = new DateTimeOffset(2026, 10, 3, 1, 0, 0, TimeSpan.Zero),
        };
        await store.SaveAsync(path, first);

        await store.SaveAsync(path, second);
        var restored = await store.LoadAsync(path);

        DomainTestData.AssertMacroEquivalent(second, restored);
        AssertNoTempFiles(directory.Path);
    }

    [Fact]
    public async Task SaveDoesNotMutateSourceMacroOrMetadata()
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "unchanged.json");
        var original = DomainTestData.CreateMacro() with
        {
            Recording = new RecordingMetadata(500_000, 999),
            UpdatedAt = new DateTimeOffset(2026, 10, 3, 2, 0, 0, TimeSpan.Zero),
        };
        var snapshot = original with { };

        await store.SaveAsync(path, original);
        var restored = await store.LoadAsync(path);

        DomainTestData.AssertMacroEquivalent(snapshot, original);
        Assert.Equal(999, restored.Recording.EventCount);
        Assert.Equal(snapshot.UpdatedAt, restored.UpdatedAt);
    }

    [Fact]
    public async Task SameTimestampEventOrderAndConcreteTypesArePreserved()
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "ordered.json");
        ImmutableArray<InputEvent> events =
        [
            new KeyboardInputEvent(100, InputEventType.KeyboardKeyDown, 65, 30, 0),
            new MouseInputEvent(100, InputEventType.MouseButtonDown, -500, 200, MouseButton.XButton1, 0),
            new MouseInputEvent(100, InputEventType.MouseHorizontalWheel, -500, 200, MouseButton.None, -15),
            new KeyboardInputEvent(long.MaxValue, InputEventType.KeyboardKeyUp, 65, 30, 128),
        ];
        var original = DomainTestData.CreateMacro(events);

        await store.SaveAsync(path, original);
        var restored = await store.LoadAsync(path);

        DomainTestData.AssertMacroEquivalent(original, restored);
        Assert.IsType<KeyboardInputEvent>(restored.Events[0]);
        Assert.IsType<MouseInputEvent>(restored.Events[1]);
        Assert.Equal(MouseButton.XButton1, Assert.IsType<MouseInputEvent>(restored.Events[1]).Button);
        Assert.Equal(-15, Assert.IsType<MouseInputEvent>(restored.Events[2]).WheelDelta);
        Assert.Equal(long.MaxValue, restored.Events[^1].TimestampUs);
    }

    [Fact]
    public async Task MissingFileThrowsAndLoadNeverCreatesIt()
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "missing.json");

        await Assert.ThrowsAsync<FileNotFoundException>(() => store.LoadAsync(path));

        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    public async Task EmptyTruncatedAndNullJsonFailClearly(string json)
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "invalid.json");
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        await Assert.ThrowsAsync<JsonException>(() => store.LoadAsync(path));
    }

    [Theory]
    [InlineData("discriminator")]
    [InlineData("enumString")]
    [InlineData("numericEnum")]
    public async Task InvalidPolymorphicAndEnumDataFailClearly(string mutation)
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "invalid-event.json");
        await store.SaveAsync(path, DomainTestData.CreateMacro());
        var root = Assert.IsType<JsonObject>(JsonNode.Parse(await File.ReadAllTextAsync(path)));
        var firstEvent = Assert.IsType<JsonObject>(Assert.IsType<JsonArray>(root["events"])[0]);
        switch (mutation)
        {
            case "discriminator":
                firstEvent["kind"] = "unknown";
                break;
            case "enumString":
                firstEvent["eventType"] = "UnknownKeyboardEvent";
                break;
            case "numericEnum":
                firstEvent["eventType"] = 0;
                break;
            default:
                Assert.Fail("The test must handle every declared mutation.");
                break;
        }

        await File.WriteAllTextAsync(
            path,
            root.ToJsonString(MacroJsonSerializer.SerializerOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        await Assert.ThrowsAsync<JsonException>(() => store.LoadAsync(path));
    }

    [Fact]
    public async Task ExistingDestinationSurvivesPartialSerializationFailure()
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "protected.json");
        var original = DomainTestData.CreateMacro() with { Name = "Macro A" };
        await store.SaveAsync(path, original);
        var before = await File.ReadAllBytesAsync(path);
        var expected = new InvalidOperationException("deterministic serializer failure");
        var failingStore = new MacroFileStore(new ThrowingMacroSerializer(expected));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failingStore.SaveAsync(path, original with { Name = "Macro B" }));

        Assert.Same(expected, actual);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        DomainTestData.AssertMacroEquivalent(original, await store.LoadAsync(path));
        AssertNoTempFiles(directory.Path);
    }

    [Fact]
    public async Task FailedNewSaveLeavesNoTargetOrTemporaryFile()
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "never-created.json");
        var failingStore = new MacroFileStore(
            new ThrowingMacroSerializer(new InvalidOperationException("deterministic serializer failure")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failingStore.SaveAsync(path, DomainTestData.CreateMacro()));

        Assert.False(File.Exists(path));
        AssertNoTempFiles(directory.Path);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationAfterPartialWritePreservesExistingStateAndCleansTemp(bool targetExists)
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "cancelled.json");
        byte[]? originalBytes = null;
        if (targetExists)
        {
            await store.SaveAsync(path, DomainTestData.CreateMacro() with { Name = "Existing" });
            originalBytes = await File.ReadAllBytesAsync(path);
        }

        var blockingSerializer = new BlockingMacroSerializer();
        var blockingStore = new MacroFileStore(blockingSerializer);
        using var cancellation = new CancellationTokenSource();
        var saveTask = blockingStore.SaveAsync(path, DomainTestData.CreateMacro(), cancellation.Token);
        await blockingSerializer.Started;
        var activeTemp = Assert.Single(Directory.EnumerateFiles(directory.Path, "*.tmp"));
        Assert.Equal(directory.Path, System.IO.Path.GetDirectoryName(activeTemp));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => saveTask);

        Assert.Equal(targetExists, File.Exists(path));
        if (originalBytes is not null)
        {
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
        }

        AssertNoTempFiles(directory.Path);
    }

    [Fact]
    public async Task SuccessfulSaveAndLoadReleaseFileHandles()
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "released.json");
        await store.SaveAsync(path, DomainTestData.CreateMacro());
        await store.LoadAsync(path);

        using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.True(exclusive.CanRead);
            Assert.True(exclusive.CanWrite);
        }

        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task PreCancelledSaveDoesNotCreateDirectoryOrTarget()
    {
        using var directory = new TestDirectory();
        var parent = System.IO.Path.Combine(directory.Path, "not-created");
        var path = System.IO.Path.Combine(parent, "cancelled.json");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SaveAsync(path, DomainTestData.CreateMacro(), cancellation.Token));

        Assert.False(Directory.Exists(parent));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task LargeMacroWithTenThousandEventsRoundTrips()
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "large.json");
        var events = Enumerable.Range(0, 10_000)
            .Select<int, InputEvent>(index => index % 2 == 0
                ? new KeyboardInputEvent(index * 1_000L, InputEventType.KeyboardKeyDown, 65, 30, 0)
                : new MouseInputEvent(
                    index * 1_000L,
                    InputEventType.MouseMove,
                    index,
                    -index,
                    MouseButton.None,
                    0))
            .ToImmutableArray();
        var original = DomainTestData.CreateMacro(events);

        await store.SaveAsync(path, original);
        var restored = await store.LoadAsync(path);

        Assert.Equal(10_000, restored.Events.Length);
        DomainTestData.AssertEventEquivalent(original.Events[0], restored.Events[0]);
        DomainTestData.AssertEventEquivalent(original.Events[^1], restored.Events[^1]);
        Assert.True(new FileInfo(path).Length > 1_000_000);
        AssertNoTempFiles(directory.Path);
    }

    [Fact]
    public async Task InvalidArgumentsAreRejected()
    {
        var macro = DomainTestData.CreateMacro();

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.SaveAsync(null!, macro));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(" ", macro));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.SaveAsync("macro.json", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.LoadAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => store.LoadAsync(" "));
    }

    private static void AssertNoTempFiles(string directory) =>
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
}
