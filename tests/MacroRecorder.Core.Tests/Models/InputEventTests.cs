using MacroRecorder.Core.Models.Input;

namespace MacroRecorder.Core.Tests.Models;

public sealed class InputEventTests
{
    [Theory]
    [InlineData(InputEventType.KeyboardKeyDown)]
    [InlineData(InputEventType.KeyboardKeyUp)]
    public void KeyboardConstructionPreservesAllRawFields(InputEventType eventType)
    {
        var input = new KeyboardInputEvent(12_345, eventType, 65, 30, uint.MaxValue);

        Assert.Equal(12_345L, input.TimestampUs);
        Assert.Equal(eventType, input.EventType);
        Assert.Equal(65U, input.VirtualKey);
        Assert.Equal(30U, input.ScanCode);
        Assert.Equal(uint.MaxValue, input.Flags);
    }

    [Fact]
    public void MouseMovePreservesNegativeCoordinatesWithoutButtonOrWheel()
    {
        var input = new MouseInputEvent(8_000, InputEventType.MouseMove, -1920, -200, MouseButton.None, 0);

        Assert.Equal(8_000L, input.TimestampUs);
        Assert.Equal(InputEventType.MouseMove, input.EventType);
        Assert.Equal(-1920, input.X);
        Assert.Equal(-200, input.Y);
        Assert.Equal(MouseButton.None, input.Button);
        Assert.Equal(0, input.WheelDelta);
    }

    [Theory]
    [InlineData(InputEventType.MouseButtonDown, MouseButton.Left)]
    [InlineData(InputEventType.MouseButtonUp, MouseButton.Left)]
    [InlineData(InputEventType.MouseButtonDown, MouseButton.Right)]
    [InlineData(InputEventType.MouseButtonUp, MouseButton.Right)]
    [InlineData(InputEventType.MouseButtonDown, MouseButton.Middle)]
    [InlineData(InputEventType.MouseButtonUp, MouseButton.Middle)]
    [InlineData(InputEventType.MouseButtonDown, MouseButton.XButton1)]
    [InlineData(InputEventType.MouseButtonUp, MouseButton.XButton1)]
    [InlineData(InputEventType.MouseButtonDown, MouseButton.XButton2)]
    [InlineData(InputEventType.MouseButtonUp, MouseButton.XButton2)]
    public void MouseButtonsRepresentIndependentTransitions(InputEventType eventType, MouseButton button)
    {
        var input = new MouseInputEvent(20_000, eventType, 100, 200, button, 0);

        Assert.Equal(20_000L, input.TimestampUs);
        Assert.Equal(eventType, input.EventType);
        Assert.Equal(100, input.X);
        Assert.Equal(200, input.Y);
        Assert.Equal(button, input.Button);
        Assert.Equal(0, input.WheelDelta);
    }

    [Theory]
    [InlineData(InputEventType.MouseVerticalWheel, 15)]
    [InlineData(InputEventType.MouseVerticalWheel, -120)]
    [InlineData(InputEventType.MouseHorizontalWheel, -30)]
    [InlineData(InputEventType.MouseHorizontalWheel, 240)]
    public void WheelsPreserveSignedRawDelta(InputEventType eventType, int delta)
    {
        var input = new MouseInputEvent(30_000, eventType, 100, 200, MouseButton.None, delta);

        Assert.Equal(30_000L, input.TimestampUs);
        Assert.Equal(eventType, input.EventType);
        Assert.Equal(100, input.X);
        Assert.Equal(200, input.Y);
        Assert.Equal(MouseButton.None, input.Button);
        Assert.Equal(delta, input.WheelDelta);
    }

    [Theory]
    [InlineData(InputEventType.MouseMove)]
    [InlineData((InputEventType)999)]
    public void KeyboardRejectsNonKeyboardTypes(InputEventType eventType)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new KeyboardInputEvent(0, eventType, 65, 30, 0));
    }

    [Theory]
    [InlineData(InputEventType.KeyboardKeyDown)]
    [InlineData((InputEventType)999)]
    public void MouseRejectsNonMouseTypes(InputEventType eventType)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MouseInputEvent(0, eventType, 0, 0, MouseButton.None, 0));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void EventsRejectNegativeRelativeTimestamps(long timestampUs)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new KeyboardInputEvent(timestampUs, InputEventType.KeyboardKeyDown, 65, 30, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MouseInputEvent(timestampUs, InputEventType.MouseMove, 0, 0, MouseButton.None, 0));
    }
}
