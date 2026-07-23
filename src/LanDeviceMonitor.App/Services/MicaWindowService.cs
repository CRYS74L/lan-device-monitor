using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LanDeviceMonitor.Services;

public static class MicaWindowService
{
    private const int DwmSystemBackdropType = 38;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int MicaBackdrop = 2;

    public static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var backdrop = MicaBackdrop;
        _ = DwmSetWindowAttribute(
            handle,
            DwmSystemBackdropType,
            ref backdrop,
            Marshal.SizeOf<int>());

        var lightMode = 0;
        _ = DwmSetWindowAttribute(
            handle,
            DwmUseImmersiveDarkMode,
            ref lightMode,
            Marshal.SizeOf<int>());

        if (HwndSource.FromHwnd(handle) is { CompositionTarget: { } target })
        {
            target.BackgroundColor = Colors.Transparent;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int value,
        int valueSize);
}
