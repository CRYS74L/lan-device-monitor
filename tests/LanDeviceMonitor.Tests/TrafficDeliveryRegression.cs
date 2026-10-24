using System.Reflection;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Controls;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class TrafficDeliveryRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static int Run(bool soak = false)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            App? app = null; MainWindow? window = null;
            try
            {
                app = TestAppFactory.Create(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                window = new MainWindow(new NotificationService()) { WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000, Top = -10000, Width = 1180, Height = 850, ShowActivated = false, ShowInTaskbar = false };
                typeof(MainWindow).GetField("_isPopulatingAppSettings", Private)!.SetValue(window, true);
                var queue = typeof(MainWindow).GetMethod("QueueMonitoringUpdate", Private)!;
                var history = typeof(MainWindow).GetMethod("GetTrafficHistory", Private)!;
                var origin = DateTimeOffset.Now.AddMinutes(-3);
                origin = origin.AddTicks(-origin.Ticks % TimeSpan.TicksPerSecond);
                const string active = "02:00:00:00:77:01", idle = "02:00:00:00:77:02";
                DeviceViewData Device(string id, long rate) => new()
                {
                    Profile = new DeviceProfile { MacAddress = id, LastRouterName = "采样交付测试", HasObservedState = true,
                        LastKnownOnline = true, HasFreshUploadGraphSample = true, HasFreshDownloadGraphSample = true,
                        LastUploadSpeedBytes = rate, LastDownloadSpeedBytes = rate, LastUploadGraphSpeedBytes = rate,
                        LastDownloadGraphSpeedBytes = rate, ObservedTrafficIntervalSeconds = 5, FirstSeenAt = origin, LastSeenAt = origin },
                    Identity = new DeviceIdentity("笔记本电脑", "\uE770", "采样交付测试"), Sessions = []
                };
                // The UI cannot drain while this batch arrives: metadata may be
                // coalesced, but every actual traffic measurement must survive.
                for (var i = 0; i < 18; i++)
                    queue.Invoke(window, [new MonitorUpdate([Device(active, i * 1024), Device(idle, 0)], [], origin.AddSeconds(i * 5))]);
                Pump(120);
                var activePoints = ((TrafficHistoryBuffer)history.Invoke(window, [active])!).Snapshot(DateTimeOffset.Now);
                var idlePoints = ((TrafficHistoryBuffer)history.Invoke(window, [idle])!).Snapshot(DateTimeOffset.Now);
                Require(activePoints.Count == 18 && idlePoints.Count == 18,
                    $"Coalescing UI snapshots lost traffic: active={activePoints.Count}/18, idle={idlePoints.Count}/18.");
                Require(activePoints.Select(p => p.Timestamp).SequenceEqual(Enumerable.Range(0, 18).Select(i => origin.AddSeconds(i * 5))),
                    "A queued sample timestamp was changed or reordered.");
                Require(activePoints.Select(p => p.DownloadBytesPerSecond).SequenceEqual(Enumerable.Range(0, 18).Select(i => i * 1024L)) &&
                    idlePoints.All(p => p.DownloadBytesPerSecond == 0 && p.HasDownloadSample), "Sample values or confirmed idle observations changed.");
                Console.WriteLine("Traffic delivery PASS: 18 queued frames retain every measured value and timestamp for active and idle devices.");

                var next = origin.AddSeconds(90);
                queue.Invoke(window, [new MonitorUpdate([Device(active, 18432), Device(idle, 0)], [], next)]);
                var load = true;
                void InputWork() { if (load) window.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(InputWork)); }
                window.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(InputWork));
                Pump(120); load = false;
                var delivered = ((TrafficHistoryBuffer)history.Invoke(window, [active])!).Snapshot(DateTimeOffset.Now);
                Require(delivered[^1].Timestamp == next,
                    "Continuous input/render work starved monitoring delivery while the chart clock kept moving.");
                Console.WriteLine("Traffic delivery PASS: monitoring reaches the chart while the input queue remains busy.");

                if (!soak) return;
                window.Show(); window.UpdateLayout(); Pump(650);
                var liveOrigin = DateTimeOffset.Now;
                for (var i = 18; i > 0; i--)
                    queue.Invoke(window, [new MonitorUpdate([Device(active, 1024), Device(idle, 0)], [], liveOrigin.AddSeconds(-i * 5))]);
                queue.Invoke(window, [new MonitorUpdate([Device(active, 1024), Device(idle, 0)], [], liveOrigin)]);
                Pump(350);
                var chart = Descendants<TrafficChart>(window).Single();
                using var cancellation = new CancellationTokenSource();
                var producer = Task.Run(async () =>
                {
                    var batch = 0;
                    try
                    {
                        while (true)
                        {
                            await Task.Delay(5000, cancellation.Token);
                            var rate = ++batch % 3 == 1 ? 0 : batch % 3 == 2 ? 40000 : 1024;
                            queue.Invoke(window, [new MonitorUpdate([Device(active, rate), Device(idle, 0)], [], DateTimeOffset.Now)]);
                        }
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                });
                load = true; window.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(InputWork));
                var watch = Stopwatch.StartNew();
                var firstTime = (DateTimeOffset)typeof(TrafficChart).GetField("_presentationTime", Private)!.GetValue(chart)!;
                var observations = 0; var minimumLead = double.PositiveInfinity; var interaction = 0;
                try
                {
                    while (watch.Elapsed.TotalSeconds < 45)
                    {
                        Pump(100);
                        if (watch.Elapsed.TotalSeconds >= (interaction + 1) * 8)
                        {
                            interaction++;
                            window.DeviceList.SelectedIndex = interaction % 2;
                            window.TrafficRangeSegments.SelectedIndex = interaction % 3;
                        }
                        var series = typeof(TrafficChart).GetField("_downloadSeries", Private)!.GetValue(chart)!;
                        var segments = (EChartsCurve.Segment[])series.GetType().GetField("_currentSegments", Private)!.GetValue(series)!;
                        Require(segments.Length > 0, "The live fixture lost its rendered curve.");
                        var transform = (MatrixTransform)typeof(TrafficChart).GetField("_plotTransform", Private)!.GetValue(chart)!;
                        var lead = transform.Transform(segments[^1].End).X - (chart.ActualWidth - 16);
                        minimumLead = Math.Min(minimumLead, lead);
                        Require(lead >= -1.75, $"The real-time curve fell short of the right edge by {-lead:F2}px at {watch.Elapsed.TotalSeconds:F2}s.");
                        var rendered = (DateTimeOffset)typeof(TrafficChart).GetField("_presentationTime", Private)!.GetValue(chart)!;
                        Require(Math.Abs((rendered - firstTime).TotalSeconds - watch.Elapsed.TotalSeconds) < .75,
                            "The graph stopped or replayed old time during live delivery.");
                        observations++;
                    }
                    var output = Path.Combine(Environment.CurrentDirectory, ".tools", "traffic-gap-20261025");
                    Directory.CreateDirectory(output);
                    var bitmap = new RenderTargetBitmap((int)chart.ActualWidth, (int)chart.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(chart); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(output, "native-delivery-soak.png")); encoder.Save(file);
                    Console.WriteLine($"Traffic delivery soak PASS: {watch.Elapsed.TotalSeconds:F1}s, {observations} rendered observations, 5-second real arrivals, busy input, {interaction} device/range switches, minimum right-edge coverage {minimumLead:F2}px.");
                }
                finally { load = false; cancellation.Cancel(); producer.GetAwaiter().GetResult(); }
            }
            catch (Exception e) { error = e; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(soak ? 70 : 20))) { Console.Error.WriteLine("Traffic delivery timed out."); Environment.Exit(1); }
        if (error is null) return 0;
        Console.Error.WriteLine(error); return 1;
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match; foreach (var item in Descendants<T>(child)) yield return item; }
    }
}
