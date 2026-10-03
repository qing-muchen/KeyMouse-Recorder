namespace MacroRecorder.Infrastructure.Windows.Keyboard;

internal static class NativeKeyboardMethods
{
    public const uint WmQuit = WindowsHookNativeMethods.WmQuit;
    public const uint WmKeyDown = 0x0100;
    public const uint WmKeyUp = 0x0101;
    public const uint WmSystemKeyDown = 0x0104;
    public const uint WmSystemKeyUp = 0x0105;
}
