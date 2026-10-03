using MacroRecorder.Core.Models.Input;
using MacroRecorder.Core.Recording.Mouse;
using MacroRecorder.Infrastructure.Windows.Mouse;

namespace MacroRecorder.Core.Tests.Windows.Mouse;

public sealed class MouseHookMessageMapperTests
{
    [Theory]
    [InlineData(NativeMouseMethods.WmMouseMove, MouseCaptureKind.Move, MouseButton.None)]
    [InlineData(NativeMouseMethods.WmLeftButtonDown, MouseCaptureKind.ButtonDown, MouseButton.Left)]
    [InlineData(NativeMouseMethods.WmLeftButtonUp, MouseCaptureKind.ButtonUp, MouseButton.Left)]
    [InlineData(NativeMouseMethods.WmRightButtonDown, MouseCaptureKind.ButtonDown, MouseButton.Right)]
    [InlineData(NativeMouseMethods.WmRightButtonUp, MouseCaptureKind.ButtonUp, MouseButton.Right)]
    [InlineData(NativeMouseMethods.WmMiddleButtonDown, MouseCaptureKind.ButtonDown, MouseButton.Middle)]
    [InlineData(NativeMouseMethods.WmMiddleButtonUp, MouseCaptureKind.ButtonUp, MouseButton.Middle)]
    public void MapsMoveAndStandardButtonMessages(uint message, MouseCaptureKind kind, MouseButton button)
    {
        var data = new LowLevelMouseData(-500, 200, 0, 0);

        Assert.True(MouseHookMessageMapper.TryMap(0, (nint)message, in data, out var actual));
        Assert.Equal(kind, actual.Kind);
        Assert.Equal(button, actual.Button);
        Assert.Equal(-500, actual.X);
        Assert.Equal(200, actual.Y);
        Assert.Equal(0, actual.WheelDelta);
    }

    [Theory]
    [InlineData(NativeMouseMethods.WmXButtonDown, NativeMouseMethods.XButton1, MouseCaptureKind.ButtonDown, MouseButton.XButton1)]
    [InlineData(NativeMouseMethods.WmXButtonUp, NativeMouseMethods.XButton1, MouseCaptureKind.ButtonUp, MouseButton.XButton1)]
    [InlineData(NativeMouseMethods.WmXButtonDown, NativeMouseMethods.XButton2, MouseCaptureKind.ButtonDown, MouseButton.XButton2)]
    [InlineData(NativeMouseMethods.WmXButtonUp, NativeMouseMethods.XButton2, MouseCaptureKind.ButtonUp, MouseButton.XButton2)]
    public void MapsXButtons(uint message, ushort highWord, MouseCaptureKind kind, MouseButton button)
    {
        var data = new LowLevelMouseData(10, 20, (uint)highWord << 16, 0);

        Assert.True(MouseHookMessageMapper.TryMap(0, (nint)message, in data, out var actual));
        Assert.Equal(kind, actual.Kind);
        Assert.Equal(button, actual.Button);
    }

    [Theory]
    [InlineData(NativeMouseMethods.WmMouseWheel, 120, MouseCaptureKind.VerticalWheel)]
    [InlineData(NativeMouseMethods.WmMouseWheel, -120, MouseCaptureKind.VerticalWheel)]
    [InlineData(NativeMouseMethods.WmMouseWheel, 60, MouseCaptureKind.VerticalWheel)]
    [InlineData(NativeMouseMethods.WmMouseWheel, -60, MouseCaptureKind.VerticalWheel)]
    [InlineData(NativeMouseMethods.WmMouseHorizontalWheel, 120, MouseCaptureKind.HorizontalWheel)]
    [InlineData(NativeMouseMethods.WmMouseHorizontalWheel, -120, MouseCaptureKind.HorizontalWheel)]
    [InlineData(NativeMouseMethods.WmMouseHorizontalWheel, 60, MouseCaptureKind.HorizontalWheel)]
    [InlineData(NativeMouseMethods.WmMouseHorizontalWheel, -60, MouseCaptureKind.HorizontalWheel)]
    public void MapsSignedWheelDelta(uint message, short delta, MouseCaptureKind kind)
    {
        var mouseData = unchecked((uint)(ushort)delta) << 16;
        var data = new LowLevelMouseData(300, 400, mouseData, 0);

        Assert.True(MouseHookMessageMapper.TryMap(0, (nint)message, in data, out var actual));
        Assert.Equal(kind, actual.Kind);
        Assert.Equal(MouseButton.None, actual.Button);
        Assert.Equal(delta, actual.WheelDelta);
        Assert.Equal(300, actual.X);
        Assert.Equal(400, actual.Y);
    }

    [Fact]
    public void UnknownXButtonIsIgnored()
    {
        var data = new LowLevelMouseData(0, 0, 3U << 16, 0);

        Assert.False(MouseHookMessageMapper.TryMap(
            0, (nint)NativeMouseMethods.WmXButtonDown, in data, out _));
    }

    [Fact]
    public void UnknownMessageIsIgnored()
    {
        var data = new LowLevelMouseData(0, 0, 0, 0);

        Assert.False(MouseHookMessageMapper.TryMap(0, 0, in data, out _));
    }

    [Fact]
    public void NegativeHookCodeIsIgnored()
    {
        var data = new LowLevelMouseData(0, 0, 0, 0);

        Assert.False(MouseHookMessageMapper.TryMap(
            -1, (nint)NativeMouseMethods.WmMouseMove, in data, out _));
    }

    [Theory]
    [InlineData(0U, false)]
    [InlineData((uint)LowLevelMouseFlags.Injected, true)]
    [InlineData((uint)LowLevelMouseFlags.LowerIntegrityInjected, true)]
    [InlineData((uint)(LowLevelMouseFlags.Injected | LowLevelMouseFlags.LowerIntegrityInjected), true)]
    public void ClassifiesInjectedFlags(uint value, bool expected)
    {
        Assert.Equal(expected, MouseHookMessageMapper.IsInjected((LowLevelMouseFlags)value));
    }
}
