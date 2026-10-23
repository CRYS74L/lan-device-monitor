using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Controls;
using LanDeviceMonitor.Models;

internal static class TrafficHoverRegression
{
    internal static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null; Window? window = null;
            try
            {
                app = TestAppFactory.Create(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var now = DateTimeOffset.Now; now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
                var chart = new TrafficChart { SampleIntervalSeconds = 5,
                    Samples = Enumerable.Range(0, 125).Select(i => new TrafficSample(now.AddSeconds(i * 5 - 620), i * 17, i * 31)).ToArray() };
                window = new Window { Content = chart, Width = 800, Height = 320, Left = -10000, Top = -10000,
                    WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
                window.Show(); window.UpdateLayout(); Pump(100);
                foreach (var range in new[] { 1, 5, 10 })
                {
                    chart.RangeMinutes = range; Pump(280);
                    var matrix = (MatrixTransform)typeof(TrafficChart).GetField("_plotTransform", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chart)!;
                    var x = matrix.Transform(new Point(590, 0)).X;
                    foreach (var y in new[] { 31d, 73d, chart.ActualHeight - 41 })
                    {
                        Require(ReferenceEquals(chart.InputHitTest(new Point(x, y)), chart),
                            $"The {range}-minute plot does not receive the pointer at y={y:F1} away from its drawn curves.");
                        chart.UpdateHover(new Point(x, y));
                        var timestamp = (DateTimeOffset?)typeof(TrafficChart).GetField("_hoveredTimestamp", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chart);
                        Require(timestamp == now.AddSeconds(-30), "The vertical hover column selected a different measured sample.");
                    }
                    chart.UpdateHover(new Point(x + (chart.ActualWidth - 76) / (range * 60), 73));
                    Require((DateTimeOffset?)typeof(TrafficChart).GetField("_hoveredTimestamp", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chart) == now.AddSeconds(-30),
                        "Hovering one second beside a five-second sample did not select its actual value.");
                    if (range == 1) { Pump(30); Capture(chart); }
                    chart.UpdateHover(new Point(x, 12));
                    Require(typeof(TrafficChart).GetField("_hoveredTimestamp", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chart) is null,
                        "Leaving the plot did not clear the hovered sample.");
                }
                var origin = DateTimeOffset.UnixEpoch;
                var sparse = new[] { new TrafficSample(origin.AddSeconds(1), 0, 100) { HasUploadSample = false }, new TrafficSample(origin.AddSeconds(6), 0, 300), new TrafficSample(origin.AddSeconds(30), 0, 900) };
                Require(TrafficChart.FindHoverSample(sparse, origin.AddSeconds(2.1), origin, origin.AddSeconds(35), 5) == 0,
                    "Normal sampling columns did not select the nearest real sample.");
                Require(TrafficChart.FindHoverSample(sparse, origin.AddSeconds(5.4), origin, origin.AddSeconds(5.5), 5) is null,
                    "Hover exposed a peak that is still outside the buffered viewport.");
                Require(TrafficChart.FindHoverSample(sparse, origin.AddSeconds(18), origin, origin.AddSeconds(35), 5) is null,
                    "Hover borrowed an old value across a sampling interruption.");
                Require(sparse[0].HasUploadSample == false && sparse[1].UploadBytesPerSecond == 0,
                    "Hover changed missing directions or real zeros.");
                Console.WriteLine("Traffic hover PASS: whole plot hit testing, vertical columns, sparse samples, all three ranges, plot exit, hidden peaks, interruptions, missing directions and real zeros.");
            }
            catch (Exception error) { failure = error; }
            finally { window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(20))) { Console.Error.WriteLine("Traffic hover regression timed out."); Environment.Exit(1); }
        if (failure is not null) { Console.Error.WriteLine(failure); return 1; }
        return 0;
    }

    private static void Require(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); }
    private static void Capture(FrameworkElement chart)
    {
        var bitmap = new RenderTargetBitmap((int)chart.ActualWidth, (int)chart.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(chart);
        var folder = System.IO.Path.Combine(Environment.CurrentDirectory, "runtime-data", "traffic-hover-v0.1.11");
        System.IO.Directory.CreateDirectory(folder);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(System.IO.Path.Combine(folder, "vertical-hover.png")); encoder.Save(file);
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
}
