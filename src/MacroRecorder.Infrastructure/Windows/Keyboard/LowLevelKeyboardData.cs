using System.Runtime.InteropServices;

namespace MacroRecorder.Infrastructure.Windows.Keyboard;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct LowLevelKeyboardData
{
    public readonly uint VirtualKey;
    public readonly uint ScanCode;
    public readonly LowLevelKeyboardFlags Flags;
    public readonly uint MessageTime;
    public readonly nuint ExtraInfo;
}
