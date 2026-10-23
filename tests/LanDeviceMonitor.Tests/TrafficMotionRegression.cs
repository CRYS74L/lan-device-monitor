using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Controls;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class TrafficMotionRegression
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static int Run(bool profileOnly = false)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null;
            MainWindow? window = null;
            try
            {
                foreach (var duration in new[] { .22, .48 })
                {
                    var motion = new ChartValueMotion(1, 10, 100);
                    Require(motion.Read(100, duration) == 1 && motion.Read(100 + duration / 2, duration) is > 1 and < 10,
                        "The transition skipped its start or intermediate frame.");
                    Require(Math.Abs(motion.Read(100 + duration, duration) - 10) < 1e-10,
                        "The transition did not finish within the selector duration.");
                    var reversal = motion.Retarget(5, 100 + duration / 3, duration);
                    Require(reversal.From == motion.Read(100 + duration / 3, duration), "Rapid reversal jumped to an old endpoint.");
                    Require(reversal.Read(101, duration) == 5 && motion.Read(100, 0) == 10,
                        "Retargeting or disabled animations did not settle.");
                }

                app = TestAppFactory.Create();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeService.Apply(ThemeService.LightMode);
                window = new MainWindow(new NotificationService())
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000, Top = -10000, Width = 1180, Height = 850,
                    ShowActivated = false, ShowInTaskbar = false
                };
                typeof(MainWindow).GetField("_isPopulatingAppSettings", PrivateInstance)!.SetValue(window, true);
                Require(window.TrafficRangeMinutes == 1 && window.TrafficRangeSegments.SelectedIndex == 0,
                    "The initial selection must be one minute.");
                var now = DateTimeOffset.Now;
                now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
                var fixture = new DeviceViewData
                {
                    Profile = new DeviceProfile
                    {
                        MacAddress = "02:00:00:00:76:01", LastRouterName = "图表动画测试设备",
                        LastKnownOnline = true, FirstSeenAt = now, LastSeenAt = now,
                        LastIpAddress = "192.0.2.76"
                    },
                    Identity = new DeviceIdentity("笔记本电脑", "\uE770", "图表动画测试设备"),
                    Sessions = []
                };
                typeof(MainWindow).GetMethod("ReplaceDevices", PrivateInstance)!.Invoke(window, [new[] { fixture }, false, false]);
                window.Show(); window.UpdateLayout(); Pump(650);
                var chart = Descendants<TrafficChart>(window).Single();
                chart.Samples = Enumerable.Range(0, 601).Select(i => new TrafficSample(now.AddSeconds(i - 600),
                    (long)(10000 + 7000 * Math.Sin(i * .17)), (long)(50000 + 40000 * Math.Cos(i * .07)))).ToArray();
                Pump(1100);
                Require(chart.IsVisible && chart.ActualWidth > 100 && chart.RangeMinutes == 1,
                    "The fixture is not exercising the visible, bound chart.");
                Require(chart.AnimationMilliseconds == window.TrafficRangeSegments.AnimationMilliseconds,
                    "The chart and range selector use different durations.");
                if (profileOnly) { Profile(chart); return; }
                foreach (var range in new[] { 1, 5, 10 })
                for (var fraction = 0; fraction < 1000; fraction += 17)
                {
                    var end = now.AddMilliseconds(fraction);
                    var ticks = TrafficChart.GetTimeAxisTicks(end.AddMinutes(-range), end, range, 60, 820);
                    Require(ticks.Count == (range == 10 ? 8 : 6) && ticks[0].X == 60 && ticks[^1].X == 820,
                        "Fractional-second scrolling dropped a time coordinate.");
                    Require(ticks[0].Timestamp == end.AddMinutes(-range) && ticks[^1].Timestamp == end,
                        "Time coordinates do not match the displayed viewport.");
                }
                var series = typeof(TrafficChart).GetField("_downloadSeries", PrivateInstance)!.GetValue(chart)!;
                var retainedLine = (GeometryGroup)series.GetType().GetField("_line", PrivateInstance)!.GetValue(series)!;

                if (SystemParameters.ClientAreaAnimation)
                {
                    foreach (var (index, target) in new[] { (1, 5d), (2, 10d), (0, 1d) })
                    {
                        var from = PresentationRange(chart);
                        var before = Capture(chart, $"range-{target}-before");
                        var observation = Stopwatch.StartNew();
                        window.TrafficRangeSegments.SelectedIndex = index;
                        Pump(65);
                        var middle = PresentationRange(chart);
                        Console.WriteLine($"Range observation {from:0}->{target:0}: {observation.Elapsed.TotalMilliseconds:F1}ms, presented={middle:F4}.");
                        var middlePixels = Capture(chart, $"range-{target}-middle");
                        Require(retainedLine.Children.Single().IsFrozen,
                            "The reprojected ECharts path must be frozen for rendering.");
                        Require(chart.Samples.Count == 601 && chart.Samples[300].Timestamp == now.AddSeconds(-300),
                            "Reprojecting the curve changed measured sample times.");
                        Require(middle > Math.Min(from, target) && middle < Math.Max(from, target),
                            "Switching the range snapped instead of animating.");
                        var indicator = (Border)window.TrafficRangeSegments.Template.FindName("PART_Indicator", window.TrafficRangeSegments);
                        var indicatorIndex = ((TranslateTransform)indicator.RenderTransform).X / indicator.ActualWidth;
                        var fromIndex = from == 1 ? 0 : from == 5 ? 1 : 2;
                        var sliderProgress = (indicatorIndex - fromIndex) / (index - fromIndex);
                        var curveProgress = (middle - from) / (target - from);
                        Require(Math.Abs(sliderProgress - curveProgress) < .22,
                            $"Slider and curve are out of sync: {sliderProgress:F3}/{curveProgress:F3}.");
                        Pump((int)chart.AnimationMilliseconds + 60);
                        Require(PresentationRange(chart) == target, "The chart continued changing range after the slider finished.");
                        var after = Capture(chart, $"range-{target}-after");
                        Require(!before.SequenceEqual(middlePixels) && !middlePixels.SequenceEqual(after),
                            "Rendered intermediate and final curve frames did not change.");
                        Console.WriteLine($"Range {from:0}->{target:0} PASS: slider={sliderProgress:F3}, curve={curveProgress:F3}, settled at {chart.AnimationMilliseconds:0}ms.");
                    }
                    window.TrafficRangeSegments.SelectedIndex = 2; Pump(55);
                    var interruptedMotion = (ChartValueMotion)typeof(TrafficChart).GetField("_rangeMotion", PrivateInstance)!.GetValue(chart)!;
                    window.TrafficRangeSegments.SelectedIndex = 1;
                    var reversedMotion = (ChartValueMotion)typeof(TrafficChart).GetField("_rangeMotion", PrivateInstance)!.GetValue(chart)!;
                    Require(Math.Abs(reversedMotion.From - interruptedMotion.Read(reversedMotion.Started, chart.AnimationMilliseconds / 1000)) < .0001,
                        "Rapid selector reversal jumped from the current animation value.");
                    Pump(40);
                    Require(PresentationRange(chart) >= Math.Min(reversedMotion.From, 5) && PresentationRange(chart) <= Math.Max(reversedMotion.From, 5),
                        "Rapid selector reversal did not continue from the displayed curve.");
                    Pump(300);
                    Require(PresentationRange(chart) == 5, "Rapid switching did not settle at the final range.");
                }
                window.TrafficRangeSegments.AnimationMilliseconds = 0;
                window.TrafficRangeSegments.SelectedIndex = 0; Pump(40);
                Require(chart.AnimationMilliseconds == 0 && PresentationRange(chart) == 1,
                    "A changed selector duration was not propagated to the chart.");
                typeof(MainWindow).GetMethod("ShowSettingsPanel", PrivateInstance)!.Invoke(window, null);
                Pump(650);
                window.SettingsPanel.ScrollToBottom(); Pump(100);
                Require(window.RepositoryLink.NavigateUri.AbsoluteUri == "https://github.com/CRYS74L/lan-device-monitor" &&
                    window.RepositoryLink.IsEnabled && window.RepositoryLink.Focusable && window.AppVersionRun.Text == MainWindow.GetDisplayVersion(),
                    "The version is not a usable repository hyperlink.");
                Require(window.RepositoryIcon.IsVisible && window.RepositoryIcon.ActualWidth == 14 &&
                    !window.RepositoryIcon.Data.Bounds.IsEmpty && window.RepositoryIcon.Fill is SolidColorBrush,
                    "The GitHub mark is missing or did not inherit the link color.");
                var versionStart = window.AppVersionRun.ContentStart.GetCharacterRect(LogicalDirection.Forward).Left;
                var iconEnd = window.RepositoryIcon.TranslatePoint(new Point(window.RepositoryIcon.ActualWidth, 0), window.AppVersionText).X;
                var separatorEnd = window.VersionSeparator.TranslatePoint(new Point(window.VersionSeparator.ActualWidth, 0), window.AppVersionText).X;
                var iconStart = window.RepositoryIcon.TranslatePoint(new Point(0, 0), window.AppVersionText).X;
                Require(window.VersionSeparator.Margin.Left == 7 && window.VersionSeparator.Margin.Right == 7 &&
                    Math.Abs((versionStart - iconEnd) - (iconStart - separatorEnd)) < .8,
                    $"Version footer gaps differ: before={iconStart-separatorEnd:F2}px, after={versionStart-iconEnd:F2}px.");
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                {
                    ThemeService.Apply(mode); Pump(40);
                    Capture(window, $"settings-{mode}");
                    CheckFooterAlignment(window, mode);
                }
                Console.WriteLine("Traffic motion PASS: complete time coordinates, frozen ECharts paths, one-minute default, shared duration, rapid reversals, disabled motion, and light/dark version footer spacing.");
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(35))) { Console.Error.WriteLine("Traffic motion regression timed out."); Environment.Exit(1); }
        if (failure is null) return 0;
        Console.Error.WriteLine(failure); return 1;
    }

    private static double PresentationRange(TrafficChart chart) =>
        (double)typeof(TrafficChart).GetField("_presentationRangeMinutes", PrivateInstance)!.GetValue(chart)!;

    private static void CheckFooterAlignment(MainWindow window, string theme)
    {
        var footer = (FrameworkElement)window.AppVersionText.Parent;
        var footerPosition = footer.TranslatePoint(new Point(), window);
        var icon = window.RepositoryIcon.TranslatePoint(new Point(), window);
        var version = window.AppVersionRun.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        version.Offset(window.AppVersionText.TranslatePoint(new Point(), window).X, 0);
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * scale),
                (int)Math.Ceiling(window.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            var background = ((int)(footerPosition.Y * scale) * bitmap.PixelWidth + (int)((footerPosition.X - 3) * scale)) * 4;
            double Center(double left, double right)
            {
                var top = bitmap.PixelHeight; var bottom = -1;
                for (var x = Math.Max(0, (int)Math.Ceiling(left * scale)); x < Math.Min(bitmap.PixelWidth, (int)Math.Floor(right * scale)); x++)
                for (var y = (int)(footerPosition.Y * scale); y < Math.Min(bitmap.PixelHeight, (int)Math.Ceiling((footerPosition.Y + footer.ActualHeight) * scale)); y++)
                {
                    var offset = (y * bitmap.PixelWidth + x) * 4;
                    var ink = Math.Abs(pixels[offset] - pixels[background]) + Math.Abs(pixels[offset + 1] - pixels[background + 1]) + Math.Abs(pixels[offset + 2] - pixels[background + 2]);
                    if (ink > 100) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
                }
                Require(bottom >= top, "The footer alignment check did not capture visible ink.");
                return (top + bottom) / (2 * scale);
            }
            var iconCenter = Center(icon.X, icon.X + window.RepositoryIcon.ActualWidth);
            var textCenter = Center(version.Left, footerPosition.X + footer.ActualWidth);
            var pixelDifference = Math.Abs(iconCenter - textCenter) * scale;
            Console.WriteLine($"Footer {theme} {scale:P0}: icon/text outline centers differ by {pixelDifference:F2} physical pixels.");
            Require(pixelDifference <= .5001, "The GitHub mark is not vertically centered with the version text.");
        }
    }

    private static void Profile(TrafficChart chart)
    {
        var render = typeof(TrafficChart).GetMethod("OnRender", PrivateInstance)!;
        var visual = new DrawingVisual();
        foreach (var range in new[] { 1, 5, 10 })
        {
            chart.RangeMinutes = range; Pump(300);
            for (var i = 0; i < 20; i++) { using var dc = visual.RenderOpen(); render.Invoke(chart, [dc]); }
            var frames = new double[240];
            var allocations = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < frames.Length; i++)
            {
                var started = Stopwatch.GetTimestamp();
                using (var dc = visual.RenderOpen()) render.Invoke(chart, [dc]);
                frames[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            }
            allocations = GC.GetAllocatedBytesForCurrentThread() - allocations;
            Require(allocations / frames.Length < 100000,
                $"Viewport rendering allocated {allocations / frames.Length} bytes/frame; retained paths should stay below 100 KB.");
            Array.Sort(frames);
            Console.WriteLine($"Render profile range={range}: median={frames[120]:F3}ms, p95={frames[228]:F3}ms, max={frames[^1]:F3}ms, allocated={allocations / frames.Length} bytes/frame.");
        }
    }

    private static byte[] Capture(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var folder = System.IO.Path.Combine(Environment.CurrentDirectory, "runtime-data", "traffic-motion-v0.1.10");
        System.IO.Directory.CreateDirectory(folder);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(System.IO.Path.Combine(folder, name + ".png")); encoder.Save(file);
        return pixels;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var item in Descendants<T>(child)) yield return item;
        }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        // Continuous rendering must not starve the timer that samples a frame.
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
