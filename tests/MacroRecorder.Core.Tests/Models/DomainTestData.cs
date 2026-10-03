using System.Collections.Immutable;
using MacroRecorder.Core.Models;
using MacroRecorder.Core.Models.Input;

namespace MacroRecorder.Core.Tests.Models;

internal static class DomainTestData
{
    public static Macro CreateMacro(ImmutableArray<InputEvent>? events = null)
    {
        ImmutableArray<InputEvent> timeline = events ??
        [
            new KeyboardInputEvent(0, InputEventType.KeyboardKeyDown, 65, 30, 0),
            new KeyboardInputEvent(0, InputEventType.KeyboardKeyUp, 65, 30, 128),
            new MouseInputEvent(100_000, InputEventType.MouseMove, -500, 200, MouseButton.None, 0),
            new MouseInputEvent(200_000, InputEventType.MouseButtonDown, -500, 200, MouseButton.Left, 0),
            new MouseInputEvent(300_000, InputEventType.MouseButtonUp, -450, 250, MouseButton.Left, 0),
            new MouseInputEvent(400_000, InputEventType.MouseVerticalWheel, -450, 250, MouseButton.None, 15),
            new MouseInputEvent(500_000, InputEventType.MouseHorizontalWheel, -450, 250, MouseButton.None, -30),
        ];

        return new Macro
        {
            Id = Guid.Parse("12acfcbb-743c-45a1-a136-a206aa2741f1"),
            Name = "Domain example",
            Description = "Mixed events with a negative screen coordinate",
            CreatedAt = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 10, 3, 0, 1, 0, TimeSpan.Zero),
            Recording = new RecordingMetadata(timeline.IsEmpty ? 0 : timeline[^1].TimestampUs, timeline.Length),
            Environment = new EnvironmentMetadata(1920, 1080, 1.25),
            Playback = new PlaybackMetadata(1.5),
            Events = timeline,
        };
    }

    public static void AssertMacroEquivalent(Macro expected, Macro actual)
    {
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Description, actual.Description);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.CreatedAt.Offset, actual.CreatedAt.Offset);
        Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
        Assert.Equal(expected.UpdatedAt.Offset, actual.UpdatedAt.Offset);
        Assert.Equal(expected.Recording.DurationUs, actual.Recording.DurationUs);
        Assert.Equal(expected.Recording.EventCount, actual.Recording.EventCount);
        Assert.Equal(expected.Environment.ScreenWidth, actual.Environment.ScreenWidth);
        Assert.Equal(expected.Environment.ScreenHeight, actual.Environment.ScreenHeight);
        Assert.Equal(expected.Environment.DpiScale, actual.Environment.DpiScale);
        Assert.Equal(expected.Playback.DefaultSpeed, actual.Playback.DefaultSpeed);
        Assert.Equal(expected.Events.Length, actual.Events.Length);

        for (var index = 0; index < expected.Events.Length; index++)
        {
            AssertEventEquivalent(expected.Events[index], actual.Events[index]);
        }
    }

    public static void AssertEventEquivalent(InputEvent expected, InputEvent actual)
    {
        Assert.Equal(expected.TimestampUs, actual.TimestampUs);
        Assert.Equal(expected.EventType, actual.EventType);

        switch (expected)
        {
            case KeyboardInputEvent keyboard:
                var actualKeyboard = Assert.IsType<KeyboardInputEvent>(actual);
                Assert.Equal(keyboard.VirtualKey, actualKeyboard.VirtualKey);
                Assert.Equal(keyboard.ScanCode, actualKeyboard.ScanCode);
                Assert.Equal(keyboard.Flags, actualKeyboard.Flags);
                break;
            case MouseInputEvent mouse:
                var actualMouse = Assert.IsType<MouseInputEvent>(actual);
                Assert.Equal(mouse.X, actualMouse.X);
                Assert.Equal(mouse.Y, actualMouse.Y);
                Assert.Equal(mouse.Button, actualMouse.Button);
                Assert.Equal(mouse.WheelDelta, actualMouse.WheelDelta);
                break;
            default:
                Assert.Fail("The equivalence helper must compare every supported concrete event type.");
                break;
        }
    }
}
