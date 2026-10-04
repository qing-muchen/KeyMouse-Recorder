namespace MacroRecorder.Infrastructure.Windows.Input;

internal readonly record struct NativeInjectionResult(uint InjectedCount, int ErrorCode);

internal interface IWindowsInputNativeApi
{
    NativeInjectionResult Send(NativeInput[] inputs, int inputSize);
}
