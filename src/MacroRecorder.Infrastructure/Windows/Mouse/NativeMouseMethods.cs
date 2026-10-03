namespace MacroRecorder.Infrastructure.Windows.Mouse;

internal static class NativeMouseMethods
{
    public const uint WmMouseMove = 0x0200;
    public const uint WmLeftButtonDown = 0x0201;
    public const uint WmLeftButtonUp = 0x0202;
    public const uint WmRightButtonDown = 0x0204;
    public const uint WmRightButtonUp = 0x0205;
    public const uint WmMiddleButtonDown = 0x0207;
    public const uint WmMiddleButtonUp = 0x0208;
    public const uint WmMouseWheel = 0x020A;
    public const uint WmXButtonDown = 0x020B;
    public const uint WmXButtonUp = 0x020C;
    public const uint WmMouseHorizontalWheel = 0x020E;

    public const ushort XButton1 = 0x0001;
    public const ushort XButton2 = 0x0002;
}
