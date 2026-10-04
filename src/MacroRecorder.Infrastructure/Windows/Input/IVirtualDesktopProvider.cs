namespace MacroRecorder.Infrastructure.Windows.Input;

internal readonly record struct VirtualDesktopBounds(int Left, int Top, int Width, int Height);

internal interface IVirtualDesktopProvider
{
    VirtualDesktopBounds GetBounds();
}
