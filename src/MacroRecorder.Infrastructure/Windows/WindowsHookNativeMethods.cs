using System.Runtime.InteropServices;

namespace MacroRecorder.Infrastructure.Windows;

internal static class WindowsHookNativeMethods
{
    public const int WhKeyboardLowLevel = 13;
    public const int WhMouseLowLevel = 14;
    public const uint WmQuit = 0x0012;
    public const uint PeekMessageNoRemove = 0x0000;

    internal delegate nint LowLevelHookProcedure(int code, nint messageParameter, nint dataPointer);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    public static extern nint SetWindowsHookEx(
        int hookType,
        LowLevelHookProcedure callback,
        nint moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(nint hookHandle);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(
        nint hookHandle,
        int code,
        nint messageParameter,
        nint dataPointer);

    [DllImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    public static extern int GetMessage(
        out NativeMessage message,
        nint windowHandle,
        uint minimumMessage,
        uint maximumMessage);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessage(
        out NativeMessage message,
        nint windowHandle,
        uint minimumMessage,
        uint maximumMessage,
        uint removeMessage);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(
        uint threadId,
        uint message,
        nuint wordParameter,
        nint longParameter);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint GetModuleHandle(string? moduleName);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeMessage
    {
        public nint WindowHandle;
        public uint Message;
        public nuint WordParameter;
        public nint LongParameter;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public readonly int X;
        public readonly int Y;
    }
}
