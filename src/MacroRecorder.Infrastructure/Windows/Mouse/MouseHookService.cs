using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroRecorder.Core.Recording.Mouse;

namespace MacroRecorder.Infrastructure.Windows.Mouse;

/// <summary>
/// Restartable WH_MOUSE_LL source hosted on a dedicated message-loop thread.
/// It observes input only after Start and always forwards messages to the next hook.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MouseHookService : IMouseEventSource
{
    private readonly WindowsHookNativeMethods.LowLevelHookProcedure hookProcedure;
    private readonly LowLevelHookThread hookThread;
    private Exception? lastCallbackException;

    public MouseHookService()
    {
        hookProcedure = HookCallback;
        hookThread = new LowLevelHookThread(
            WindowsHookNativeMethods.WhMouseLowLevel,
            "low-level mouse",
            "MacroRecorder.MouseHook",
            hookProcedure);
    }

    public event Action<MouseCaptureEvent>? EventReceived;

    public bool IsRunning => hookThread.IsRunning;

    /// <summary>The most recent subscriber or marshalling failure caught at the native callback boundary.</summary>
    public Exception? LastCallbackException => Volatile.Read(ref lastCallbackException);

    public void StartCapture()
    {
        Volatile.Write(ref lastCallbackException, null);
        hookThread.StartCapture();
    }

    public void StopCapture() => hookThread.StopCapture();

    public void Dispose() => hookThread.Dispose();

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Managed exceptions must never cross the unmanaged low-level hook callback boundary.")]
    private nint HookCallback(int code, nint messageParameter, nint dataPointer)
    {
        try
        {
            if (code >= 0)
            {
                var data = Marshal.PtrToStructure<LowLevelMouseData>(dataPointer);
                if (MouseHookMessageMapper.TryMap(code, messageParameter, in data, out var capturedEvent))
                {
                    PublishSafely(capturedEvent);
                }
            }
        }
        catch (Exception exception)
        {
            Volatile.Write(ref lastCallbackException, exception);
        }

        return WindowsHookNativeMethods.CallNextHookEx(0, code, messageParameter, dataPointer);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "This method models the exception boundary used by the unmanaged hook callback.")]
    internal void PublishSafely(MouseCaptureEvent capturedEvent)
    {
        try
        {
            EventReceived?.Invoke(capturedEvent);
        }
        catch (Exception exception)
        {
            Volatile.Write(ref lastCallbackException, exception);
        }
    }
}
