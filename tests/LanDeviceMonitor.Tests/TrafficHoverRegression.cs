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
        VerifyMotion();
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
                Require(typeof(TrafficChart).GetField("_hoverMotion", BindingFlags.Instance | BindingFlags.NonPublic) is not null,
                    "Hovered samples still switch instantly without a shared presentation motion.");
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
                    VerifyRenderedMotion(chart, now, range);
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
                Console.WriteLine("Traffic hover PASS: vertical hit columns, synchronized 160ms motion, intermediate/final frames, interruptions, scrolling, disabled motion, hidden peaks, missing directions and real zeros.");
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
    private static void VerifyMotion()
    {
        var motion = new ChartHoverMotion();
        var a = new ChartHoverFrame(100, 30, 60, 112, 31);
        var b = new ChartHoverFrame(300, 90, 20, 180, 31);
        Require(motion.Update(1, a, true, true, 1, .16) == a, "The first hover animated from an unrelated point.");
        Require(motion.Update(2, b, true, true, 1.01, .16) == a, "A hover switch jumped to its endpoint.");
        var middle = motion.Update(2, b, true, true, 1.09, .16);
        var p = (middle.X - a.X) / (b.X - a.X);
        Require(p is > 0 and < 1 && Math.Abs((middle.TooltipX-a.TooltipX)/(b.TooltipX-a.TooltipX)-p) < 1e-10 &&
            Math.Abs((middle.DownloadY-a.DownloadY)/(b.DownloadY-a.DownloadY)-p) < 1e-10,
            "The tooltip and selected markers do not share the same transition.");
        Require(motion.Update(3, a, true, true, 1.09, .16) == middle, "Interrupted motion did not continue at its visible position.");
        Require(motion.Update(3, a, true, true, 1.26, .16) == a, "Hover motion did not settle within 160ms.");
        motion.Reset(); motion.Update(4, a, false, true, 2, .16);
        Require(motion.Update(5, b, true, true, 2.01, .16).UploadY == b.UploadY, "A missing upload marker flew in from a false zero.");
        var scrolled = b with { X = 298, TooltipX = 178 };
        Require(motion.Update(5, scrolled, true, true, 2.18, .16) == scrolled, "Scrolling restarted the selection animation.");
        Require(motion.Update(6, a, true, true, 2.19, 0) == a, "Disabled motion did not snap immediately.");
    }

    private static void VerifyRenderedMotion(TrafficChart chart, DateTimeOffset now, int range)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var origin = (DateTimeOffset)typeof(TrafficChart).GetField("_sampleOrigin", flags)!.GetValue(chart)!;
        var motion = (ChartHoverMotion)typeof(TrafficChart).GetField("_hoverMotion", flags)!.GetValue(chart)!;
        double Tick() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
        double X(DateTimeOffset time) => ((MatrixTransform)typeof(TrafficChart).GetField("_plotTransform", flags)!.GetValue(chart)!).Transform(new Point((time-origin).TotalSeconds, 0)).X;
        void Render()
        {
            var visual = new DrawingVisual(); using var dc = visual.RenderOpen();
            typeof(TrafficChart).GetMethod("OnRender", flags)!.Invoke(chart, [dc]);
        }
        chart.UpdateHover(new Point(0, 0));
        chart.UpdateHover(new Point(X(now.AddSeconds(-45)), 73)); Render();
        var first = motion.Read(Tick(), .16);
        chart.UpdateHover(new Point(X(now.AddSeconds(-15)), 73)); Render();
        Require(Math.Abs(motion.Read(Tick(), .16).X-first.X) < 8, "Rendering a new hover skipped its starting frame.");
        Pump(45);
        var middle = motion.Read(Tick(), .16);
        if (SystemParameters.ClientAreaAnimation)
            Require(middle.X > first.X && middle.X < X(now.AddSeconds(-15)), "The rendered hover has no intermediate frame.");
        Capture(chart, $"range-{range}-middle");
        chart.UpdateHover(new Point(X(now.AddSeconds(-45)), 73)); Render();
        Pump(190); Render();
        Require(Math.Abs(motion.Read(Tick(), .16).X-X(now.AddSeconds(-45))) < .1, "The reversed hover did not settle on its actual sample.");
        Capture(chart, $"range-{range}-settled");
        chart.AnimationMilliseconds = 0;
        chart.UpdateHover(new Point(X(now.AddSeconds(-15)), 73)); Render();
        Require(Math.Abs(motion.Read(Tick(), 0).X-X(now.AddSeconds(-15))) < .1, "The renderer ignored disabled animation.");
        chart.AnimationMilliseconds = 220;
    }

    private static void Capture(FrameworkElement chart, string name = "vertical-hover")
    {
        var bitmap = new RenderTargetBitmap((int)chart.ActualWidth, (int)chart.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(chart);
        var folder = System.IO.Path.Combine(Environment.CurrentDirectory, "runtime-data", "traffic-hover-v0.1.12");
        System.IO.Directory.CreateDirectory(folder);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(System.IO.Path.Combine(folder, name + ".png")); encoder.Save(file);
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
}
