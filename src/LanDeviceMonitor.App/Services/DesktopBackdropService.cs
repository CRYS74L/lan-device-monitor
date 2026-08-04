using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace LanDeviceMonitor.Services;

public static class DesktopBackdropService
{
    public static BitmapSource? CaptureFullScreen(
        System.Drawing.Rectangle excludeRect)
    {
        var screenWidth = GetSystemMetrics(0);
        var screenHeight = GetSystemMetrics(1);
        if (screenWidth <= 0 ||
            screenHeight <= 0 ||
            screenWidth > 8192 ||
            screenHeight > 8192)
        {
            return null;
        }

        try
        {
            using var bitmap = new System.Drawing.Bitmap(
                screenWidth,
                screenHeight);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(0, 0, 0, 0, bitmap.Size);
            }

            ExcludeOwnWindowRegion(bitmap, excludeRect);

            var hBitmap = bitmap.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        catch
        {
            return null;
        }
    }

    private static void ExcludeOwnWindowRegion(
        System.Drawing.Bitmap bitmap,
        System.Drawing.Rectangle windowRect)
    {
        var left = windowRect.Left;
        var top = windowRect.Top;
        var right = windowRect.Right;
        var bottom = windowRect.Bottom;
        if (right <= left || bottom <= top)
        {
            return;
        }

        var inset = 28;
        var sampleLeft = Math.Max(0, left - inset);
        var sampleTop = Math.Max(0, top - inset);
        var sampleRight = Math.Min(bitmap.Width - 1, right + inset);
        var sampleBottom = Math.Min(bitmap.Height - 1, bottom + inset);
        long sumR = 0;
        long sumG = 0;
        long sumB = 0;
        var count = 0;
        for (var y = sampleTop; y <= sampleBottom; y += 6)
        {
            for (var x = sampleLeft; x <= sampleRight; x += 6)
            {
                if (x >= left && x < right &&
                    y >= top && y < bottom)
                {
                    continue;
                }

                var pixel = bitmap.GetPixel(x, y);
                sumR += pixel.R;
                sumG += pixel.G;
                sumB += pixel.B;
                count++;
            }
        }

        if (count == 0)
        {
            return;
        }

        using var brush = new System.Drawing.SolidBrush(
            System.Drawing.Color.FromArgb(
                (int)(sumR / count),
                (int)(sumG / count),
                (int)(sumB / count)));
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.FillRectangle(
            brush,
            left,
            top,
            right - left,
            bottom - top);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

}
