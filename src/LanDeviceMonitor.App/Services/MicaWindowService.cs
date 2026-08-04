using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LanDeviceMonitor.Services;

public static class MicaWindowService
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmSystemBackdropType = 38;
    private const int NoSystemBackdrop = 1;
    private const int AcrylicSystemBackdrop = 3;
    private const int RoundWindowCorners = 2;
    private const int WindowCompositionAttributeAccentPolicy = 19;
    private const int AcrylicBlurBehind = 4;
    private const int DrawAllBorders = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint RedrawInvalidate = 0x0001;
    private const uint RedrawUpdateNow = 0x0100;
    private const uint RedrawFrame = 0x0400;
    private const uint RedrawAllChildren = 0x0080;

    internal static bool LastAcrylicApplySucceeded { get; private set; }
    internal static bool LastSystemBackdropApplySucceeded { get; private set; }
    internal static bool LastFrameRefreshSucceeded { get; private set; }
    internal static string? LastAcrylicError { get; private set; }
    internal static IntPtr ActiveBackdropWindowHandle { get; private set; }

    public static void Apply(Window window, bool useDarkMode = false)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        PrepareTransparentWpfSurface(window, handle);
        ApplyWindowFrameAttributes(handle);
        ApplyTheme(window, useDarkMode);
        LastFrameRefreshSucceeded =
            window.IsVisible && RefreshWindowFrame(handle);
    }

    public static void ApplyTheme(Window window, bool useDarkMode)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        PrepareTransparentWpfSurface(window, handle);
        ApplyDwmThemeAttribute(handle, useDarkMode);
        ApplyPersistentAcrylicBackdrop(handle, useDarkMode);
    }

    public static void Dispose()
    {
        ActiveBackdropWindowHandle = IntPtr.Zero;
    }

    private static void PrepareTransparentWpfSurface(
        Window window,
        IntPtr handle)
    {
        window.Background =
            System.Windows.Media.Brushes.Transparent;
        if (HwndSource.FromHwnd(handle) is { CompositionTarget: { } target })
        {
            target.BackgroundColor = Colors.Transparent;
        }
    }

    private static void ApplyWindowFrameAttributes(IntPtr handle)
    {
        var margins = new Margins
        {
            Left = -1,
            Right = -1,
            Top = -1,
            Bottom = -1
        };
        _ = DwmExtendFrameIntoClientArea(handle, ref margins);

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var cornerPreference = RoundWindowCorners;
        _ = DwmSetWindowAttribute(
            handle,
            DwmWindowCornerPreference,
            ref cornerPreference,
            Marshal.SizeOf<int>());
    }

    private static void ApplyDwmThemeAttribute(
        IntPtr handle,
        bool useDarkMode)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
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

    internal static int GetPersistentAcrylicGradientColor(
        bool useDarkMode) =>
        useDarkMode
            ? unchecked((int)0x30202020)
            : unchecked((int)0x14F5F5F5);

    private static void ApplyPersistentAcrylicBackdrop(
        IntPtr handle,
        bool useDarkMode)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            LastAcrylicApplySucceeded = false;
            LastSystemBackdropApplySucceeded = false;
            LastAcrylicError =
                "当前 Windows 版本不支持持久 Acrylic 背景。";
            return;
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            var backdrop = AcrylicSystemBackdrop;
            var result = DwmSetWindowAttribute(
                handle,
                DwmSystemBackdropType,
                ref backdrop,
                Marshal.SizeOf<int>());
            LastSystemBackdropApplySucceeded = result == 0;
            LastAcrylicApplySucceeded =
                LastSystemBackdropApplySucceeded;
            LastAcrylicError = LastSystemBackdropApplySucceeded
                ? null
                : $"DwmSetWindowAttribute 系统 Acrylic 背景返回" +
                  $" 0x{result:X8}。";
            ActiveBackdropWindowHandle =
                LastSystemBackdropApplySucceeded
                    ? handle
                    : IntPtr.Zero;
            return;
        }

        var policy = new AccentPolicy
        {
            AccentState = AcrylicBlurBehind,
            AccentFlags = DrawAllBorders,
            GradientColor =
                GetPersistentAcrylicGradientColor(useDarkMode)
        };
        var policyPointer = Marshal.AllocHGlobal(
            Marshal.SizeOf<AccentPolicy>());
        try
        {
            Marshal.StructureToPtr(
                policy,
                policyPointer,
                false);
            var data = new WindowCompositionAttributeData
            {
                Attribute =
                    WindowCompositionAttributeAccentPolicy,
                Data = policyPointer,
                SizeOfData = Marshal.SizeOf<AccentPolicy>()
            };
            LastAcrylicApplySucceeded =
                SetWindowCompositionAttribute(handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(policyPointer);
        }

        LastSystemBackdropApplySucceeded =
            LastAcrylicApplySucceeded;
        LastAcrylicError = LastAcrylicApplySucceeded
            ? null
            : $"SetWindowCompositionAttribute 返回失败，" +
              $"Win32Error={Marshal.GetLastWin32Error()}。";
        ActiveBackdropWindowHandle =
            LastAcrylicApplySucceeded ? handle : IntPtr.Zero;
    }

    private static bool RefreshWindowFrame(IntPtr windowHandle)
    {
        var frameChanged = SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoSize |
            SwpNoMove |
            SwpNoZOrder |
            SwpNoActivate |
            SwpFrameChanged);
        _ = RedrawWindow(
            windowHandle,
            IntPtr.Zero,
            IntPtr.Zero,
            RedrawInvalidate |
            RedrawUpdateNow |
            RedrawFrame |
            RedrawAllChildren);
        return frameChanged && DwmFlush() == 0;
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr windowInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(
        IntPtr windowHandle,
        IntPtr updateRectangle,
        IntPtr updateRegion,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
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
