using MacroRecorder.Core.Models.Input;
using MacroRecorder.Infrastructure.Windows.Keyboard;

namespace MacroRecorder.Infrastructure.Windows.Input;

internal static class KeyboardInputMapper
{
    public static NativeInput Map(KeyboardInputEvent inputEvent, nuint injectionMarker)
    {
        var flags = inputEvent.EventType == InputEventType.KeyboardKeyUp
            ? NativeKeyboardInputFlags.KeyUp
            : NativeKeyboardInputFlags.None;

        if ((inputEvent.Flags & (uint)LowLevelKeyboardFlags.Extended) != 0)
        {
            flags |= NativeKeyboardInputFlags.ExtendedKey;
        }

        ushort virtualKey;
        ushort scanCode;
        if (inputEvent.ScanCode != 0)
        {
            virtualKey = 0;
            scanCode = CheckedUShort(inputEvent.ScanCode, nameof(inputEvent.ScanCode));
            flags |= NativeKeyboardInputFlags.ScanCode;
        }
        else
        {
            virtualKey = CheckedUShort(inputEvent.VirtualKey, nameof(inputEvent.VirtualKey));
            scanCode = 0;
        }

        return new NativeInput
        {
            Type = NativeInputType.Keyboard,
            Data = new NativeInputUnion
            {
                Keyboard = new NativeKeyboardInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = scanCode,
                    Flags = flags,
                    ExtraInfo = injectionMarker,
                },
            },
        };
    }

    private static ushort CheckedUShort(uint value, string parameterName)
    {
        if (value > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The native keyboard value must fit in 16 bits.");
        }

        return (ushort)value;
    }
}
