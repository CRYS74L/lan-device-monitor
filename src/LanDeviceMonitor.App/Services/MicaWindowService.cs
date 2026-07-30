using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.UI.Composition.SystemBackdrops;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;
using CompositionContainerVisual =
    Windows.UI.Composition.ContainerVisual;

namespace LanDeviceMonitor.Services;

public static class MicaWindowService
{
    private const int DwmSystemBackdropType = 38;
    private const int DwmUseHostBackdropBrush = 17;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int AcrylicBackdrop = 3;
    private const int RoundWindowCorners = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint RedrawInvalidate = 0x0001;
    private const uint RedrawUpdateNow = 0x0100;
    private const uint RedrawFrame = 0x0400;
    private const uint RedrawAllChildren = 0x0080;
    private static AcrylicControllerState? _controllerState;

    internal static bool LastAcrylicApplySucceeded { get; private set; }
    internal static bool LastSystemBackdropApplySucceeded { get; private set; }
    internal static bool LastFrameRefreshSucceeded { get; private set; }
    internal static string? LastAcrylicError { get; private set; }
    internal static int ControllerCreationCount { get; private set; }
    internal static IntPtr ActiveControllerWindowHandle =>
        _controllerState?.WindowHandle ?? IntPtr.Zero;

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

        LastFrameRefreshSucceeded = window.IsVisible &&
            RefreshWindowFrame(handle);
    }

    public static void ApplyTheme(Window window, bool useDarkMode)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        PrepareTransparentWpfSurface(window, handle);
        ApplyDwmThemeAttributes(handle, useDarkMode);

        LastAcrylicApplySucceeded =
            TryApplyCompositionAcrylic(handle, useDarkMode);

        if (!LastAcrylicApplySucceeded)
        {
            ApplySystemBackdropFallback(handle);
        }
    }

    public static void Dispose()
    {
        _controllerState?.Dispose();
        _controllerState = null;
    }

    internal static AcrylicThemeValues GetAcrylicThemeValues(bool useDarkMode) =>
        useDarkMode
            ? new AcrylicThemeValues(
                TintColor: Windows.UI.Color.FromArgb(255, 30, 30, 30),
                TintOpacity: 0.18f,
                LuminosityOpacity: 0.24f,
                FallbackColor: Windows.UI.Color.FromArgb(255, 31, 31, 31))
            : new AcrylicThemeValues(
                TintColor: Windows.UI.Color.FromArgb(255, 247, 247, 247),
                TintOpacity: 0.08f,
                LuminosityOpacity: 0.28f,
                FallbackColor: Windows.UI.Color.FromArgb(255, 246, 246, 246));

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

    private static void ApplyDwmThemeAttributes(
        IntPtr handle,
        bool useDarkMode)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            LastSystemBackdropApplySucceeded = false;
            return;
        }

        var darkMode = useDarkMode ? 1 : 0;
        _ = DwmSetWindowAttribute(
            handle,
            DwmUseImmersiveDarkMode,
            ref darkMode,
            Marshal.SizeOf<int>());

        var useHostBackdrop = 1;
        _ = DwmSetWindowAttribute(
            handle,
            DwmUseHostBackdropBrush,
            ref useHostBackdrop,
            Marshal.SizeOf<int>());
    }

    private static bool TryApplyCompositionAcrylic(
        IntPtr handle,
        bool useDarkMode)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return false;
        }

        try
        {
            if (_controllerState is null ||
                _controllerState.WindowHandle != handle)
            {
                _controllerState?.Dispose();
                _controllerState =
                    AcrylicControllerState.Create(handle);
                ControllerCreationCount++;
            }

            _controllerState.ApplyTheme(
                GetAcrylicThemeValues(useDarkMode));
            LastAcrylicError = null;
            LastSystemBackdropApplySucceeded = true;
            return true;
        }
        catch (Exception exception)
        {
            LastAcrylicError = exception.ToString();
            _controllerState?.Dispose();
            _controllerState = null;
            return false;
        }
    }

    private static void ApplySystemBackdropFallback(IntPtr handle)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            LastSystemBackdropApplySucceeded = false;
            return;
        }

        var backdrop = AcrylicBackdrop;
        LastSystemBackdropApplySucceeded =
            DwmSetWindowAttribute(
                handle,
                DwmSystemBackdropType,
                ref backdrop,
                Marshal.SizeOf<int>()) == 0;
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

    private sealed class AcrylicControllerState : IDisposable
    {
        private readonly object? _dispatcherQueueController;
        private readonly Compositor _compositor;
        private readonly DesktopWindowTarget _compositionTarget;
        private readonly CompositionContainerVisual _rootVisual;
        private readonly DesktopAcrylicController _acrylicController;

        private AcrylicControllerState(
            IntPtr windowHandle,
            object? dispatcherQueueController,
            Compositor compositor,
            DesktopWindowTarget compositionTarget,
            CompositionContainerVisual rootVisual,
            DesktopAcrylicController acrylicController)
        {
            WindowHandle = windowHandle;
            _dispatcherQueueController = dispatcherQueueController;
            _compositor = compositor;
            _compositionTarget = compositionTarget;
            _rootVisual = rootVisual;
            _acrylicController = acrylicController;
        }

        public IntPtr WindowHandle { get; }

        public static AcrylicControllerState Create(IntPtr windowHandle)
        {
            object? queueController = null;
            if (Windows.System.DispatcherQueue.GetForCurrentThread() is null)
            {
                var options = new DispatcherQueueOptions
                {
                    Size = Marshal.SizeOf<DispatcherQueueOptions>(),
                    ThreadType = 2,
                    ApartmentType = 2
                };
                var result = CreateDispatcherQueueController(
                    options,
                    ref queueController);
                if (result != 0)
                {
                    Marshal.ThrowExceptionForHR(result);
                }
            }

            var compositor = new Compositor();
            var interop = compositor.As<ICompositorDesktopInterop>();
            interop.CreateDesktopWindowTarget(
                windowHandle,
                false,
                out var targetPointer);
            if (targetPointer == IntPtr.Zero)
            {
                compositor.Dispose();
                throw new InvalidOperationException(
                    "无法创建窗口合成目标。");
            }

            DesktopWindowTarget compositionTarget;
            try
            {
                compositionTarget =
                    DesktopWindowTarget.FromAbi(targetPointer);
            }
            finally
            {
                Marshal.Release(targetPointer);
            }

            var rootVisual = compositor.CreateContainerVisual();
            compositionTarget.Root = rootVisual;
            var acrylicController = new DesktopAcrylicController();
            var windowId =
                Microsoft.UI.Win32Interop.GetWindowIdFromWindow(
                    windowHandle);
            if (!acrylicController.SetTarget(
                    windowId,
                    compositionTarget))
            {
                acrylicController.Dispose();
                rootVisual.Dispose();
                compositionTarget.Dispose();
                compositor.Dispose();
                throw new InvalidOperationException(
                    "系统拒绝应用桌面亚克力材质。");
            }

            return new AcrylicControllerState(
                windowHandle,
                queueController,
                compositor,
                compositionTarget,
                rootVisual,
                acrylicController);
        }

        public void ApplyTheme(AcrylicThemeValues values)
        {
            _acrylicController.TintColor = values.TintColor;
            _acrylicController.TintOpacity = values.TintOpacity;
            _acrylicController.LuminosityOpacity =
                values.LuminosityOpacity;
            _acrylicController.FallbackColor =
                values.FallbackColor;
        }

        public void Dispose()
        {
            _acrylicController.Dispose();
            _rootVisual.Dispose();
            _compositionTarget.Dispose();
            _compositor.Dispose();
        }
    }

    [ComImport]
    [Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        void CreateDesktopWindowTarget(
            IntPtr windowHandle,
            [MarshalAs(UnmanagedType.Bool)] bool isTopmost,
            out IntPtr desktopWindowTarget);
    }

    internal readonly record struct AcrylicThemeValues(
        Windows.UI.Color TintColor,
        float TintOpacity,
        float LuminosityOpacity,
        Windows.UI.Color FallbackColor);

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(
        DispatcherQueueOptions options,
        [MarshalAs(UnmanagedType.IUnknown)] ref object? dispatcherQueueController);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }
}
