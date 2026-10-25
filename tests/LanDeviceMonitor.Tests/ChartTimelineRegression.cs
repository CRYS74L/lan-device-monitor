using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Controls;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;
using LanDeviceMonitor.ViewModels;

internal static class ChartTimelineRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<string> Failures = [];

    internal static int Run()
    {
        var clock = new ChartTimeline();
        clock.Observe(1000, 5, 50);
        Check(clock.Read(50) == 993, "First display enters the current buffered window without replaying history");
        clock.Observe(1300, 5, 350);
        Check(clock.Read(350) == 1293, "A five-minute rendering pause resumes at the current buffered time");
        var resumed = clock.Read(350);
        Check(Math.Abs(clock.Read(351) - resumed - 1) < .001, "Resumed time advances at one second per second");
        var currentTime = WholeSecond(DateTimeOffset.Now);
        var oldSamples = new[] { new TrafficSample(currentTime.AddHours(-8), 10, 100), new TrafficSample(currentTime.AddHours(-8).AddSeconds(5), 10, 100) };
        var visible = (IReadOnlyList<TrafficSample>)typeof(TrafficChart).GetMethod("GetVisibleSamples", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [oldSamples, 1, currentTime])!;
        Check(visible.Count == 0, "Expired history does not create a long sequence of artificial time slots");
        var curve = new[] { new System.Windows.Point(0,100), new System.Windows.Point(5,100), new System.Windows.Point(10,1000000), new System.Windows.Point(15,100) };
        var segments = EChartsCurve.Build(curve);
        Check(SampleCurve.VisibleBounds(curve, segments, -60, 5)?.Maximum == 100, "A wholly hidden peak cannot enlarge the Y axis");
        var entering = SampleCurve.VisibleBounds(curve, segments, -54, 6)!.Value;
        Check(entering.Maximum is > 10000 and < 200000, "The visible rise is measured at the exact right clipping boundary");
        Check(SampleCurve.VisibleBounds(curve, segments, -50, 10)?.Maximum == 1000000, "The full peak contributes when it enters the viewport");

        Exception? error = null;
        var thread = new Thread(() =>
        {
            App? app = null;
            MainWindow? window = null;
            try
            {
                app = TestAppFactory.Create(); app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeService.Apply(ThemeService.LightMode);
                window = new MainWindow(new NotificationService())
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000, Top = -10000, Width = 1180, Height = 850,
                    ShowActivated = false, ShowInTaskbar = false
                };
                typeof(MainWindow).GetField("_isPopulatingAppSettings", Private)!.SetValue(window, true);
                var now = WholeSecond(DateTimeOffset.Now);
                var first = Device("02:00:00:00:78:01", "A 图表测试设备", now);
                var second = Device("02:00:00:00:78:02", "B 图表测试设备", now);
                var getHistory = typeof(MainWindow).GetMethod("GetTrafficHistory", Private)!;
                var firstHistory = (TrafficHistoryBuffer)getHistory.Invoke(window, [first.Profile.MacAddress])!;
                var secondHistory = (TrafficHistoryBuffer)getHistory.Invoke(window, [second.Profile.MacAddress])!;
                firstHistory.Append(new(now.AddSeconds(-5), 10, 100)); firstHistory.Append(new(now, 20, 120));
                for (var i = 0; i <= 24; i++)
                    secondHistory.Append(new(now.AddSeconds((i - 24) * 5), 20, i == 24 ? 200000 : i == 21 ? 80000 : 100));
                typeof(MainWindow).GetMethod("ReplaceDevices", Private)!.Invoke(window, [new[] { first, second }, false, false]);
                window.Show(); window.UpdateLayout(); Pump(650);
                var secondView = window.DeviceList.Items.Cast<DeviceItemViewModel>().Single(d => d.MacAddress == second.Profile.MacAddress);
                // The unselected view still holds its previous visit's snapshot.
                secondView.Update(secondView.Data, [new(now.AddMinutes(-3), 10, 100), new(now.AddMinutes(-3).AddSeconds(5), 10, 100)]);
                window.DeviceList.SelectedItem = secondView;
                Pump(240);
                var chart = Descendants<TrafficChart>(window).Single();
                var displayed = (IReadOnlyList<TrafficSample>)typeof(TrafficChart).GetField("_renderSamples", Private)!.GetValue(chart)!;
                var top = (ChartValueMotion?)typeof(TrafficChart).GetField("_scaleTopMotion", Private)!.GetValue(chart);
                Check(window.SelectedDevice == secondView && displayed.Count > 0 && displayed[^1].Timestamp == now,
                    "The entrance animation starts with the selected device's fresh history");
                Check((bool)typeof(MainWindow).GetField("_deviceDetailAnimationCacheActive", Private)!.GetValue(window)!,
                    "The selected-device checks run before its entrance animation completes");
                Check(top?.From is > 80000 and < 100000 && top?.To is > 80000 and < 100000,
                    "The new device's visible peak sets its Y axis during entrance; its hidden peak does not");
                Capture(window, "device-entry");
                Pump(650);
                var presentation = (DateTimeOffset)typeof(TrafficChart).GetField("_presentationTime", Private)!.GetValue(chart)!;
                Check((DateTimeOffset.Now - presentation).TotalSeconds is >= 6.9 and < 8,
                    "Switching devices does not fast-forward through the old cached window");

                chart.DeviceKey = "peak-fixture";
                var peakAt = WholeSecond(DateTimeOffset.Now);
                chart.Samples = [new(peakAt.AddSeconds(-30), 100, 100), new(peakAt.AddSeconds(-4), 100, 100), new(peakAt, 100, 900000)];
                Pump(80);
                top = (ChartValueMotion?)typeof(TrafficChart).GetField("_scaleTopMotion", Private)!.GetValue(chart);
                Check(top?.To == 120, "Receiving a buffered peak does not compress the still-flat visible history");
                Capture(chart, "peak-hidden");
                var riseDeadline = DateTimeOffset.Now.AddSeconds(5);
                while (DateTimeOffset.Now < riseDeadline && (DateTimeOffset)typeof(TrafficChart).GetField("_presentationTime", Private)!.GetValue(chart)! < peakAt.AddSeconds(-3)) Pump(20);
                top = (ChartValueMotion?)typeof(TrafficChart).GetField("_scaleTopMotion", Private)!.GetValue(chart);
                Check(top?.To is > 10000 and < 900000, "The Y axis expands with the visible part of the incoming rise");
                Capture(chart, "peak-entering");
                var deadline = DateTimeOffset.Now.AddSeconds(7);
                while (DateTimeOffset.Now < deadline && (DateTimeOffset)typeof(TrafficChart).GetField("_presentationTime", Private)!.GetValue(chart)! < peakAt.AddMilliseconds(50)) Pump(20);
                var transform = (MatrixTransform)typeof(TrafficChart).GetField("_plotTransform", Private)!.GetValue(chart)!;
                var origin = (DateTimeOffset)typeof(TrafficChart).GetField("_sampleOrigin", Private)!.GetValue(chart)!;
                var peak = transform.Transform(new System.Windows.Point((peakAt - origin).TotalSeconds, -900000 / 1024d));
                Check(peak.X >= 60 && peak.X <= chart.ActualWidth - 16,
                    "The buffered peak reaches the viewport at its displayed time");
                Check(peak.Y >= 23 && peak.Y <= chart.ActualHeight - 32,
                    "The full peak and its Y scale are visible in the same frame");
                Capture(chart, "peak-visible");
                window.Hide(); Pump(120); window.Show(); Pump(45);
                presentation = (DateTimeOffset)typeof(TrafficChart).GetField("_presentationTime", Private)!.GetValue(chart)!;
                Check((DateTimeOffset.Now - presentation).TotalSeconds is >= 6.9 and < 8,
                    "Resuming a hidden window does not replay buffered time");

                var zeroAt = WholeSecond(DateTimeOffset.Now);
                chart.Samples = [new(zeroAt.AddSeconds(-30), 0, 0) { HasUploadSample = false }, new(zeroAt, 0, 0) { HasUploadSample = false }];
                chart.GridBrush = Brushes.Transparent; chart.LabelBrush = Brushes.Transparent; chart.DownloadBrush = Brushes.Lime;
                Pump(350);
                var bitmap = new RenderTargetBitmap((int)chart.ActualWidth, (int)chart.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(chart);
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                var x = (int)(chart.ActualWidth - 60);
                var y = (int)(chart.ActualHeight - 32);
                var offset = (y * bitmap.PixelWidth + x) * 4;
                Check(pixels[offset + 1] > 100 && pixels[offset + 3] > 100,
                    "The zero-valued stroke remains visible below the baseline center");
                var liveAt = WholeSecond(DateTimeOffset.Now);
                chart.Samples = [new(liveAt.AddSeconds(-10), 0, 1024) { HasUploadSample = false }, new(liveAt.AddSeconds(-5), 0, 1024) { HasUploadSample = false }, new(liveAt, 0, 1024) { HasUploadSample = false }];
                foreach (var range in new[] { 1, 5, 10 })
                {
                    chart.RangeMinutes = range; Pump(300);
                    Check(HasGreenAtRight(chart, 1024), $"The live {range}-minute curve reaches the right edge between real samples");
                }
                Check(chart.Samples.Count == 3, "Buffered presentation does not add fabricated samples");
                Capture(chart, "continuous-buffered-edge");
                chart.DeviceKey = "offset-right-edge";
                var receivedAt = DateTimeOffset.Now.AddSeconds(-4.8);
                chart.Samples = Enumerable.Range(0, 15).Select(i => new TrafficSample(receivedAt.AddSeconds((i - 14) * 5 - 4), 0, 1024)
                    { ReceivedAt = receivedAt.AddSeconds((i - 14) * 5), IntervalSeconds = 5, HasUploadSample = false }).ToArray();
                Pump(120);
                Check(HasGreenAtRight(chart, 1024), "A four-second sampling phase offset cannot leave an empty right edge");
                var phasePresentation = (DateTimeOffset)typeof(TrafficChart).GetField("_presentationTime", Private)!.GetValue(chart)!;
                Check(Math.Abs((DateTimeOffset.Now - phasePresentation).TotalSeconds - 11) < .2,
                    "The viewport applies the four-second coordinate phase while keeping the seven-second receive-time buffer");
                Capture(chart, "sampling-phase-right-edge");
                chart.DeviceKey = "sampling-interruption";
                chart.Samples = [new(liveAt.AddSeconds(-40), 0, 1024) { HasUploadSample = false }, new(liveAt.AddSeconds(-30), 0, 1024) { HasUploadSample = false }];
                Pump(40);
                Check(!HasGreenAtRight(chart, 1024), "A sampling interruption is not replaced with a horizontal tail");
                chart.RangeMinutes = 1;
                chart.Samples = [new(liveAt.AddMinutes(-2), 0, 1024) { HasUploadSample = false }];
                Pump(300);
                Check(!HasGreenAtRight(chart, 1024), "Expired history does not produce a curve across an empty window");
                chart.Samples = [new(WholeSecond(DateTimeOffset.Now).AddSeconds(-4), 0, 1024) { HasUploadSample = false }];
                Pump(300);
                Check(!HasGreenAtRight(chart, 1024), "A first isolated sample is not extrapolated to the right edge");
                chart.Samples = []; Pump(80);
                var emptyStart = (DateTimeOffset)typeof(TrafficChart).GetField("_presentationTime", Private)!.GetValue(chart)!;
                Pump(150);
                var emptyEnd = (DateTimeOffset)typeof(TrafficChart).GetField("_presentationTime", Private)!.GetValue(chart)!;
                Check((emptyEnd - emptyStart).TotalSeconds > .08, "The visible time axis keeps moving even when no samples are available");
            }
            catch (Exception exception) { error = exception; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(40))) { Console.Error.WriteLine("Chart timeline regression timed out."); Environment.Exit(1); }
        if (error is not null) { Console.Error.WriteLine(error); return 1; }
        return Failures.Count == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string label)
    { Console.WriteLine($"{(condition ? "PASS" : "FAIL")}: {label}"); if (!condition) Failures.Add(label); }

    private static DateTimeOffset WholeSecond(DateTimeOffset time) => time.AddTicks(-(time.Ticks % TimeSpan.TicksPerSecond));

    private static bool HasGreenAtRight(TrafficChart chart, long value)
    {
        var transform = (MatrixTransform)typeof(TrafficChart).GetField("_plotTransform", Private)!.GetValue(chart)!;
        var y = transform.Transform(new System.Windows.Point(0, -value / 1024d)).Y;
        var bitmap = new RenderTargetBitmap((int)chart.ActualWidth, (int)chart.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(chart);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var x = (int)(chart.ActualWidth - 17);
        for (var row = Math.Max(0, (int)y - 2); row <= Math.Min(bitmap.PixelHeight - 1, (int)y + 2); row++)
        {
            var index = (row * bitmap.PixelWidth + x) * 4;
            if (pixels[index + 1] > 100 && pixels[index + 3] > 100) return true;
        }
        return false;
    }

    private static void Capture(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var folder = System.IO.Path.Combine(Environment.CurrentDirectory, "runtime-data", "chart-timeline-v0.1.10");
        System.IO.Directory.CreateDirectory(folder);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(System.IO.Path.Combine(folder, name + ".png")); encoder.Save(file);
    }

    private static DeviceViewData Device(string mac, string name, DateTimeOffset time) => new()
    {
        Profile = new DeviceProfile { MacAddress = mac, LastRouterName = name, HasObservedState = true, LastKnownOnline = true,
            LastIpAddress = "192.0.2.78", FirstSeenAt = time, LastSeenAt = time },
        Identity = new DeviceIdentity("笔记本电脑", "\uE770", name), Sessions = []
    };

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match; foreach (var item in Descendants<T>(child)) yield return item; }
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
}
