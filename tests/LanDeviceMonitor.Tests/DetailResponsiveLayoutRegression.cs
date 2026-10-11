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
    private const double MinimumUsableWidth = 506;
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
                { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, Width = 520, Height = 1000, ShowActivated = false, ShowInTaskbar = false };
                typeof(MainWindow).GetField("_isPopulatingAppSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                var now = DateTimeOffset.Now;
                var device = new DeviceViewData
                {
                    Profile = new DeviceProfile { MacAddress = "CC:CC:CC:CC:CC:CC", LastRouterName = "竖屏布局测试", LastKnownOnline = true, LastConnectionType = 5, LastIpAddress = "200.200.200.200", FirstSeenAt = now.AddDays(-1), LastSeenAt = now },
                    Identity = new DeviceIdentity("手机", "\uE8EA", "竖屏布局测试"),
                    Sessions = new[] { new DeviceSession { MacAddress = "CC:CC:CC:CC:CC:CC", OnlineAt = now.AddHours(-2) } }
                };
                typeof(MainWindow).GetMethod("ReplaceDevices", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new[] { device }, false, false]);
                window.Show();
                var errors = new List<string>();
                var singleRows = 0; var inlineHeaders = 0;
                var widths = new[] { 520d, 500d, 504d, 505d, 506d, 507d, 508d, 510d, 560d, 600d, 700d, 800d, 898d, 902d, 920d, 1180d, 506d, 440d, 380d };
                var viewports = widths.Select(width => new Size(width, 1000)).Concat([new Size(506, 560), new Size(380, 400)]).ToArray();
                var naturalMinimumWindow = double.NaN;
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                foreach (var viewport in viewports)
                {
                    var width = viewport.Width;
                    ThemeService.Apply(mode); window.Width = width; window.Height = viewport.Height; Pump(30);
                    typeof(MainWindow).GetMethod("ShowDevicesPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [false, false]);
                    Pump(700); window.DetailPanel.ScrollToTop(); window.UpdateLayout();
                    Require(window.DetailSurface.IsVisible && window.DetailPanel.IsVisible, "Fixture is testing an invisible detail page.");
                    Require(window.ActualWidth >= MinimumUsableWidth - .1 && (width >= MinimumUsableWidth || window.ActualWidth <= MinimumUsableWidth + 1),
                        $"Window does not enforce the usable {MinimumUsableWidth} DIP minimum: requested={width}, actual={window.ActualWidth}");
                    if (window.ActualHeight < 559.9 || (viewport.Height < 560 && Math.Abs(window.ActualHeight - 560) > .1))
                        errors.Add($"Window does not enforce its existing 560 DIP minimum height: requested={viewport.Height}, actual={window.ActualHeight}");
                    var host = window.DetailNetworkMetadataRow;
                    var blocks = new[] { window.ConnectionStateInfoBlock, window.ConnectionInfoBlock, window.IpInfoBlock, window.MacInfoBlock };
                    var bounds = blocks.Select(b => b.TransformToAncestor(host).TransformBounds(new Rect(b.RenderSize))).ToArray();
                    if (bounds.All(b => Math.Abs(b.Y - bounds[0].Y) < 1)) singleRows++;
                    else errors.Add($"Metadata fields leave their single horizontal row at {width}: {string.Join(";", bounds.Select(b => b.ToString()))}");
                    for (var i = 0; i < bounds.Length; i++)
                    {
                        if (bounds[i].Left < -1 || bounds[i].Right > host.ActualWidth + 1 || bounds[i].Bottom > host.ActualHeight + 1)
                            errors.Add($"Metadata field overflows its visible row at {width}: {bounds[i]}, available={host.RenderSize}");
                        if (i > 0 && bounds[i].Left < bounds[i - 1].Right - 1)
                            errors.Add($"Metadata fields overlap at {width}: {bounds[i - 1]}; {bounds[i]}");
                        var scale = window.DetailConnectionText.FontSize / 14;
                        if (Math.Abs(scale - 1) > .001)
                            errors.Add($"Metadata needs smaller text inside a usable window at {width}: {scale:F3}");
                    }
                    if (double.IsNaN(naturalMinimumWindow) && window.ActualWidth < 900 && Math.Abs(window.DetailConnectionText.FontSize - 14) < .001)
                    {
                        var rowWidth = bounds.Sum(b => b.Width) + 3 * 8;
                        var chromeAndPadding = window.ActualWidth - host.ActualWidth;
                        naturalMinimumWindow = rowWidth + chromeAndPadding;
                        Console.WriteLine($"Natural metadata threshold: row={rowWidth:F2}, window insets={chromeAndPadding:F2}, needed window={naturalMinimumWindow:F2}, DPI={VisualTreeHelper.GetDpi(host).DpiScaleX:F2}.");
                        CheckLongestAddressSamples(window);
                    }
                    var visibleRow = host.TransformToAncestor(window).TransformBounds(new Rect(host.RenderSize));
                    if (visibleRow.Top < 0 || visibleRow.Bottom > window.ActualHeight - 8)
                        errors.Add($"The complete metadata row is not visible at the minimum height: viewport={window.RenderSize}, row={visibleRow}");
                    Console.WriteLine($"Metadata {mode} requested={viewport}, actual={window.RenderSize}: available={host.ActualWidth:F2}, valueFont={window.DetailConnectionText.FontSize:F2}, fields={string.Join(";", bounds.Select(b => $"{b.X:F2},{b.Y:F2} {b.Width:F2}x{b.Height:F2}"))}");
                    foreach (var block in blocks)
                    {
                        foreach (var text in Descendants<TextBlock>(block))
                        {
                            var measured = new FormattedText(text.Text, System.Globalization.CultureInfo.CurrentCulture, text.FlowDirection,
                                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip);
                            if (measured.WidthIncludingTrailingWhitespace > text.ActualWidth + 2)
                                errors.Add($"Metadata text clips at {width}: {text.Text}");
                            var naturalFontSize = text.Text is "连接状态" or "连接类型" or "IP 地址" or "MAC 地址" ? 12 : 14;
                            if (Math.Abs(text.FontSize / naturalFontSize - window.DetailConnectionText.FontSize / 14) > .001)
                                errors.Add($"Metadata labels and values use inconsistent scaling at {width}: {text.Text}, {text.FontSize}");
                            var textBounds = text.TransformToAncestor(host).TransformBounds(new Rect(text.RenderSize));
                            if (!text.IsVisible || text.TextTrimming != TextTrimming.None || text.TextWrapping != TextWrapping.NoWrap ||
                                textBounds.Left < -1 || textBounds.Right > host.ActualWidth + 1 || textBounds.Top < -1 || textBounds.Bottom > host.ActualHeight + 1)
                                errors.Add($"Metadata text is hidden, wrapped, trimmed or outside its row at {width}: {text.Text}, {textBounds}");
                        }
                    }
                    var values = new[] { window.DetailStatusText, window.DetailConnectionText, window.DetailIpAddressText, window.DetailMacAddressText };
                    var baselines = values.Select(text => text.TranslatePoint(new Point(0, text.BaselineOffset + text.Padding.Top), host).Y).ToArray();
                    if (baselines.Max() - baselines.Min() > 1)
                        errors.Add($"Metadata value baselines differ at {width}: {string.Join(";", baselines.Select(y => y.ToString("F2")))}");
                    CheckRenderedGaps(host, bounds, window.ActualWidth, mode, errors);
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
                        if (required > header.ActualWidth + 4 && Math.Abs(titleCenter.Y - segmentCenter.Y) <= 1)
                            errors.Add($"Traffic header did not use its narrow fallback at {width}.");
                    }
                    var left = segments.TranslatePoint(new Point(), header).X;
                    var right = segments.TranslatePoint(new Point(segments.ActualWidth, 0), header).X;
                    if (left < -1 || right > header.ActualWidth + 1) errors.Add($"Traffic slider overflows its header at {width}.");
                    if (segments.ActualWidth != 228 || segments.ActualHeight != 36 || segments.FontSize != 13)
                        errors.Add("Accepted segmented control sizing changed.");
                    if (window.ActualWidth >= MinimumUsableWidth - .1 && window.ActualWidth <= MinimumUsableWidth + 1 || window.ActualWidth == 1180)
                        Capture(window, mode, window.ActualWidth);
                }
                Require(errors.Count == 0, string.Join(Environment.NewLine, errors.Distinct()));
                Require(singleRows == viewports.Length * 2 && inlineHeaders > 0, "Fixture missed a responsive layout mode.");
                Require(naturalMinimumWindow > 505 && naturalMinimumWindow <= MinimumUsableWidth, $"The chosen minimum is not close to the full-font threshold: {naturalMinimumWindow:F2}.");
                CheckNarrowTrafficHeaderFallback();
                Console.WriteLine("Detail responsive layout PASS: light/dark usable 506–1180 DIP, smaller requests clamp to the minimum with physical-pixel rounding, 506x1000 portrait and 506x560 minimum height, four metadata fields at full 12/14 font sizes, widest legal IPv4/MAC, aligned baselines and clear rendered gaps; generic narrow traffic-header fallback retained.");
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
    private static void CheckNarrowTrafficHeaderFallback()
    {
        var header = new LanDeviceMonitor.Controls.AdaptiveColumns { Columns = 2, MinimumColumnWidth = 220, Justify = true, Gap = 10 };
        var title = new TextBlock { Text = "流量统计", FontSize = 16, Margin = new Thickness(19, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var segments = new LanDeviceMonitor.Controls.SegmentedControl { Width = 228, Height = 36, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right };
        header.Children.Add(title); header.Children.Add(segments);
        header.Measure(new Size(300, double.PositiveInfinity)); header.Arrange(new Rect(0, 0, 300, header.DesiredSize.Height));
        var titlePoint = title.TranslatePoint(new Point(), header); var segmentPoint = segments.TranslatePoint(new Point(), header);
        Require(segmentPoint.Y > titlePoint.Y + 1 && segmentPoint.X >= 0 && segmentPoint.X + segments.ActualWidth <= 301,
            "The original AdaptiveColumns narrow traffic-header fallback stopped stacking complete controls.");
        Require(segments.ActualWidth == 228 && segments.ActualHeight == 36 && segments.FontSize == 13,
            "The narrow traffic-header fallback changed the accepted segmented-control sizing.");
    }
    private static void CheckLongestAddressSamples(MainWindow window)
    {
        static double TextWidth(TextBlock text, string value) => new FormattedText(value,
            System.Globalization.CultureInfo.CurrentCulture, text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), 14, Brushes.Black,
            VisualTreeHelper.GetDpi(text).PixelsPerDip).WidthIncludingTrailingWhitespace;
        var mac = window.DetailMacAddressText;
        var widestMac = new[] { mac.Text }.Concat("0123456789ABCDEF".Select(character => string.Join(":", Enumerable.Repeat(new string(character, 2), 6))))
            .Select(value => (Value: value, Width: TextWidth(mac, value))).OrderByDescending(candidate => candidate.Width).First();
        var ip = window.DetailIpAddressText;
        var widestIp = new[] { ip.Text }.Concat(Enumerable.Range(100, 156).Select(octet => string.Join(".", Enumerable.Repeat(octet.ToString(), 4))))
            .Select(value => (Value: value, Width: TextWidth(ip, value))).OrderByDescending(candidate => candidate.Width).First();
        Console.WriteLine($"Longest address samples at font 14: MAC={widestMac.Value} ({widestMac.Width:F2} DIP), IPv4={widestIp.Value} ({widestIp.Width:F2} DIP).");
        Require(widestMac.Width <= TextWidth(mac, mac.Text) + .01, $"MAC fixture misses the widest legal hexadecimal glyphs: {widestMac.Value}.");
        Require(widestIp.Width <= TextWidth(ip, ip.Text) + .01, $"IPv4 fixture misses the widest legal octets: {widestIp.Value}.");
    }
    private static void CheckRenderedGaps(FrameworkElement host, Rect[] bounds, double width, string mode, List<string> errors)
    {
        var dpi = VisualTreeHelper.GetDpi(host);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var row = new Rect(host.RenderSize);
            dc.DrawRectangle(new VisualBrush(host) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect((Point)VisualTreeHelper.GetOffset(host), host.RenderSize) }, null, row);
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(host.ActualHeight * dpi.DpiScaleY),
            96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        if (width >= MinimumUsableWidth - .1 && width <= MinimumUsableWidth + 1 || width == 1180)
        {
            var folder = Path.Combine(Environment.CurrentDirectory, "runtime-data", "detail-metadata-single-row"); Directory.CreateDirectory(folder);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var label = width < 600 ? "min" : width.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var file = File.Create(Path.Combine(folder, $"row-{label}-{mode}.png")); encoder.Save(file);
        }
        var stride = bitmap.PixelWidth * 4; var pixels = new byte[stride * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, stride, 0);
        for (var i = 1; i < bounds.Length; i++)
        {
            var left = (int)Math.Ceiling(bounds[i - 1].Right * dpi.DpiScaleX) + 1;
            var right = (int)Math.Floor(bounds[i].Left * dpi.DpiScaleX) - 1;
            var ink = 0;
            for (var y = 0; y < bitmap.PixelHeight; y++)
            for (var x = left; x < right; x++)
                if (x >= 0 && x < bitmap.PixelWidth && pixels[y * stride + x * 4 + 3] > 32) ink++;
            if (ink > 0) errors.Add($"Rendered metadata ink crosses field gap {i} at {width}: {ink} pixels.");
        }
    }
    private static void Capture(MainWindow window, string mode, double width)
    {
        Pump(50);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * 1.25), (int)Math.Ceiling(window.ActualHeight * 1.25), 120, 120, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var folder = System.IO.Path.Combine(Environment.CurrentDirectory, "runtime-data", "detail-metadata-single-row"); System.IO.Directory.CreateDirectory(folder);
        var label = width < 600 ? "min" : width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var file = System.IO.File.Create(System.IO.Path.Combine(folder, $"detail-{label}x{window.ActualHeight}-{mode}.png")); encoder.Save(file);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
}
