using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace LanDeviceMonitor.Services;

public static class DesktopBackdropService
{
    private const uint PrintWindowFullContent = 0x00000002;
    private static readonly object SyncRoot = new();
    private static BitmapSource? _cachedWallpaper;

    public static BitmapSource? GetWallpaper()
    {
        if (_cachedWallpaper is not null)
        {
            return _cachedWallpaper;
        }

        lock (SyncRoot)
        {
            if (_cachedWallpaper is not null)
            {
                return _cachedWallpaper;
            }

            _cachedWallpaper = CaptureWallpaper();
            return _cachedWallpaper;
        }
    }

    public static void InvalidateCache() =>
        _cachedWallpaper = null;

    private static BitmapSource? CaptureWallpaper()
    {
        var wallpaperWindow = FindWallpaperWindow();
        if (wallpaperWindow == IntPtr.Zero)
        {
            return null;
        }

        var width = GetSystemMetrics(0);
        var height = GetSystemMetrics(1);
        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(screenDc, width, height);
        if (screenDc != IntPtr.Zero)
        {
            _ = ReleaseDC(IntPtr.Zero, screenDc);
        }

        if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
        {
            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                DeleteDC(memoryDc);
            }

            return null;
        }

        try
        {
            var previousObject = SelectObject(memoryDc, bitmap);
            _ = PrintWindow(
                wallpaperWindow,
                memoryDc,
                PrintWindowFullContent);
            _ = SelectObject(memoryDc, previousObject);

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
        }
    }

    private static IntPtr FindWallpaperWindow()
    {
        var result = IntPtr.Zero;
        _ = EnumWindows((hWnd, _) =>
        {
            var className = new StringBuilder(256);
            if (GetClassName(hWnd, className, className.Capacity) == 0)
            {
                return true;
            }

            var name = className.ToString();
            if ((name == "WorkerW" || name == "Progman") &&
                FindWindowEx(
                    hWnd,
                    IntPtr.Zero,
                    "SHELLDLL_DefView",
                    null) != IntPtr.Zero)
            {
                result = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return result;
    }

    private delegate bool EnumWindowsProc(
        IntPtr hWnd,
        IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(
        IntPtr parentWindow,
        IntPtr childAfter,
        string? className,
        string? windowName);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(
        EnumWindowsProc callback,
        IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        IntPtr hWnd,
        StringBuilder className,
        int maxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(
        IntPtr hWnd,
        IntPtr hdcBitmap,
        uint flags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(
        IntPtr hDc,
        int width,
        int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(
        IntPtr hDc,
        IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hDc);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
