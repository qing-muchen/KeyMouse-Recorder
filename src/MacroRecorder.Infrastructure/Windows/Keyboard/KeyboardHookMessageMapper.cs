using MacroRecorder.Core.Recording.Keyboard;

namespace MacroRecorder.Infrastructure.Windows.Keyboard;

internal static class KeyboardHookMessageMapper
{
    public static bool TryMap(int hookCode, nint messageParameter, out KeyboardTransition transition)
    {
        if (hookCode < 0)
        {
            transition = default;
            return false;
        }

        var message = unchecked((uint)(nuint)messageParameter);
        switch (message)
        {
            case NativeKeyboardMethods.WmKeyDown:
            case NativeKeyboardMethods.WmSystemKeyDown:
                transition = KeyboardTransition.KeyDown;
                return true;
            case NativeKeyboardMethods.WmKeyUp:
            case NativeKeyboardMethods.WmSystemKeyUp:
                transition = KeyboardTransition.KeyUp;
                return true;
            default:
                transition = default;
                return false;
        }
    }

    public static bool IsInjected(LowLevelKeyboardFlags flags) =>
        (flags & (LowLevelKeyboardFlags.Injected | LowLevelKeyboardFlags.LowerIntegrityInjected)) != 0;
}
