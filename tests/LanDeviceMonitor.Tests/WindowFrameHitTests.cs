using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LanDeviceMonitor;

internal static class WindowFrameHitTests
{
    [StructLayout(LayoutKind.Sequential)] private struct Bounds { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle,out Bounds bounds);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr wParam,IntPtr lParam);
    public static void Verify(MainWindow window)
    {
        var handle=new WindowInteropHelper(window).Handle;
        if(!GetWindowRect(handle,out var r)) throw new InvalidOperationException("无法读取窗口边界");
        int midX=(r.Left+r.Right)/2, midY=(r.Top+r.Bottom)/2;
        foreach(var (x,y,expected) in new[] { (r.Left+2,midY,10),(r.Right-3,midY,11),(midX,r.Top+2,12),(midX,r.Bottom-3,15),(r.Left+3,r.Top+3,13),(r.Right-4,r.Top+3,14),(r.Left+3,r.Bottom-4,16),(r.Right-4,r.Bottom-4,17) })
        {
            int hit=Hit(handle,x,y);
            if(hit!=expected) throw new InvalidOperationException($"窗口缩放命中错误: expected={expected}, actual={hit}");
        }
        var point=window.MinimizeWindowButton.PointToScreen(new Point(window.MinimizeWindowButton.ActualWidth/2,window.MinimizeWindowButton.ActualHeight/2));
        if(Hit(handle,(int)point.X,(int)point.Y)!=1) throw new InvalidOperationException("窗口按钮被非客户区拦截");
        Console.WriteLine("Native frame hit tests PASS: four edges, four corners, control client area.");
    }
    private static int Hit(IntPtr handle,int x,int y) => SendMessage(handle,0x84,IntPtr.Zero,new IntPtr(unchecked((int)(((uint)y&0xffff)<<16|((uint)x&0xffff))))).ToInt32();
}
