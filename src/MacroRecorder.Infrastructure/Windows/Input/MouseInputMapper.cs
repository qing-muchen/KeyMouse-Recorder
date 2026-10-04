using MacroRecorder.Core.Models.Input;

namespace MacroRecorder.Infrastructure.Windows.Input;

internal static class MouseInputMapper
{
    private const NativeMouseInputFlags PositionFlags = NativeMouseInputFlags.Move
        | NativeMouseInputFlags.Absolute
        | NativeMouseInputFlags.VirtualDesktop;

    public static NativeInput[] Map(
        MouseInputEvent inputEvent,
        VirtualDesktopBounds bounds,
        nuint injectionMarker)
    {
        ValidateSemantics(inputEvent);
        var (normalizedX, normalizedY) = Normalize(inputEvent.X, inputEvent.Y, bounds);
        var position = Create(normalizedX, normalizedY, 0, PositionFlags, injectionMarker);

        return inputEvent.EventType switch
        {
            InputEventType.MouseMove => [position],
            InputEventType.MouseButtonDown =>
                [position, Create(0, 0, GetButtonData(inputEvent.Button), GetButtonFlag(inputEvent.Button, true), injectionMarker)],
            InputEventType.MouseButtonUp =>
                [position, Create(0, 0, GetButtonData(inputEvent.Button), GetButtonFlag(inputEvent.Button, false), injectionMarker)],
            InputEventType.MouseVerticalWheel =>
                [position, Create(0, 0, unchecked((uint)inputEvent.WheelDelta), NativeMouseInputFlags.Wheel, injectionMarker)],
            InputEventType.MouseHorizontalWheel =>
                [position, Create(0, 0, unchecked((uint)inputEvent.WheelDelta), NativeMouseInputFlags.HorizontalWheel, injectionMarker)],
            _ => throw new NotSupportedException($"Mouse event type '{inputEvent.EventType}' is not supported."),
        };
    }

    private static void ValidateSemantics(MouseInputEvent inputEvent)
    {
        switch (inputEvent.EventType)
        {
            case InputEventType.MouseMove when inputEvent.Button != MouseButton.None || inputEvent.WheelDelta != 0:
                throw new ArgumentException("Mouse move events cannot include a button or wheel delta.", nameof(inputEvent));
            case InputEventType.MouseButtonDown or InputEventType.MouseButtonUp:
                if (inputEvent.Button == MouseButton.None || !Enum.IsDefined(inputEvent.Button))
                {
                    throw new ArgumentException("Mouse button events require a supported button.", nameof(inputEvent));
                }

                if (inputEvent.WheelDelta != 0)
                {
                    throw new ArgumentException("Mouse button events cannot include a wheel delta.", nameof(inputEvent));
                }

                break;
            case InputEventType.MouseVerticalWheel or InputEventType.MouseHorizontalWheel:
                if (inputEvent.Button != MouseButton.None)
                {
                    throw new ArgumentException("Mouse wheel events cannot include a button.", nameof(inputEvent));
                }

                if (inputEvent.WheelDelta == 0)
                {
                    throw new ArgumentException("Mouse wheel events require a non-zero delta.", nameof(inputEvent));
                }

                break;
        }
    }

    private static (int X, int Y) Normalize(int x, int y, VirtualDesktopBounds bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("The virtual desktop dimensions must be positive.");
        }

        var rightExclusive = (long)bounds.Left + bounds.Width;
        var bottomExclusive = (long)bounds.Top + bounds.Height;
        if ((long)x < bounds.Left || x >= rightExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, "The X coordinate is outside the virtual desktop.");
        }

        if ((long)y < bounds.Top || y >= bottomExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, "The Y coordinate is outside the virtual desktop.");
        }

        return (
            NormalizeAxis((long)x - bounds.Left, bounds.Width),
            NormalizeAxis((long)y - bounds.Top, bounds.Height));
    }

    private static int NormalizeAxis(long offset, int length) => length == 1
        ? 0
        : checked((int)Math.Round(offset * 65535d / (length - 1), MidpointRounding.AwayFromZero));

    private static NativeInput Create(
        int x,
        int y,
        uint mouseData,
        NativeMouseInputFlags flags,
        nuint injectionMarker) => new()
        {
            Type = NativeInputType.Mouse,
            Data = new NativeInputUnion
            {
                Mouse = new NativeMouseInput
                {
                    X = x,
                    Y = y,
                    MouseData = mouseData,
                    Flags = flags,
                    ExtraInfo = injectionMarker,
                },
            },
        };

    private static NativeMouseInputFlags GetButtonFlag(MouseButton button, bool isDown) => (button, isDown) switch
    {
        (MouseButton.Left, true) => NativeMouseInputFlags.LeftDown,
        (MouseButton.Left, false) => NativeMouseInputFlags.LeftUp,
        (MouseButton.Right, true) => NativeMouseInputFlags.RightDown,
        (MouseButton.Right, false) => NativeMouseInputFlags.RightUp,
        (MouseButton.Middle, true) => NativeMouseInputFlags.MiddleDown,
        (MouseButton.Middle, false) => NativeMouseInputFlags.MiddleUp,
        (MouseButton.XButton1 or MouseButton.XButton2, true) => NativeMouseInputFlags.XDown,
        (MouseButton.XButton1 or MouseButton.XButton2, false) => NativeMouseInputFlags.XUp,
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported mouse button."),
    };

    private static uint GetButtonData(MouseButton button) => button switch
    {
        MouseButton.XButton1 => 1,
        MouseButton.XButton2 => 2,
        _ => 0,
    };
}
