using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroRecorder.Core.InputInjection;
using MacroRecorder.Core.Models.Input;

namespace MacroRecorder.Infrastructure.Windows.Input;

/// <summary>Maps one domain event to a marked SendInput batch.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsInputInjector : IInputInjector
{
    internal static readonly nuint InjectionMarker = 0x4B4D5243;

    private readonly IWindowsInputNativeApi nativeApi;
    private readonly IVirtualDesktopProvider virtualDesktopProvider;

    public WindowsInputInjector()
        : this(new WindowsInputNativeApi(), new WindowsVirtualDesktopProvider())
    {
    }

    internal WindowsInputInjector(
        IWindowsInputNativeApi nativeApi,
        IVirtualDesktopProvider virtualDesktopProvider)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        ArgumentNullException.ThrowIfNull(virtualDesktopProvider);
        this.nativeApi = nativeApi;
        this.virtualDesktopProvider = virtualDesktopProvider;
    }

    public void Inject(InputEvent inputEvent)
    {
        ArgumentNullException.ThrowIfNull(inputEvent);

        var inputs = inputEvent switch
        {
            KeyboardInputEvent keyboard => [KeyboardInputMapper.Map(keyboard, InjectionMarker)],
            MouseInputEvent mouse => MouseInputMapper.Map(mouse, virtualDesktopProvider.GetBounds(), InjectionMarker),
            _ => throw new NotSupportedException($"Input event type '{inputEvent.GetType().FullName}' is not supported."),
        };

        var requestedCount = (uint)inputs.Length;
        var result = nativeApi.Send(inputs, Marshal.SizeOf<NativeInput>());
        if (result.InjectedCount != requestedCount)
        {
            throw new InputInjectionException(requestedCount, result.InjectedCount, result.ErrorCode);
        }
    }
}
