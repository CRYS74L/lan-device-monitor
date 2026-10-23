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
        clock.Observe(1000, 995, 50);
        Check(clock.Read(50) >= 1000, "First display includes the newest sample without replaying history");
        clock.Observe(1300, 1300, 350);
        Check(clock.Read(350) >= 1300, "A five-minute rendering pause resumes at current time");
        var resumed = clock.Read(350);
        Check(Math.Abs(clock.Read(351) - resumed - 1) < .001, "Resumed time advances at one second per second");
        var currentTime = WholeSecond(DateTimeOffset.Now);
        var oldSamples = new[] { new TrafficSample(currentTime.AddHours(-8), 10, 100), new TrafficSample(currentTime.AddHours(-8).AddSeconds(5), 10, 100) };
        var visible = (IReadOnlyList<TrafficSample>)typeof(TrafficChart).GetMethod("GetVisibleSamples", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [oldSamples, 1, currentTime])!;
        Check(visible.Count == 0, "Expired history does not create a long sequence of artificial time slots");

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
                    secondHistory.Append(new(now.AddSeconds((i - 24) * 5), 20, i == 24 ? 200000 : 100));
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
                Check(top?.From >= 200000 && top?.To >= 200000, "The new device's Y axis is ready during its entrance animation");
                Capture(window, "device-entry");
                Pump(650);
                var presentation = (DateTimeOffset)typeof(TrafficChart).GetField("_presentationTime", Private)!.GetValue(chart)!;
                Check(presentation >= now && (DateTimeOffset.Now - presentation).TotalSeconds < 1,
                    "Switching devices does not fast-forward through the old cached window");

                chart.DeviceKey = "peak-fixture";
                chart.Samples = [new(now.AddSeconds(-30), 20, 100), new(now, 20, 100)];
                Pump(350);
                var peakAt = WholeSecond(DateTimeOffset.Now);
                chart.Samples = [..chart.Samples, new(peakAt, 0, 900000) { HasUploadSample = false }];
                Pump(35);
                var transform = (MatrixTransform)typeof(TrafficChart).GetField("_plotTransform", Private)!.GetValue(chart)!;
                var origin = (DateTimeOffset)typeof(TrafficChart).GetField("_sampleOrigin", Private)!.GetValue(chart)!;
                var peak = transform.Transform(new System.Windows.Point((peakAt - origin).TotalSeconds, -900000 / 1024d));
                Check(peak.X >= 60 && peak.X <= chart.ActualWidth - 16,
                    "A newly arrived peak is immediately inside the time viewport");
                Check(peak.Y >= 23 && peak.Y <= chart.ActualHeight - 32,
                    "The Y axis and the new peak are visible in the same frame");
                Capture(chart, "peak-first-frame");
                window.Hide(); Pump(120); window.Show(); Pump(45);
                presentation = (DateTimeOffset)typeof(TrafficChart).GetField("_presentationTime", Private)!.GetValue(chart)!;
                Check((DateTimeOffset.Now - presentation).TotalSeconds < 1,
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
            }
            catch (Exception exception) { error = exception; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(25))) { Console.Error.WriteLine("Chart timeline regression timed out."); Environment.Exit(1); }
        if (error is not null) { Console.Error.WriteLine(error); return 1; }
        return Failures.Count == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string label)
    { Console.WriteLine($"{(condition ? "PASS" : "FAIL")}: {label}"); if (!condition) Failures.Add(label); }

    private static DateTimeOffset WholeSecond(DateTimeOffset time) => time.AddTicks(-(time.Ticks % TimeSpan.TicksPerSecond));

    private static void Capture(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var folder = System.IO.Path.Combine(Environment.CurrentDirectory, "runtime-data", "chart-timeline-v0.1.8");
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
