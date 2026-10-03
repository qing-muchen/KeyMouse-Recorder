using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace MacroRecorder.Infrastructure.Windows;

/// <summary>Owns one low-level hook, its dedicated message-loop thread, and its native handle.</summary>
internal sealed class LowLevelHookThread : IDisposable
{
    private static readonly TimeSpan LifecycleTimeout = TimeSpan.FromSeconds(10);

    private readonly object lifecycleSync = new();
    private readonly object stateSync = new();
    private readonly int hookType;
    private readonly string hookName;
    private readonly string threadName;
    private readonly WindowsHookNativeMethods.LowLevelHookProcedure hookProcedure;
    private HookThreadState state;
    private HookThreadContext? activeContext;

    public LowLevelHookThread(
        int hookType,
        string hookName,
        string threadName,
        WindowsHookNativeMethods.LowLevelHookProcedure hookProcedure)
    {
        this.hookType = hookType;
        this.hookName = hookName;
        this.threadName = threadName;
        this.hookProcedure = hookProcedure;
    }

    public bool IsRunning
    {
        get
        {
            lock (stateSync)
            {
                return state == HookThreadState.Running;
            }
        }
    }

    public void StartCapture()
    {
        lock (lifecycleSync)
        {
            HookThreadContext context;
            lock (stateSync)
            {
                ObjectDisposedException.ThrowIf(state == HookThreadState.Disposed, this);
                if (state != HookThreadState.Stopped)
                {
                    throw new InvalidOperationException($"The {hookName} hook thread is {state}.");
                }

                context = new HookThreadContext(RunHookThread, threadName);
                activeContext = context;
                state = HookThreadState.Starting;
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
                throw new TimeoutException($"The {hookName} hook thread did not finish starting within 10 seconds.");
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
                    var error = context.ExitError ??
                        new InvalidOperationException($"The {hookName} hook thread exited during startup.");
                    ResetStopped(context);
                    context.Started.Dispose();
                    throw error;
                }

                state = HookThreadState.Running;
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
                if (state == HookThreadState.Disposed)
                {
                    return;
                }
            }

            StopCore();

            lock (stateSync)
            {
                state = HookThreadState.Disposed;
            }
        }
    }

    private void StopCore()
    {
        HookThreadContext? context;
        lock (stateSync)
        {
            if (state is HookThreadState.Stopped or HookThreadState.Disposed)
            {
                return;
            }

            if (state == HookThreadState.Starting)
            {
                throw new InvalidOperationException($"The {hookName} hook thread is still starting.");
            }

            context = activeContext;
            if (state == HookThreadState.Running)
            {
                state = HookThreadState.Stopping;
            }
        }

        if (context is null)
        {
            throw new InvalidOperationException($"The running {hookName} hook has no thread context.");
        }

        if (!WindowsHookNativeMethods.PostThreadMessage(
                context.ThreadId, WindowsHookNativeMethods.WmQuit, 0, 0))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to stop the {hookName} hook thread.");
            lock (stateSync)
            {
                if (ReferenceEquals(activeContext, context))
                {
                    state = context.Thread.IsAlive ? HookThreadState.Running : HookThreadState.Stopped;
                }
            }

            throw error;
        }

        if (!context.Thread.Join(LifecycleTimeout))
        {
            throw new TimeoutException($"The {hookName} hook thread did not stop within 10 seconds.");
        }

        if (context.ExitError is not null)
        {
            throw context.ExitError;
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
            context.ThreadId = WindowsHookNativeMethods.GetCurrentThreadId();
            WindowsHookNativeMethods.PeekMessage(
                out _, 0, 0, 0, WindowsHookNativeMethods.PeekMessageNoRemove);

            var moduleHandle = WindowsHookNativeMethods.GetModuleHandle(null);
            if (moduleHandle == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to obtain the current module handle.");
            }

            hookHandle = WindowsHookNativeMethods.SetWindowsHookEx(hookType, hookProcedure, moduleHandle, 0);
            if (hookHandle == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to install the {hookName} hook.");
            }

            startupSignaled = true;
            context.Started.Set();

            while (true)
            {
                var result = WindowsHookNativeMethods.GetMessage(out _, 0, 0, 0);
                if (result == 0)
                {
                    break;
                }

                if (result == -1)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"The {hookName} hook message loop failed.");
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
            if (hookHandle != 0 && !WindowsHookNativeMethods.UnhookWindowsHookEx(hookHandle))
            {
                context.ExitError ??= new Win32Exception(
                    Marshal.GetLastWin32Error(), $"Failed to uninstall the {hookName} hook.");
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
                    disposeStartSignal = state != HookThreadState.Starting;
                    activeContext = null;
                    if (state != HookThreadState.Disposed)
                    {
                        state = HookThreadState.Stopped;
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
            WindowsHookNativeMethods.PostThreadMessage(
                context.ThreadId, WindowsHookNativeMethods.WmQuit, 0, 0);
        }
    }

    private void ResetStopped(HookThreadContext context)
    {
        lock (stateSync)
        {
            if (ReferenceEquals(activeContext, context))
            {
                activeContext = null;
                state = HookThreadState.Stopped;
            }
        }
    }

    private enum HookThreadState
    {
        Stopped,
        Starting,
        Running,
        Stopping,
        Disposed,
    }

    private sealed class HookThreadContext
    {
        public HookThreadContext(Action<HookThreadContext> threadEntry, string name)
        {
            Thread = new Thread(() => threadEntry(this))
            {
                IsBackground = true,
                Name = name,
            };
        }

        public ManualResetEventSlim Started { get; } = new(false);
        public Thread Thread { get; }
        public uint ThreadId { get; set; }
        public Exception? StartError { get; set; }
        public Exception? ExitError { get; set; }
    }
}
