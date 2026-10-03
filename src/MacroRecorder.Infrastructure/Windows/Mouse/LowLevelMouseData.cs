using System.Runtime.InteropServices;

namespace MacroRecorder.Infrastructure.Windows.Mouse;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct LowLevelMouseData
{
    public LowLevelMouseData(int x, int y, uint mouseData, LowLevelMouseFlags flags)
    {
        Point = new WindowsHookNativeMethods.NativePoint(x, y);
        MouseData = mouseData;
        Flags = flags;
        MessageTime = 0;
        ExtraInfo = 0;
    }

    public readonly WindowsHookNativeMethods.NativePoint Point;
    public readonly uint MouseData;
    public readonly LowLevelMouseFlags Flags;
    public readonly uint MessageTime;
    public readonly nuint ExtraInfo;
}
