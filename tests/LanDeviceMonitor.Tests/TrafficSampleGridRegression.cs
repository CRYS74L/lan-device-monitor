using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LanDeviceMonitor;
using LanDeviceMonitor.Controls;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class TrafficSampleGridRegression
{
    internal static int Run()
    {
        var origin = new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.FromHours(8));
        foreach (var period in new[] { .25, 1, 2.5, 3.75, 5, 10, 20 })
        {
            var raw = Enumerable.Range(0, 32).Select(i => new TrafficSample(origin.AddSeconds(i * period + (i % 2 == 0 ? .05 : period * .18)),
                i == 20 ? 19 : 0, i == 20 ? 29 : 0) { IntervalSeconds = period }).ToArray();
            var history = new TrafficHistoryBuffer(); foreach (var point in raw) history.Append(point);
            var chart = history.ChartSnapshot(raw[^1].Timestamp);
            Require(chart.Count == raw.Length, "Grid alignment dropped or invented observations.");
            for (var i = 0; i < chart.Count; i++)
            {
                Require(chart[i].ReceivedAt == raw[i].Timestamp && chart[i].UploadBytesPerSecond == raw[i].UploadBytesPerSecond &&
                    chart[i].DownloadBytesPerSecond == raw[i].DownloadBytesPerSecond, "Alignment changed raw values or arrival timestamps.");
                if (i > 0) Require(Math.Abs((chart[i].Timestamp - chart[i - 1].Timestamp).TotalSeconds - period) < 1e-7,
                    $"Chart spacing is not {period}s at point {i}.");
            }
            var points = chart.Select(s => new Point((s.Timestamp - chart[0].Timestamp).TotalSeconds, s.DownloadBytesPerSecond)).ToArray();
            var segments = EChartsCurve.Build(points, 12 / period, 4.5);
            var center = points[20].X;
            for (var i = 0; i < 100; i++)
            {
                var delta = period * i / 100;
                Require(Math.Abs(SampleCurve.ValueAt(points, segments, center - delta) -
                    SampleCurve.ValueAt(points, segments, center + delta)) < 1e-7, "A symmetric spike became asymmetric after alignment.");
            }
        }
        var precision = new TrafficHistoryBuffer();
        precision.Append(new(origin.AddMilliseconds(125), 1, 2));
        precision.Append(new(origin.AddMilliseconds(375), 3, 4));
        Require(precision.Snapshot(origin.AddSeconds(1)).Count == 2, "Subsecond samples were merged into a whole-second slot.");
        var missing = TrafficSampleGrid.Project(new[] { new TrafficSample(origin, 10, 10) { IntervalSeconds = 5 },
            new(origin.AddSeconds(5.2), 20, 20) { IntervalSeconds = 5 }, new(origin.AddSeconds(25.1), 30, 30) { IntervalSeconds = 5 } });
        Require(missing.Count == 3 && missing[^1].StartsNewSegment && (missing[^1].Timestamp - missing[^2].Timestamp).TotalSeconds == 20,
            "A missing batch was filled or the time gap was compressed.");
        var changed = TrafficSampleGrid.Project(Enumerable.Range(0, 12).Select(i => new TrafficSample(
            origin.AddSeconds(i < 6 ? i * 5 : 30 + (i - 6) * 2.5), i, i) { IntervalSeconds = i < 6 ? 5 : 2.5 }).ToArray());
        Require(changed[6].StartsNewSegment && (changed[^1].Timestamp - changed[^2].Timestamp).TotalSeconds == 2.5 &&
            (changed[4].Timestamp - changed[3].Timestamp).TotalSeconds == 5, "A period change rewrote the old sampling period.");

        var estimator = new RouterSamplingCadence();
        DeviceSnapshot Frame(long bytes) => new("02:00:00:00:73:01", "周期测试", "", true, "192.0.2.73", 0, 0, 1, 1, bytes, bytes);
        for (var i = 0; i < 15; i++) estimator.Observe([Frame(i * 1000)], origin.AddSeconds(i * 5));
        Require(estimator.IntervalSeconds == 5, "Initial cadence was not detected.");
        for (var i = 1; i < 16; i++) estimator.Observe([Frame((15 + i) * 1000)], origin.AddSeconds(70 + i * 2.5));
        Require(estimator.IntervalSeconds == 2.5, $"Cadence was frozen after the first detection: {estimator.IntervalSeconds}.");
        var jittered = new RouterSamplingCadence();
        for (var i = 0; i < 20; i++) jittered.Observe([Frame(i * 1000)], origin.AddSeconds(i * 5 + (i % 2)));
        Require(jittered.IntervalSeconds == 5, "Alternating 4/6-second discoveries disguised a five-second source.");
        Console.WriteLine("Uniform sampling PASS: .25/1/2.5/3.75/5/10/20s, jitter, symmetric spikes, original values and arrival precision, gaps and changing periods.");
        Capture();
        return 0;
    }

    private static void Capture()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            App? app = null;
            try
            {
                app = TestAppFactory.Create(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeService.Apply(ThemeService.LightMode);
                var now = DateTimeOffset.Now;
                var history = new TrafficHistoryBuffer();
                for (var i = 0; i < 14; i++) history.Append(new(now.AddSeconds(-70 + i * 5 + (i % 2 == 0 ? .1 : .9)),
                    i == 7 ? 19 : 0, i == 7 ? 29 : 0) { IntervalSeconds = 5 });
                var samples = history.ChartSnapshot(now);
                var chart = new TrafficChart { Width = 860, Height = 220, Samples = samples };
                var border = new Border { Width = 860, Height = 220, Background = Brushes.White, Child = chart };
                border.Measure(new Size(860, 220)); border.Arrange(new Rect(0, 0, 860, 220)); border.UpdateLayout();
                var output = Path.Combine(Environment.CurrentDirectory, ".tools", "uniform-sampling"); Directory.CreateDirectory(output);
                var bitmap = new RenderTargetBitmap(860, 220, 96, 96, PixelFormats.Pbgra32); bitmap.Render(border);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(output, "native-uniform-spike.png"))) encoder.Save(file);
                File.WriteAllText(Path.Combine(output, "projected-samples.json"), JsonSerializer.Serialize(samples.Select(p => new {
                    at = p.Timestamp, receivedAt = p.ReceivedAt, intervalSeconds = p.IntervalSeconds,
                    startsNewSegment = p.StartsNewSegment, upload = p.UploadBytesPerSecond, download = p.DownloadBytesPerSecond })));
                var watch = Stopwatch.StartNew();
                for (var i = 0; i < 1000; i++) history.ChartSnapshot(now);
                Console.WriteLine($"Uniform sampling projection: {watch.Elapsed.TotalMilliseconds / 1000:F4}ms per history; WPF symmetric spike captured.");
            }
            catch (Exception e) { error = e; }
            finally { app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error is not null) throw error;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
