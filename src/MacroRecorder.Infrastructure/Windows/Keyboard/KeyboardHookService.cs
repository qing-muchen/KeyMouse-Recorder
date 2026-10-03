using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroRecorder.Core.Recording.Keyboard;

namespace MacroRecorder.Infrastructure.Windows.Keyboard;

/// <summary>
/// Restartable WH_KEYBOARD_LL source hosted on a dedicated message-loop thread.
/// It observes input only after Start and always forwards messages to the next hook.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class KeyboardHookService : IKeyboardEventSource
{
    private static readonly TimeSpan LifecycleTimeout = TimeSpan.FromSeconds(10);

    private readonly object lifecycleSync = new();
    private readonly object stateSync = new();
    private readonly NativeKeyboardMethods.LowLevelKeyboardProcedure hookProcedure;
    private HookServiceState state;
    private HookThreadContext? activeContext;
    private Exception? lastCallbackException;

    public KeyboardHookService()
    {
        hookProcedure = HookCallback;
    }

    public event Action<KeyboardCaptureEvent>? EventReceived;

    public bool IsRunning
    {
        get
        {
            lock (stateSync)
            {
                return state == HookServiceState.Running;
            }
        }
    }

    /// <summary>The most recent subscriber or marshalling failure caught at the native callback boundary.</summary>
    public Exception? LastCallbackException => Volatile.Read(ref lastCallbackException);

    public void StartCapture()
    {
        lock (lifecycleSync)
        {
            HookThreadContext context;
            lock (stateSync)
            {
                ObjectDisposedException.ThrowIf(state == HookServiceState.Disposed, this);
                if (state != HookServiceState.Stopped)
                {
                    throw new InvalidOperationException($"The keyboard hook service is {state}.");
                }

                Volatile.Write(ref lastCallbackException, null);
                context = new HookThreadContext(RunHookThread);
                activeContext = context;
                state = HookServiceState.Starting;
                context.Thread.Start();
            }

            if (!context.Started.Wait(LifecycleTimeout))
            {
                RequestThreadExit(context);
                if (context.Thread.Join(LifecycleTimeout))
                {
                    context.Started.Dispose();
                }

                ResetStopped(context);
                throw new TimeoutException("The keyboard hook thread did not finish starting within 10 seconds.");
            }

            if (context.StartError is not null)
            {
                context.Thread.Join(LifecycleTimeout);
                context.Started.Dispose();
                ResetStopped(context);
                throw context.StartError;
            }

            lock (stateSync)
            {
                if (!ReferenceEquals(activeContext, context) || !context.Thread.IsAlive)
                {
                    var error = context.ExitError ?? new InvalidOperationException("The keyboard hook thread exited during startup.");
                    ResetStopped(context);
                    context.Started.Dispose();
                    throw error;
                }

                state = HookServiceState.Running;
            }
        }
    }

    public void StopCapture()
    {
        lock (lifecycleSync)
        {
            StopCore();
        }
    }

    public void Dispose()
    {
        lock (lifecycleSync)
        {
            lock (stateSync)
            {
                if (state == HookServiceState.Disposed)
                {
                    return;
                }
            }

            StopCore();

            lock (stateSync)
            {
                state = HookServiceState.Disposed;
            }
        }
    }

    private void StopCore()
    {
        HookThreadContext? context;
        lock (stateSync)
        {
            if (state is HookServiceState.Stopped or HookServiceState.Disposed)
            {
                return;
            }

            if (state == HookServiceState.Starting)
            {
                throw new InvalidOperationException("The keyboard hook service is still starting.");
            }

            context = activeContext;
            if (state == HookServiceState.Running)
            {
                state = HookServiceState.Stopping;
            }
        }

        if (context is null)
        {
            throw new InvalidOperationException("The running keyboard hook has no thread context.");
        }

        if (!NativeKeyboardMethods.PostThreadMessage(
                context.ThreadId, NativeKeyboardMethods.WmQuit, 0, 0))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error(), "Failed to stop the keyboard hook thread.");
            lock (stateSync)
            {
                if (ReferenceEquals(activeContext, context))
                {
                    state = context.Thread.IsAlive ? HookServiceState.Running : HookServiceState.Stopped;
                }
            }

            throw error;
        }

        if (!context.Thread.Join(LifecycleTimeout))
        {
            throw new TimeoutException("The keyboard hook thread did not stop within 10 seconds.");
        }

        if (context.ExitError is not null)
        {
            throw context.ExitError;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Managed exceptions must never cross the unmanaged low-level hook callback boundary.")]
    private nint HookCallback(int code, nint messageParameter, nint dataPointer)
    {
        try
        {
            if (KeyboardHookMessageMapper.TryMap(code, messageParameter, out var transition))
            {
                var data = Marshal.PtrToStructure<LowLevelKeyboardData>(dataPointer);
                PublishSafely(new KeyboardCaptureEvent(
                    transition,
                    data.VirtualKey,
                    data.ScanCode,
                    (uint)data.Flags,
                    KeyboardHookMessageMapper.IsInjected(data.Flags)));
            }
        }
        catch (Exception exception)
        {
            Volatile.Write(ref lastCallbackException, exception);
        }

        return NativeKeyboardMethods.CallNextHookEx(0, code, messageParameter, dataPointer);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "This method models the exception boundary used by the unmanaged hook callback.")]
    internal void PublishSafely(KeyboardCaptureEvent capturedEvent)
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

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Thread startup and cleanup errors are transported back to Start or Stop.")]
    private void RunHookThread(HookThreadContext context)
    {
        nint hookHandle = 0;
        var startupSignaled = false;
        try
        {
            context.ThreadId = NativeKeyboardMethods.GetCurrentThreadId();
            NativeKeyboardMethods.PeekMessage(out _, 0, 0, 0, NativeKeyboardMethods.PeekMessageNoRemove);

            var moduleHandle = NativeKeyboardMethods.GetModuleHandle(null);
            if (moduleHandle == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to obtain the current module handle.");
            }

            hookHandle = NativeKeyboardMethods.SetWindowsHookEx(
                NativeKeyboardMethods.WhKeyboardLowLevel,
                hookProcedure,
                moduleHandle,
                0);
            if (hookHandle == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install the low-level keyboard hook.");
            }

            startupSignaled = true;
            context.Started.Set();

            while (true)
            {
                var result = NativeKeyboardMethods.GetMessage(out _, 0, 0, 0);
                if (result == 0)
                {
                    break;
                }

                if (result == -1)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The keyboard hook message loop failed.");
                }
            }
        }
        catch (Exception exception)
        {
            if (!startupSignaled)
            {
                context.StartError = exception;
                startupSignaled = true;
                context.Started.Set();
            }
            else
            {
                context.ExitError = exception;
            }
        }
        finally
        {
            if (hookHandle != 0 && !NativeKeyboardMethods.UnhookWindowsHookEx(hookHandle))
            {
                context.ExitError ??= new Win32Exception(
                    Marshal.GetLastWin32Error(), "Failed to uninstall the low-level keyboard hook.");
            }

            if (!startupSignaled)
            {
                context.Started.Set();
            }

            var disposeStartSignal = false;
            lock (stateSync)
            {
                if (ReferenceEquals(activeContext, context))
                {
                    disposeStartSignal = state != HookServiceState.Starting;
                    activeContext = null;
                    if (state != HookServiceState.Disposed)
                    {
                        state = HookServiceState.Stopped;
                    }
                }
            }

            if (disposeStartSignal)
            {
                context.Started.Dispose();
            }
        }
    }

    private static void RequestThreadExit(HookThreadContext context)
    {
        if (context.ThreadId != 0)
        {
            NativeKeyboardMethods.PostThreadMessage(context.ThreadId, NativeKeyboardMethods.WmQuit, 0, 0);
        }
    }

    private void ResetStopped(HookThreadContext context)
    {
        lock (stateSync)
        {
            if (ReferenceEquals(activeContext, context))
            {
                activeContext = null;
                state = HookServiceState.Stopped;
            }
        }
    }

    private enum HookServiceState
    {
        Stopped,
        Starting,
        Running,
        Stopping,
        Disposed,
    }

    private sealed class HookThreadContext
    {
        public HookThreadContext(Action<HookThreadContext> threadEntry)
        {
            Thread = new Thread(() => threadEntry(this))
            {
                IsBackground = true,
                Name = "MacroRecorder.KeyboardHook",
            };
        }

        public ManualResetEventSlim Started { get; } = new(false);
        public Thread Thread { get; }
        public uint ThreadId { get; set; }
        public Exception? StartError { get; set; }
        public Exception? ExitError { get; set; }
    }
}
