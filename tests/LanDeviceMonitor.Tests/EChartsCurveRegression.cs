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

internal static class EChartsCurveRegression
{
    internal static int Run()
    {
        using var reference = Assembly.GetExecutingAssembly().GetManifestResourceStream("LanDeviceMonitor.Tests.EChartsCurveReference")!;
        using var fixture = JsonDocument.Parse(reference);
        var count = 0;
        foreach (var example in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var points = example.GetProperty("points").EnumerateArray().Select(p => new Point(p[0].GetDouble(), p[1].GetDouble())).ToArray();
            var actual = EChartsCurve.Build(points, example.GetProperty("scaleX").GetDouble(), example.GetProperty("scaleY").GetDouble());
            var expected = example.GetProperty("segments");
            Require(actual.Length == expected.GetArrayLength(), example.GetProperty("name").GetString() + ": segment count");
            for (var i = 0; i < actual.Length; i++)
            {
                var values = new[] { actual[i].Start, actual[i].First, actual[i].Second, actual[i].End };
                for (var j = 0; j < 4; j++)
                for (var k = 0; k < 2; k++)
                {
                    var value = k == 0 ? values[j].X : values[j].Y;
                    var target = expected[i][j][k].GetDouble(); count++;
                    Require(Math.Abs(value - target) <= 1e-10 * Math.Max(1, Math.Abs(target)),
                        $"{example.GetProperty("name").GetString()}: segment {i}, control {j}, coordinate {k}: {value:R}/{target:R}");
                }
            }
        }
        Require(count > 15000, "The upstream fixture did not cover its expected control coordinates.");
        Console.WriteLine($"ECharts upstream parity PASS: 104 cases, {count} control coordinates, including valleys, spikes, irregular spacing, projections and subpixel segments.");

        Exception? error = null;
        var thread = new Thread(() =>
        {
            App? app = null;
            try
            {
                var transform = new MatrixTransform();
                var series = new TrafficSeriesDrawing(false, transform);
                var origin = DateTimeOffset.UnixEpoch;
                var samples = new[] { new TrafficSample(origin, 0, 0), new TrafficSample(origin.AddSeconds(5), 0, 30000), new TrafficSample(origin.AddSeconds(10), 0, 5000) };
                series.SetSamples(samples, origin, 0, .22); series.SetProjection(12, 2);
                series.VisibleBounds(0, 10, 0, .22);
                var line = (GeometryGroup)typeof(TrafficSeriesDrawing).GetField("_line", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(series)!;
                var retained = line.Children.Single();
                transform.Matrix = new Matrix(12, 0, 0, 2, -1, 100);
                series.VisibleBounds(0, 10, .016, .22);
                Require(ReferenceEquals(retained, line.Children.Single()), "Pure time scrolling rebuilt an unchanged path.");
                series.SetProjection(2, 2); series.VisibleBounds(0, 10, .032, .22);
                Require(!ReferenceEquals(retained, line.Children.Single()) && line.Children.Single().IsFrozen,
                    "Range/aspect changes did not reproject and freeze the ECharts controls.");
                var appended = samples.Append(new TrafficSample(origin.AddSeconds(15), 0, 60000)).ToArray();
                series.SetSamples(appended, origin, .05, .22);
                series.VisibleBounds(0, 15, .05, .22);
                var first = line.Children.Single().ToString();
                series.VisibleBounds(0, 15, .14, .22);
                var middle = line.Children.Single().ToString();
                series.VisibleBounds(0, 15, .28, .22);
                Require(first == middle && middle == line.Children.Single().ToString(), "A new sample reshaped already displayed history.");
                app = TestAppFactory.Create(); app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var output = Path.Combine(Environment.CurrentDirectory, ".tools", "echarts-integration", "evidence");
                Directory.CreateDirectory(output);
                foreach (var theme in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                {
                    ThemeService.Apply(theme);
                    var now = DateTimeOffset.Now;
                    var chart = new TrafficChart { Width = 860, Height = 220,
                        Samples = new[] { new TrafficSample(now.AddSeconds(-67), 30000, 8000), new TrafficSample(now.AddSeconds(-37), 0, 25000), new TrafficSample(now.AddSeconds(-7), 30000, 12000) } };
                    var border = new Border { Width = 860, Height = 220, Background = theme == ThemeService.DarkMode ? Brushes.Black : Brushes.White, Child = chart };
                    border.Measure(new Size(860, 220)); border.Arrange(new Rect(0, 0, 860, 220)); border.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(860, 220, 96, 96, PixelFormats.Pbgra32); bitmap.Render(border);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(output, $"native-valley-{theme}.png")); encoder.Save(file);
                }
                Console.WriteLine("ECharts rendering PASS: continuous scrolling retains paths, range changes reproject, new samples preserve visible history; native light/dark valley captures saved.");
            }
            catch (Exception e) { error = e; }
            finally { app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error is null) return 0;
        Console.Error.WriteLine(error); return 1;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
