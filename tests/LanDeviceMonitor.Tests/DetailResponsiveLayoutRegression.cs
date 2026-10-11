using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class DetailResponsiveLayoutRegression
{
    internal static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null; MainWindow? window = null;
            try
            {
                app = TestAppFactory.Create(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                window = new MainWindow(new NotificationService())
                { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, Width = 500, Height = 1000, ShowActivated = false, ShowInTaskbar = false };
                typeof(MainWindow).GetField("_isPopulatingAppSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                var now = DateTimeOffset.Now;
                var device = new DeviceViewData
                {
                    Profile = new DeviceProfile { MacAddress = "02:00:00:00:69:01", LastRouterName = "竖屏布局测试", LastKnownOnline = true, LastConnectionType = 1, LastIpAddress = "10.77.0.84", FirstSeenAt = now.AddDays(-1), LastSeenAt = now },
                    Identity = new DeviceIdentity("手机", "\uE8EA", "竖屏布局测试"),
                    Sessions = new[] { new DeviceSession { MacAddress = "02:00:00:00:69:01", OnlineAt = now.AddHours(-2) } }
                };
                typeof(MainWindow).GetMethod("ReplaceDevices", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new[] { device }, false, false]);
                window.Show();
                var errors = new List<string>();
                var pairs = 0; var singleRows = 0; var inlineHeaders = 0; var stackedHeaders = 0;
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                foreach (var width in new[] { 500d, 380d, 440d, 480d, 520d, 560d, 600d, 700d, 800d, 1180d, 500d })
                {
                    ThemeService.Apply(mode); window.Width = width; Pump(30);
                    typeof(MainWindow).GetMethod("ShowDevicesPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [false, false]);
                    Pump(700); window.DetailPanel.ScrollToTop(); window.UpdateLayout();
                    Require(window.DetailSurface.IsVisible && window.DetailPanel.IsVisible, "Fixture is testing an invisible detail page.");
                    Require(Math.Abs(window.ActualWidth - width) < 1, $"Restored desktop sizing cannot reach {width}: actual={window.ActualWidth}.");
                    var host = window.DetailNetworkMetadataRow;
                    var blocks = new[] { window.ConnectionStateInfoBlock, window.ConnectionInfoBlock, window.IpInfoBlock, window.MacInfoBlock };
                    var points = blocks.Select(b => b.TranslatePoint(new Point(b.ActualWidth / 2, 0), host)).ToArray();
                    if (points[1].Y > points[0].Y + 1)
                    {
                        pairs++;
                        if (Math.Abs(points[0].Y - points[2].Y) > 1 || Math.Abs(points[1].Y - points[3].Y) > 1 ||
                            points[2].X <= points[0].X || points[3].X <= points[1].X)
                            errors.Add($"Wrapped metadata must show status/IP above type/MAC at {width}: {string.Join(";", points.Select(p => p.ToString()))}");
                        if (Math.Abs(points[0].X - points[1].X) > 1 || Math.Abs(points[2].X - points[3].X) > 1)
                            errors.Add($"Wrapped metadata column centers differ at {width}.");
                    }
                    else
                    {
                        singleRows++;
                        if (points.Any(point => Math.Abs(point.Y - points[0].Y) > 1) ||
                            Enumerable.Range(1, points.Length - 1).Any(i => points[i].X <= points[i - 1].X))
                            errors.Add($"Wide metadata lost its original four-field order at {width}.");
                    }
                    foreach (var block in blocks)
                    {
                        foreach (var text in Descendants<TextBlock>(block))
                        {
                            var measured = new FormattedText(text.Text, System.Globalization.CultureInfo.CurrentCulture, text.FlowDirection,
                                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip);
                            if (measured.WidthIncludingTrailingWhitespace > text.ActualWidth + 2)
                                errors.Add($"Metadata text clips at {width}: {text.Text}");
                        }
                    }
                    var header = window.TrafficSummaryHeader;
                    var title = window.TrafficSummaryTitleText;
                    var segments = window.TrafficRangeSegments;
                    title.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var required = title.DesiredSize.Width + segments.DesiredSize.Width + header.Gap + 4;
                    var titleCenter = title.TranslatePoint(new Point(0, title.ActualHeight / 2), header);
                    var segmentCenter = segments.TranslatePoint(new Point(0, segments.ActualHeight / 2), header);
                    if (required <= header.ActualWidth)
                    {
                        inlineHeaders++;
                        if (Math.Abs(titleCenter.Y - segmentCenter.Y) > 1)
                            errors.Add($"Traffic header wrapped despite fitting at {width}: available={header.ActualWidth}, needed={required}");
                    }
                    else
                    {
                        stackedHeaders++;
                        if (required > header.ActualWidth + 4 && Math.Abs(titleCenter.Y - segmentCenter.Y) <= 1)
                            errors.Add($"Traffic header did not use its narrow fallback at {width}.");
                    }
                    var left = segments.TranslatePoint(new Point(), header).X;
                    var right = segments.TranslatePoint(new Point(segments.ActualWidth, 0), header).X;
                    if (left < -1 || right > header.ActualWidth + 1) errors.Add($"Traffic slider overflows its header at {width}.");
                    if (segments.ActualWidth != 228 || segments.ActualHeight != 36 || segments.FontSize != 13)
                        errors.Add("Accepted segmented control sizing changed.");
                    if (width == 500) Capture(window, mode);
                }
                Require(pairs > 0 && singleRows > 0 && inlineHeaders > 0 && stackedHeaders > 0, "Fixture missed a responsive layout mode.");
                Require(errors.Count == 0, string.Join(Environment.NewLine, errors.Distinct()));
                Console.WriteLine("Detail responsive layout PASS: light/dark 380–1180, status/IP above type/MAC, original font sizes and wide order, complete labels, fitting traffic headers inline, narrow fallback and unchanged slider sizing.");
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(45))) { Console.Error.WriteLine("Detail layout regression timed out."); Environment.Exit(1); }
        if (failure is null) return 0; Console.Error.WriteLine(failure); return 1;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match; foreach (var item in Descendants<T>(child)) yield return item; }
    }
    private static void Capture(MainWindow window, string mode)
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen())
        { var bounds = new Rect(0, 0, window.ActualWidth, window.ActualHeight); dc.DrawRectangle(Brushes.White, null, bounds); dc.DrawRectangle(new VisualBrush(window), null, bounds); }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * 1.25), (int)Math.Ceiling(window.ActualHeight * 1.25), 120, 120, PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var folder = System.IO.Path.Combine(Environment.CurrentDirectory, "runtime-data", "detail-wrap-dev287"); System.IO.Directory.CreateDirectory(folder);
        using var file = System.IO.File.Create(System.IO.Path.Combine(folder, $"detail-500-{mode}.png")); encoder.Save(file);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
}
