using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LanDeviceMonitor.Services;

internal static class WindowWorkArea
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window,out Rect bounds);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window,IntPtr region,bool redraw);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    internal static void Update(Window window,FrameworkElement root)
    {
        var handle=new WindowInteropHelper(window).Handle;
        if(handle==IntPtr.Zero) return;
        if(window.WindowState!=WindowState.Maximized)
        { root.Margin=new Thickness(0);SetWindowRgn(handle,IntPtr.Zero,true);return; }
        var work=System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        if(!GetWindowRect(handle,out var bounds)) return;
        var dpi=VisualTreeHelper.GetDpi(window);
        // Keep native maximize/snap and its invisible resize frame, but clip the
        // full-glass material and place all client content within the work area.
        root.Margin=new Thickness(Math.Max(0,work.Left-bounds.Left)/dpi.DpiScaleX,
            Math.Max(0,work.Top-bounds.Top)/dpi.DpiScaleY,
            Math.Max(0,bounds.Right-work.Right)/dpi.DpiScaleX,
            Math.Max(0,bounds.Bottom-work.Bottom)/dpi.DpiScaleY);
        var region=CreateRectRgn(work.Left-bounds.Left,work.Top-bounds.Top,work.Right-bounds.Left,work.Bottom-bounds.Top);
        if(SetWindowRgn(handle,region,true)==0) DeleteObject(region);
    }
}
