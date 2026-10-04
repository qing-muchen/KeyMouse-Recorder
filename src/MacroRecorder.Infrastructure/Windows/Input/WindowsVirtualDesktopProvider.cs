using System.Runtime.InteropServices;

namespace MacroRecorder.Infrastructure.Windows.Input;

internal sealed class WindowsVirtualDesktopProvider : IVirtualDesktopProvider
{
    private const int VirtualScreenLeft = 76;
    private const int VirtualScreenTop = 77;
    private const int VirtualScreenWidth = 78;
    private const int VirtualScreenHeight = 79;

    public VirtualDesktopBounds GetBounds() => new(
        GetSystemMetrics(VirtualScreenLeft),
        GetSystemMetrics(VirtualScreenTop),
        GetSystemMetrics(VirtualScreenWidth),
        GetSystemMetrics(VirtualScreenHeight));

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
