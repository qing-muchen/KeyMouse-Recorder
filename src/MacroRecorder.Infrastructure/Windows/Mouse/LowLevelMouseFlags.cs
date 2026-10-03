namespace MacroRecorder.Infrastructure.Windows.Mouse;

[Flags]
internal enum LowLevelMouseFlags : uint
{
    Injected = 0x00000001,
    LowerIntegrityInjected = 0x00000002,
}
