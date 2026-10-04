using System.Runtime.InteropServices;

namespace MacroRecorder.Infrastructure.Windows.Input;

internal sealed class WindowsInputNativeApi : IWindowsInputNativeApi
{
    public NativeInjectionResult Send(NativeInput[] inputs, int inputSize)
    {
        var injectedCount = SendInput((uint)inputs.Length, inputs, inputSize);
        var errorCode = injectedCount == inputs.Length ? 0 : Marshal.GetLastWin32Error();
        return new NativeInjectionResult(injectedCount, errorCode);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, [In] NativeInput[] inputs, int inputSize);
}
