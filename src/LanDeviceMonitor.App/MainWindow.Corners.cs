using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace LanDeviceMonitor;

public partial class MainWindow
{
    private bool _cornerUpdatePending;
    private void QueueWindowCornerUpdate()
    {
        if (_cornerUpdatePending || !IsLoaded) return;
        _cornerUpdatePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            _cornerUpdatePending = false;
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero || WindowState == WindowState.Minimized) return;
            if (WindowState == WindowState.Maximized) { SetWindowRgn(handle, IntPtr.Zero, true); return; }
            if (!GetCornerWindowRect(handle, out var rect)) return;
            var radius = ((CornerRadius)FindResource("CardCornerRadius")).TopLeft;
            var dpi = VisualTreeHelper.GetDpi(this);
            var region = CreateRoundRectRgn(0, 0, rect.Right - rect.Left + 1, rect.Bottom - rect.Top + 1,
                (int)Math.Round(radius * 2 * dpi.DpiScaleX), (int)Math.Round(radius * 2 * dpi.DpiScaleY));
            // On success Windows owns the region; otherwise release it here.
            if (SetWindowRgn(handle, region, true) == 0) DeleteCornerRegion(region);
        }));
    }
    [StructLayout(LayoutKind.Sequential)] private struct CornerRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")] private static extern bool GetCornerWindowRect(IntPtr handle, out CornerRect rect);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr handle, IntPtr region, bool redraw);
    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")] private static extern bool DeleteCornerRegion(IntPtr region);
}
