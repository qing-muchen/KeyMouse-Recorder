namespace MacroRecorder.Infrastructure.Windows.Keyboard;

[Flags]
internal enum LowLevelKeyboardFlags : uint
{
    Extended = 0x01,
    LowerIntegrityInjected = 0x02,
    Injected = 0x10,
    AltDown = 0x20,
    Up = 0x80,
}
