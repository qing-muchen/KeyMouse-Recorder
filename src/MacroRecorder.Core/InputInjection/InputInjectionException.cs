namespace MacroRecorder.Core.InputInjection;

/// <summary>Raised when the operating system does not accept an entire native input batch.</summary>
public sealed class InputInjectionException : Exception
{
    public InputInjectionException(uint requestedInputCount, uint injectedInputCount, int win32ErrorCode)
        : base($"Input injection accepted {injectedInputCount} of {requestedInputCount} native inputs (Win32 error {win32ErrorCode}).")
    {
        RequestedInputCount = requestedInputCount;
        InjectedInputCount = injectedInputCount;
        Win32ErrorCode = win32ErrorCode;
    }

    public uint RequestedInputCount { get; }

    public uint InjectedInputCount { get; }

    public int Win32ErrorCode { get; }
}
