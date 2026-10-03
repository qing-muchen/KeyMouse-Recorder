using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroRecorder.Core.Models;
using MacroRecorder.Core.Models.Input;
using MacroRecorder.Core.Schema;
using MacroRecorder.Core.Tests.Models;
using Xunit.Abstractions;

namespace MacroRecorder.Core.Tests.Serialization;

public sealed class DomainJsonTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(InputEventType.KeyboardKeyDown)]
    [InlineData(InputEventType.KeyboardKeyUp)]
    public void KeyboardRoundTripRestoresConcreteTypeAndEveryField(InputEventType eventType)
    {
        InputEvent original = new KeyboardInputEvent(12_345, eventType, uint.MaxValue, uint.MaxValue, uint.MaxValue);
        var json = JsonSerializer.Serialize(original, DomainJson.Options);
        var restored = JsonSerializer.Deserialize<InputEvent>(json, DomainJson.Options);

        DomainTestData.AssertEventEquivalent(original, Assert.IsType<KeyboardInputEvent>(restored));
    }

    [Theory]
    [InlineData(InputEventType.MouseMove, MouseButton.None, 0)]
    [InlineData(InputEventType.MouseButtonDown, MouseButton.Left, 0)]
    [InlineData(InputEventType.MouseButtonUp, MouseButton.Left, 0)]
    [InlineData(InputEventType.MouseButtonDown, MouseButton.Right, 0)]
    [InlineData(InputEventType.MouseButtonUp, MouseButton.Right, 0)]
    [InlineData(InputEventType.MouseButtonDown, MouseButton.Middle, 0)]
    [InlineData(InputEventType.MouseButtonUp, MouseButton.Middle, 0)]
    [InlineData(InputEventType.MouseButtonDown, MouseButton.XButton1, 0)]
    [InlineData(InputEventType.MouseButtonUp, MouseButton.XButton1, 0)]
    [InlineData(InputEventType.MouseButtonDown, MouseButton.XButton2, 0)]
    [InlineData(InputEventType.MouseButtonUp, MouseButton.XButton2, 0)]
    [InlineData(InputEventType.MouseVerticalWheel, MouseButton.None, 15)]
    [InlineData(InputEventType.MouseVerticalWheel, MouseButton.None, -120)]
    [InlineData(InputEventType.MouseHorizontalWheel, MouseButton.None, -30)]
    [InlineData(InputEventType.MouseHorizontalWheel, MouseButton.None, 240)]
    public void MouseRoundTripRestoresConcreteTypeAndEveryField(InputEventType eventType, MouseButton button, int delta)
    {
        InputEvent original = new MouseInputEvent(30_000, eventType, 500, 200, button, delta);
        var json = JsonSerializer.Serialize(original, DomainJson.Options);
        var restored = JsonSerializer.Deserialize<InputEvent>(json, DomainJson.Options);

        DomainTestData.AssertEventEquivalent(original, Assert.IsType<MouseInputEvent>(restored));
    }

    [Fact]
    public void CompleteMixedMacroRoundTripPreservesEveryFieldAndOrder()
    {
        var original = DomainTestData.CreateMacro();
        var json = JsonSerializer.Serialize(original, DomainJson.Options);
        output.WriteLine(json);
        var restored = Assert.IsType<Macro>(JsonSerializer.Deserialize<Macro>(json, DomainJson.Options));

        DomainTestData.AssertMacroEquivalent(original, restored);
        Assert.Equal(json, JsonSerializer.Serialize(original, DomainJson.Options));
    }

    [Fact]
    public void SchemaV1UsesCamelCaseAndReadableEnumsWithoutAssemblyNames()
    {
        var json = JsonSerializer.Serialize(DomainTestData.CreateMacro(), DomainJson.Options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(MacroSchema.CurrentVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.False(root.TryGetProperty("SchemaVersion", out _));
        var keyboard = root.GetProperty("events")[0];
        Assert.Equal("keyboard", keyboard.GetProperty("kind").GetString());
        Assert.Equal("KeyboardKeyDown", keyboard.GetProperty("eventType").GetString());
        Assert.Equal(0L, keyboard.GetProperty("timestampUs").GetInt64());
        var mouse = root.GetProperty("events")[3];
        Assert.Equal("mouse", mouse.GetProperty("kind").GetString());
        Assert.Equal("MouseButtonDown", mouse.GetProperty("eventType").GetString());
        Assert.Equal("Left", mouse.GetProperty("button").GetString());
        Assert.DoesNotContain("$type", json, StringComparison.Ordinal);
        Assert.DoesNotContain("MacroRecorder.", json, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyboardInputEvent", json, StringComparison.Ordinal);
        Assert.DoesNotContain("MouseInputEvent", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PublicKeyToken", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-500, 200)]
    [InlineData(-1920, -1080)]
    [InlineData(int.MinValue, int.MaxValue)]
    public void SignedMouseCoordinatesRoundTripWithoutClamping(int x, int y)
    {
        var original = DomainTestData.CreateMacro(
            [new MouseInputEvent(0, InputEventType.MouseMove, x, y, MouseButton.None, 0)]);
        var json = JsonSerializer.Serialize(original, DomainJson.Options);
        var restored = Assert.IsType<Macro>(JsonSerializer.Deserialize<Macro>(json, DomainJson.Options));

        DomainTestData.AssertMacroEquivalent(original, restored);
    }

    [Theory]
    [InlineData(28_800_000_000L)]
    [InlineData(long.MaxValue)]
    public void LongMicrosecondsRoundTripWithoutOverflow(long timestampUs)
    {
        var original = DomainTestData.CreateMacro(
        [
            new KeyboardInputEvent(timestampUs, InputEventType.KeyboardKeyDown, 65, 30, 0),
            new MouseInputEvent(timestampUs, InputEventType.MouseMove, 0, 0, MouseButton.None, 0),
        ]);
        var json = JsonSerializer.Serialize(original, DomainJson.Options);
        var restored = Assert.IsType<Macro>(JsonSerializer.Deserialize<Macro>(json, DomainJson.Options));

        DomainTestData.AssertMacroEquivalent(original, restored);
        Assert.Equal(timestampUs, restored.Recording.DurationUs);
    }

    [Fact]
    public void EmptyEventsRemainAnEmptyArrayAfterRoundTrip()
    {
        var original = DomainTestData.CreateMacro(ImmutableArray<InputEvent>.Empty);
        var json = JsonSerializer.Serialize(original, DomainJson.Options);
        var restored = Assert.IsType<Macro>(JsonSerializer.Deserialize<Macro>(json, DomainJson.Options));

        DomainTestData.AssertMacroEquivalent(original, restored);
        Assert.False(restored.Events.IsDefault);
        Assert.Empty(restored.Events);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("events").ValueKind);
        Assert.Equal(0, document.RootElement.GetProperty("events").GetArrayLength());
    }

    [Fact]
    public void JsonCannotOmitSchemaVersionAndSilentlyUseTheDefault()
    {
        var document = SerializeMacroToObject();
        Assert.True(document.Remove("schemaVersion"));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Macro>(document.ToJsonString(), DomainJson.Options));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("description")]
    [InlineData("createdAt")]
    [InlineData("updatedAt")]
    [InlineData("recording")]
    [InlineData("environment")]
    [InlineData("playback")]
    [InlineData("events")]
    public void JsonCannotOmitRequiredMacroFields(string property)
    {
        var document = SerializeMacroToObject();
        Assert.True(document.Remove(property));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Macro>(document.ToJsonString(), DomainJson.Options));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("description")]
    [InlineData("recording")]
    [InlineData("environment")]
    [InlineData("playback")]
    [InlineData("events")]
    public void JsonCannotReplaceRequiredMacroFieldsWithNull(string property)
    {
        var document = SerializeMacroToObject();
        document[property] = null;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Macro>(document.ToJsonString(), DomainJson.Options));
    }

    [Theory]
    [InlineData("timestampUs")]
    [InlineData("eventType")]
    [InlineData("virtualKey")]
    [InlineData("scanCode")]
    [InlineData("flags")]
    public void JsonCannotOmitRequiredKeyboardConstructorData(string property)
    {
        InputEvent original = new KeyboardInputEvent(0, InputEventType.KeyboardKeyDown, 65, 30, 0);
        var document = Assert.IsType<JsonObject>(JsonSerializer.SerializeToNode(original, DomainJson.Options));
        Assert.True(document.Remove(property));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<InputEvent>(document.ToJsonString(), DomainJson.Options));
    }

    [Theory]
    [InlineData("timestampUs")]
    [InlineData("eventType")]
    [InlineData("x")]
    [InlineData("y")]
    [InlineData("button")]
    [InlineData("wheelDelta")]
    public void JsonCannotOmitRequiredMouseConstructorData(string property)
    {
        InputEvent original = new MouseInputEvent(0, InputEventType.MouseMove, 0, 0, MouseButton.None, 0);
        var document = Assert.IsType<JsonObject>(JsonSerializer.SerializeToNode(original, DomainJson.Options));
        Assert.True(document.Remove(property));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<InputEvent>(document.ToJsonString(), DomainJson.Options));
    }

    [Fact]
    public void JsonRejectsUnknownDiscriminatorsInsteadOfLosingTheConcreteType()
    {
        const string json = """{"kind":"unknown","timestampUs":0,"eventType":"MouseMove"}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<InputEvent>(json, DomainJson.Options));
    }

    [Fact]
    public void JsonEnumConverterRejectsNumericEventTypes()
    {
        InputEvent original = new KeyboardInputEvent(0, InputEventType.KeyboardKeyDown, 65, 30, 0);
        var document = Assert.IsType<JsonObject>(JsonSerializer.SerializeToNode(original, DomainJson.Options));
        document["eventType"] = 0;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<InputEvent>(document.ToJsonString(), DomainJson.Options));
    }

    [Fact]
    public void SchemaVersionIsPreservedForFutureValidationRatherThanReplaced()
    {
        var original = DomainTestData.CreateMacro() with { SchemaVersion = MacroSchema.CurrentVersion + 1 };
        var json = JsonSerializer.Serialize(original, DomainJson.Options);
        var restored = Assert.IsType<Macro>(JsonSerializer.Deserialize<Macro>(json, DomainJson.Options));

        DomainTestData.AssertMacroEquivalent(original, restored);
    }

    private static JsonObject SerializeMacroToObject() =>
        Assert.IsType<JsonObject>(JsonSerializer.SerializeToNode(DomainTestData.CreateMacro(), DomainJson.Options));
}
