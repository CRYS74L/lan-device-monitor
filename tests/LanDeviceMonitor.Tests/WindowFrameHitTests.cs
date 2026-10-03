using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LanDeviceMonitor;

internal static class WindowFrameHitTests
{
    [StructLayout(LayoutKind.Sequential)] private struct Bounds { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle,out Bounds bounds);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int l,int t,int r,int b);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window,IntPtr region);
    [DllImport("gdi32.dll")] private static extern int GetRgnBox(IntPtr region,out Bounds bounds);
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

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    public static void VerifyMaximized(MainWindow window)
    {
        var opacity=window.Opacity;
        window.Opacity=0;
        try
        {
            foreach(var monitor in System.Windows.Forms.Screen.AllScreens)
            {
                SetWindowPos(new WindowInteropHelper(window).Handle,IntPtr.Zero,monitor.WorkingArea.Left+20,monitor.WorkingArea.Top+20,500,600,0x14);
                window.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                VerifyMaximizedOnCurrentMonitor(window);
                Console.WriteLine("Monitor verified: "+monitor.DeviceName);
            }
        }
        finally {window.Left=-10000;window.Top=-10000;window.Opacity=opacity;window.Width=1180;window.Height=760;}
    }
    private static void VerifyMaximizedOnCurrentMonitor(MainWindow window)
    {
        var opacity=window.Opacity;
        window.Opacity=0;
        var handle=new WindowInteropHelper(window).Handle;
        var area=System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        try
        {
            window.WindowState=WindowState.Maximized;
            window.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();

            var rootPoint=window.WindowRoot.PointToScreen(new Point());
            if(!GetWindowRect(handle,out var bounds)) throw new Exception("无法读取最大化窗口范围");
            var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(window);
            if(rootPoint.X<area.Left-.1 || rootPoint.Y<area.Top-.1 || rootPoint.X+window.WindowRoot.ActualWidth*dpi.DpiScaleX>area.Right+.1 || rootPoint.Y+window.WindowRoot.ActualHeight*dpi.DpiScaleY>area.Bottom+.1)
                throw new Exception("最大化内容越过工作区");
            var region=CreateRectRgn(0,0,0,0);
            try {
                if(GetWindowRgn(handle,region)==0) throw new Exception("最大化缺少可见区域裁剪");
                GetRgnBox(region,out var clipped);
                if(bounds.Left+clipped.Left<area.Left || bounds.Top+clipped.Top<area.Top || bounds.Left+clipped.Right>area.Right || bounds.Top+clipped.Bottom>area.Bottom)
                    throw new Exception("最大化可见区域越过工作区");
            } finally {DeleteObject(region);}
            var title=window.WindowDragRegion.PointToScreen(new Point(120,22));
            if(Hit(handle,(int)title.X,(int)title.Y)!=2) throw new Exception("最大化标题栏没有交给原生标题栏移动处理");
            var button=window.MaximizeWindowButton.PointToScreen(new Point(15,14));
            if(Hit(handle,(int)button.X,(int)button.Y)!=1) throw new Exception("最大化时红绿灯按钮被标题栏截获");
            Console.WriteLine("Native maximize PASS: visible region and content inside monitor work area; caption HTCAPTION, controls HTCLIENT.");
        }
        finally {window.WindowState=WindowState.Normal;window.Left=-10000;window.Top=-10000;window.Opacity=opacity;}
    }
    private static int Hit(IntPtr handle,int x,int y) => SendMessage(handle,0x84,IntPtr.Zero,new IntPtr(unchecked((int)(((uint)y&0xffff)<<16|((uint)x&0xffff))))).ToInt32();
}
