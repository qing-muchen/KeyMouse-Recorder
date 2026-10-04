using System.Runtime.InteropServices;

namespace MacroRecorder.Infrastructure.Windows.Input;

internal enum NativeInputType : uint
{
    Mouse = 0,
    Keyboard = 1,
}

[Flags]
internal enum NativeKeyboardInputFlags : uint
{
    None = 0,
    ExtendedKey = 0x0001,
    KeyUp = 0x0002,
    ScanCode = 0x0008,
}

[Flags]
internal enum NativeMouseInputFlags : uint
{
    Move = 0x0001,
    LeftDown = 0x0002,
    LeftUp = 0x0004,
    RightDown = 0x0008,
    RightUp = 0x0010,
    MiddleDown = 0x0020,
    MiddleUp = 0x0040,
    XDown = 0x0080,
    XUp = 0x0100,
    Wheel = 0x0800,
    HorizontalWheel = 0x1000,
    VirtualDesktop = 0x4000,
    Absolute = 0x8000,
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeInput
{
    public NativeInputType Type;
    public NativeInputUnion Data;
}

[StructLayout(LayoutKind.Explicit)]
internal struct NativeInputUnion
{
    [FieldOffset(0)]
    public NativeMouseInput Mouse;

    [FieldOffset(0)]
    public NativeKeyboardInput Keyboard;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeKeyboardInput
{
    public ushort VirtualKey;
    public ushort ScanCode;
    public NativeKeyboardInputFlags Flags;
    public uint Time;
    public nuint ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMouseInput
{
    public int X;
    public int Y;
    public uint MouseData;
    public NativeMouseInputFlags Flags;
    public uint Time;
    public nuint ExtraInfo;
}
