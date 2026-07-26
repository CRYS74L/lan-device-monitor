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
    private const int AcrylicBackdrop = 3;
    private const int RoundWindowCorners = 2;
    private const int WindowCompositionAttributeAccentPolicy = 19;
    private const int AcrylicBlurBehind = 4;
    private const int DrawAllBorders = 2;
    internal static bool LastAcrylicApplySucceeded { get; private set; }

    public static void Apply(Window window, bool useDarkMode = false)
    {
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

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var backdrop = AcrylicBackdrop;
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
        }

        ApplyTheme(window, useDarkMode);

        if (HwndSource.FromHwnd(handle) is { CompositionTarget: { } target })
        {
            target.BackgroundColor = Colors.Transparent;
        }
    }

    public static void ApplyTheme(Window window, bool useDarkMode)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var darkMode = useDarkMode ? 1 : 0;
            _ = DwmSetWindowAttribute(
                handle,
                DwmUseImmersiveDarkMode,
                ref darkMode,
                Marshal.SizeOf<int>());
        }

        LastAcrylicApplySucceeded =
            ApplyAcrylicAccent(handle, useDarkMode);
    }

    private static bool ApplyAcrylicAccent(
        IntPtr windowHandle,
        bool useDarkMode)
    {
        var policy = new AccentPolicy
        {
            AccentState = AcrylicBlurBehind,
            AccentFlags = DrawAllBorders,
            GradientColor = useDarkMode
                ? unchecked((int)0x3824211E)
                : unchecked((int)0x20FFFFFF)
        };
        var policyPointer = Marshal.AllocHGlobal(
            Marshal.SizeOf<AccentPolicy>());
        try
        {
            Marshal.StructureToPtr(policy, policyPointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttributeAccentPolicy,
                Data = policyPointer,
                SizeOfData = Marshal.SizeOf<AccentPolicy>()
            };
            return SetWindowCompositionAttribute(
                windowHandle,
                ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(policyPointer);
        }
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowCompositionAttribute(
        IntPtr windowHandle,
        ref WindowCompositionAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }
}
