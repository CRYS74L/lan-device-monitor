using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LanDeviceMonitor.Services;

public static class DesktopBackdropService
{
    private const uint SrcCopy = 0x00CC0020;
    private const int StretchHalftone = 4;
    private const int DownscaleDivisor = 8;
    private const int BlurRadius = 10;
    private const double SaturationFactor = 1.5d;

    public static BitmapSource? CaptureLiveBackdrop(
        IntPtr excludeWindowHandle)
    {
        var screenWidth = GetSystemMetrics(0);
        var screenHeight = GetSystemMetrics(1);
        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return null;
        }

        try
        {
            var smallWidth = screenWidth / DownscaleDivisor;
            var smallHeight = screenHeight / DownscaleDivisor;
            using var small = CaptureScaledScreen(
                smallWidth,
                smallHeight,
                screenWidth,
                screenHeight);
            if (small is null)
            {
                return null;
            }

            ExcludeOwnWindowRegion(small, excludeWindowHandle);

            var stride = smallWidth * 4;
            var pixels = new byte[smallHeight * stride];
            var bitmapData = small.LockBits(
                new System.Drawing.Rectangle(
                    0, 0, smallWidth, smallHeight),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                Marshal.Copy(
                    bitmapData.Scan0,
                    pixels,
                    0,
                    pixels.Length);
            }
            finally
            {
                small.UnlockBits(bitmapData);
            }

            ApplySaturation(pixels, SaturationFactor);
            ApplyBoxBlur(pixels, smallWidth, smallHeight, BlurRadius);

            var source = BitmapSource.Create(
                smallWidth,
                smallHeight,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                stride);
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    private static System.Drawing.Bitmap? CaptureScaledScreen(
        int smallWidth,
        int smallHeight,
        int screenWidth,
        int screenHeight)
    {
        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(
            screenDc,
            smallWidth,
            smallHeight);
        if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
        {
            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            if (screenDc != IntPtr.Zero)
            {
                _ = ReleaseDC(IntPtr.Zero, screenDc);
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
            _ = SetStretchBltMode(memoryDc, StretchHalftone);
            _ = StretchBlt(
                memoryDc,
                0,
                0,
                smallWidth,
                smallHeight,
                screenDc,
                0,
                0,
                screenWidth,
                screenHeight,
                SrcCopy);
            _ = SelectObject(memoryDc, previousObject);

            return System.Drawing.Image.FromHbitmap(bitmap)
                as System.Drawing.Bitmap;
        }
        finally
        {
            if (screenDc != IntPtr.Zero)
            {
                _ = ReleaseDC(IntPtr.Zero, screenDc);
            }

            DeleteObject(bitmap);
            DeleteDC(memoryDc);
        }
    }

    private static void ExcludeOwnWindowRegion(
        System.Drawing.Bitmap small,
        IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero ||
            DwmGetWindowAttribute(
                windowHandle,
                DwmExtendedFrameBounds,
                out var bounds,
                Marshal.SizeOf<NativeRect>()) != 0)
        {
            return;
        }

        var left = bounds.Left / DownscaleDivisor;
        var top = bounds.Top / DownscaleDivisor;
        var right = bounds.Right / DownscaleDivisor;
        var bottom = bounds.Bottom / DownscaleDivisor;
        if (right <= left || bottom <= top)
        {
            return;
        }

        var width = small.Width;
        var height = small.Height;
        var inset = 3;
        var sampleLeft = Math.Max(0, left - inset);
        var sampleTop = Math.Max(0, top - inset);
        var sampleRight = Math.Min(width - 1, right + inset);
        var sampleBottom = Math.Min(height - 1, bottom + inset);
        long sumR = 0;
        long sumG = 0;
        long sumB = 0;
        var count = 0;
        for (var y = sampleTop; y <= sampleBottom; y++)
        {
            for (var x = sampleLeft; x <= sampleRight; x++)
            {
                if (x >= left && x < right &&
                    y >= top && y < bottom)
                {
                    continue;
                }

                var pixel = small.GetPixel(x, y);
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
        using var graphics = System.Drawing.Graphics.FromImage(small);
        graphics.FillRectangle(
            brush,
            left,
            top,
            right - left,
            bottom - top);
    }

    private static void ApplySaturation(
        byte[] pixels,
        double factor)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var blue = pixels[i];
            var green = pixels[i + 1];
            var red = pixels[i + 2];
            var luminance =
                0.2126d * red +
                0.7152d * green +
                0.0722d * blue;
            pixels[i] = (byte)Math.Clamp(
                luminance + (blue - luminance) * factor,
                0,
                255);
            pixels[i + 1] = (byte)Math.Clamp(
                luminance + (green - luminance) * factor,
                0,
                255);
            pixels[i + 2] = (byte)Math.Clamp(
                luminance + (red - luminance) * factor,
                0,
                255);
        }
    }

    private static void ApplyBoxBlur(
        byte[] pixels,
        int width,
        int height,
        int radius)
    {
        var buffer = new byte[pixels.Length];
        Array.Copy(pixels, buffer, pixels.Length);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                long sumR = 0;
                long sumG = 0;
                long sumB = 0;
                var count = 0;
                for (var dx = -radius; dx <= radius; dx++)
                {
                    var xx = Math.Clamp(x + dx, 0, width - 1);
                    var index = (y * width + xx) * 4;
                    sumB += buffer[index];
                    sumG += buffer[index + 1];
                    sumR += buffer[index + 2];
                    count++;
                }

                var output = (y * width + x) * 4;
                pixels[output] = (byte)(sumB / count);
                pixels[output + 1] = (byte)(sumG / count);
                pixels[output + 2] = (byte)(sumR / count);
            }
        }

        Array.Copy(pixels, buffer, pixels.Length);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                long sumR = 0;
                long sumG = 0;
                long sumB = 0;
                var count = 0;
                for (var dy = -radius; dy <= radius; dy++)
                {
                    var yy = Math.Clamp(y + dy, 0, height - 1);
                    var index = (yy * width + x) * 4;
                    sumB += buffer[index];
                    sumG += buffer[index + 1];
                    sumR += buffer[index + 2];
                    count++;
                }

                var output = (y * width + x) * 4;
                pixels[output] = (byte)(sumB / count);
                pixels[output + 1] = (byte)(sumG / count);
                pixels[output + 2] = (byte)(sumR / count);
            }
        }
    }

    private const int DwmExtendedFrameBounds = 9;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr hWnd,
        int attribute,
        out NativeRect rect,
        int size);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

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
    private static extern int SetStretchBltMode(
        IntPtr hDc,
        int mode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StretchBlt(
        IntPtr hdcDestination,
        int xDestination,
        int yDestination,
        int widthDestination,
        int heightDestination,
        IntPtr hdcSource,
        int xSource,
        int ySource,
        int widthSource,
        int heightSource,
        uint rasterOperation);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hDc);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
