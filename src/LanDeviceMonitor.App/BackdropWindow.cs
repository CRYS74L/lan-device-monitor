using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using LanDeviceMonitor.Services;

namespace LanDeviceMonitor;

public sealed class BackdropWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;

    private readonly System.Windows.Controls.Image _wallpaperImage;
    private readonly Border _tint;
    private readonly DispatcherTimer _refreshTimer;

    public BackdropWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = false;
        Background = new SolidColorBrush(Colors.Black);
        Left = 0;
        Top = 0;
        Width = 1;
        Height = 1;

        _wallpaperImage = new System.Windows.Controls.Image
        {
            Stretch = Stretch.Fill,
            Effect = new BlurEffect { Radius = 60 }
        };
        RenderOptions.SetBitmapScalingMode(
            _wallpaperImage,
            BitmapScalingMode.HighQuality);

        _tint = new Border
        {
            Background = CreateTintBrush(
                ThemeService.ResolveIsDark(ThemeService.CurrentMode))
        };

        var root = new Grid();
        root.Children.Add(_wallpaperImage);
        root.Children.Add(_tint);
        Content = root;

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                var style = GetWindowLongPtr(handle, GwlExStyle);
                var newStyle =
                    style.ToInt64() | WsExNoActivate | WsExToolWindow;
                _ = SetWindowLongPtr(
                    handle,
                    GwlExStyle,
                    new IntPtr(newStyle));
            }
        };
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _refreshTimer.Tick += async (_, _) =>
        {
            try
            {
                DesktopBackdropService.InvalidateCache();
                var wallpaper = await System.Threading.Tasks.Task.Run(
                    () => DesktopBackdropService.GetWallpaper());
                if (wallpaper is null)
                {
                    return;
                }

                _wallpaperImage.Source = wallpaper;
            }
            catch
            {
                // 刷新失败时保留现有背景。
            }
        };

        Loaded += (_, _) =>
        {
            RefreshWallpaper();
            _refreshTimer.Start();
        };
    }

    public void SyncToMainWindow(IntPtr mainWindowHandle)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero ||
            mainWindowHandle == IntPtr.Zero ||
            !GetWindowRect(mainWindowHandle, out var rect))
        {
            return;
        }

        _ = SetWindowPos(
            handle,
            mainWindowHandle,
            rect.Left,
            rect.Top,
            rect.Right - rect.Left,
            rect.Bottom - rect.Top,
            SwpNoActivate);
    }

    public void UpdateTheme(bool isDark)
    {
        _tint.Background = CreateTintBrush(isDark);
    }

    private void RefreshWallpaper()
    {
        var wallpaper = DesktopBackdropService.GetWallpaper();
        if (wallpaper is not null)
        {
            _wallpaperImage.Source = wallpaper;
        }
    }

    private static SolidColorBrush CreateTintBrush(bool isDark)
    {
        var color = isDark
            ? System.Windows.Media.Color.FromArgb(
                0xCC, 0x20, 0x20, 0x20)
            : System.Windows.Media.Color.FromArgb(
                0xCC, 0xF5, 0xF5, 0xF5);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(
        IntPtr hWnd,
        int index);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(
        IntPtr hWnd,
        int index,
        IntPtr newLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        IntPtr hWnd,
        out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
