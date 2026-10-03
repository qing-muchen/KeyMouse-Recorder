using MacroRecorder.Core.Models.Input;
using MacroRecorder.Core.Recording.Mouse;

namespace MacroRecorder.Infrastructure.Windows.Mouse;

internal static class MouseHookMessageMapper
{
    public static bool TryMap(
        int hookCode,
        nint messageParameter,
        in LowLevelMouseData data,
        out MouseCaptureEvent capturedEvent)
    {
        if (hookCode < 0)
        {
            capturedEvent = default;
            return false;
        }

        var message = unchecked((uint)(nuint)messageParameter);
        var isInjected = IsInjected(data.Flags);
        switch (message)
        {
            case NativeMouseMethods.WmMouseMove:
                capturedEvent = Create(data, MouseCaptureKind.Move, MouseButton.None, 0, isInjected);
                return true;
            case NativeMouseMethods.WmLeftButtonDown:
                capturedEvent = Create(data, MouseCaptureKind.ButtonDown, MouseButton.Left, 0, isInjected);
                return true;
            case NativeMouseMethods.WmLeftButtonUp:
                capturedEvent = Create(data, MouseCaptureKind.ButtonUp, MouseButton.Left, 0, isInjected);
                return true;
            case NativeMouseMethods.WmRightButtonDown:
                capturedEvent = Create(data, MouseCaptureKind.ButtonDown, MouseButton.Right, 0, isInjected);
                return true;
            case NativeMouseMethods.WmRightButtonUp:
                capturedEvent = Create(data, MouseCaptureKind.ButtonUp, MouseButton.Right, 0, isInjected);
                return true;
            case NativeMouseMethods.WmMiddleButtonDown:
                capturedEvent = Create(data, MouseCaptureKind.ButtonDown, MouseButton.Middle, 0, isInjected);
                return true;
            case NativeMouseMethods.WmMiddleButtonUp:
                capturedEvent = Create(data, MouseCaptureKind.ButtonUp, MouseButton.Middle, 0, isInjected);
                return true;
            case NativeMouseMethods.WmXButtonDown:
                return TryCreateXButton(data, MouseCaptureKind.ButtonDown, isInjected, out capturedEvent);
            case NativeMouseMethods.WmXButtonUp:
                return TryCreateXButton(data, MouseCaptureKind.ButtonUp, isInjected, out capturedEvent);
            case NativeMouseMethods.WmMouseWheel:
                capturedEvent = Create(
                    data, MouseCaptureKind.VerticalWheel, MouseButton.None, GetSignedHighWord(data.MouseData), isInjected);
                return true;
            case NativeMouseMethods.WmMouseHorizontalWheel:
                capturedEvent = Create(
                    data, MouseCaptureKind.HorizontalWheel, MouseButton.None, GetSignedHighWord(data.MouseData), isInjected);
                return true;
            default:
                capturedEvent = default;
                return false;
        }
    }

    public static bool IsInjected(LowLevelMouseFlags flags) =>
        (flags & (LowLevelMouseFlags.Injected | LowLevelMouseFlags.LowerIntegrityInjected)) != 0;

    private static bool TryCreateXButton(
        in LowLevelMouseData data,
        MouseCaptureKind kind,
        bool isInjected,
        out MouseCaptureEvent capturedEvent)
    {
        var button = GetUnsignedHighWord(data.MouseData) switch
        {
            NativeMouseMethods.XButton1 => MouseButton.XButton1,
            NativeMouseMethods.XButton2 => MouseButton.XButton2,
            _ => MouseButton.None,
        };

        if (button == MouseButton.None)
        {
            capturedEvent = default;
            return false;
        }

        capturedEvent = Create(data, kind, button, 0, isInjected);
        return true;
    }

    private static MouseCaptureEvent Create(
        in LowLevelMouseData data,
        MouseCaptureKind kind,
        MouseButton button,
        int wheelDelta,
        bool isInjected) =>
        new(kind, data.Point.X, data.Point.Y, button, wheelDelta, isInjected);

    private static ushort GetUnsignedHighWord(uint value) => (ushort)(value >> 16);

    private static int GetSignedHighWord(uint value) => unchecked((short)(value >> 16));
}
