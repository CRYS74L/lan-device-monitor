using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Controls;
using LanDeviceMonitor.Services;

internal static class ViewportFadeRegression
{
    internal static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null; MainWindow? resources = null; Window? host = null;
            try
            {
                app = TestAppFactory.Create(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                resources = new MainWindow(new NotificationService());
                var sections = new StackPanel { Margin = new Thickness(28, 24, 28, 36) };
                for (var i = 0; i < 8; i++) sections.Children.Add(new Border
                {
                    Height = i == 4 ? 1100 : 180, Margin = new Thickness(0, 0, 0, 16), CornerRadius = new CornerRadius(20),
                    Background = new SolidColorBrush(Color.FromArgb(100, (byte)(i * 23), 120, 210)),
                    BorderBrush = Brushes.Coral, BorderThickness = new Thickness(1),
                    Child = new TextBlock { Text = $"渐隐质量测试 {i + 1}", FontSize = 22, Margin = new Thickness(18) }
                });
                var viewer = new ScrollViewer { Content = sections, Style = (Style)resources.FindResource("FadingDetailScrollViewerStyle") };
                DeviceListViewport.SetEnabled(viewer, true);
                host = new Window { Content = viewer, Width = 650, Height = 550, Left = -10000, Top = -10000,
                    ShowActivated = false, ShowInTaskbar = false, Background = Brushes.LightGoldenrodYellow };
                host.Show(); host.UpdateLayout(); Pump(100);
                foreach (var offset in new[] { 0d, 20, 160, 480, 1100, 100000 })
                {
                    viewer.ScrollToVerticalOffset(offset); Pump(60); host.UpdateLayout();
                    foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
                    {
                        DeviceListViewport.SetDistributeFade(viewer, false); host.UpdateLayout();
                        var original = Capture(viewer, scale);
                        DeviceListViewport.SetDistributeFade(viewer, true); host.UpdateLayout();
                        var distributed = Capture(viewer, scale);
                        var differences = original.Zip(distributed, (a, b) => Math.Abs(a - b)).ToArray();
                        Console.WriteLine($"Viewport fade offset={viewer.VerticalOffset:F0}, DPI={scale}: max={differences.Max()}, mean={differences.Average():F5}");
                        if (differences.Max() > 3 || differences.Average() > .02)
                            throw new InvalidOperationException("Distributed fade changed the displayed edge gradient.");
                    }
                }
                Console.WriteLine("Viewport fade PASS: equivalent pixels across scrolling and DPI, including long history sections.");
            }
            catch (Exception e) { failure = e; }
            finally { host?.Close(); resources?.PrepareForExit(); resources?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(25))) { Console.Error.WriteLine("Viewport fade regression timed out."); Environment.Exit(1); }
        if (failure is null) return 0; Console.Error.WriteLine(failure); return 1;
    }

    private static byte[] Capture(FrameworkElement element, double scale)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * scale),
            (int)Math.Ceiling(element.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
}
