using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LanDeviceMonitor.Services;

public static class MicaWindowService
{
    private const int DwmSystemBackdropType = 38;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int MicaBackdrop = 2;
    private const int RoundWindowCorners = 2;

    public static void Apply(Window window, bool useDarkMode = false)
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

        window.Background = System.Windows.Media.Brushes.Transparent;
        var margins = new Margins
        {
            Left = -1,
            Right = -1,
            Top = -1,
            Bottom = -1
        };
        _ = DwmExtendFrameIntoClientArea(handle, ref margins);

        var backdrop = MicaBackdrop;
        _ = DwmSetWindowAttribute(
            handle,
            DwmSystemBackdropType,
            ref backdrop,
            Marshal.SizeOf<int>());

        var cornerPreference = RoundWindowCorners;
        _ = DwmSetWindowAttribute(
            handle,
            DwmWindowCornerPreference,
            ref cornerPreference,
            Marshal.SizeOf<int>());

        ApplyTheme(window, useDarkMode);

        if (HwndSource.FromHwnd(handle) is { CompositionTarget: { } target })
        {
            target.BackgroundColor = Colors.Transparent;
        }
    }

    public static void ApplyTheme(Window window, bool useDarkMode)
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

        var darkMode = useDarkMode ? 1 : 0;
        _ = DwmSetWindowAttribute(
            handle,
            DwmUseImmersiveDarkMode,
            ref darkMode,
            Marshal.SizeOf<int>());
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int value,
        int valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(
        IntPtr windowHandle,
        ref Margins margins);


    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }
}
