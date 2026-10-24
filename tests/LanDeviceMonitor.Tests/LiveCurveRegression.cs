using System.Reflection;
using System.Windows;
using System.Windows.Media;
using LanDeviceMonitor.Controls;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class LiveCurveRegression
{
    internal static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Verify(); } catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw failure;
        return 0;
    }

    private static void Verify()
    {
        var origin = DateTimeOffset.UnixEpoch;
        var history = new TrafficHistoryBuffer();
        var remembered = new Dictionary<DateTimeOffset, DateTimeOffset>();
        var series = new TrafficSeriesDrawing(false, new MatrixTransform());
        var clock = new ChartTimeline();
        EChartsCurve.Segment[] previous = [];
        Point[] oldPoints = [];
        var oldOrigin = origin;
        var oldEnd = double.NegativeInfinity;
        var checkedValues = 0;
        for (var i = 0; i < 160; i++)
        {
            var now = origin.AddSeconds(i + 1);
            if (i % 5 == 0)
                history.Append(new(origin.AddSeconds(i + (i % 10 == 0 ? .9 : .1)), 10, 100 + i * 193 % 997) { IntervalSeconds = 5 });
            var projected = history.ChartSnapshot(now);
            foreach (var sample in projected)
            {
                var raw = sample.ReceivedAt!.Value;
                if (remembered.TryGetValue(raw, out var oldTime)) Require(oldTime == sample.Timestamp, "Arrival jitter moved a historical sampling slot.");
                remembered[raw] = sample.Timestamp;
            }
            var samples = projected.TakeLast(15).ToArray();
            var chartOrigin = samples[0].Timestamp;
            var points = samples.Select(s => new Point((s.Timestamp - chartOrigin).TotalSeconds, -s.DownloadBytesPerSecond / 1024d)).ToArray();
            clock.Observe(i + 1, 5, i);
            Require(clock.Read(i) == i - 6, "Playback delay changed from its original seven seconds.");
            var end = (origin.AddSeconds(clock.Read(i)) - chartOrigin).TotalSeconds;
            series.SetSamples(samples, chartOrigin, i, .22);
            series.SetProjection(7.483333333333333, 184.0522875816993 + i % 7 * .1234567);
            series.VisibleBounds(end - 60, end, i, .22);
            var actual = (EChartsCurve.Segment[])typeof(TrafficSeriesDrawing).GetField("_currentSegments", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(series)!;
            if (previous.Length > 0)
            {
                var shift = (oldOrigin - chartOrigin).TotalSeconds;
                var left = Math.Max(oldPoints[0].X, points[0].X - shift);
                var right = Math.Min(oldEnd, oldPoints[^1].X);
                for (var x = left; x <= right; x += .071)
                {
                    Require(Math.Abs(SampleCurve.ValueAt(oldPoints, previous, x) - SampleCurve.ValueAt(points, actual, x + shift)) < 1e-8,
                        $"Already visible curve changed at wall={i}s, point={x}s.");
                    checkedValues++;
                }
            }
            if (actual.Length > 0)
            {
                foreach (var point in points)
                    Require(Math.Abs(SampleCurve.ValueAt(points, actual, point.X) - point.Y) < 1e-8, "Retaining history missed a real sample.");
                foreach (var segment in actual)
                foreach (var p in new[] { segment.First, segment.Second })
                    Require(p.Y >= Math.Min(segment.Start.Y, segment.End.Y) - 1e-8 && p.Y <= Math.Max(segment.Start.Y, segment.End.Y) + 1e-8,
                        "The unseen suffix overshot its measured endpoints.");
                for (var j = 1; j < actual.Length; j++)
                {
                    var a = actual[j - 1]; var b = actual[j];
                    if (a.End != b.Start) continue;
                    var u = a.End - a.Second; var v = b.First - b.Start;
                    if (u.Length < 1e-12) u = a.End - a.First;
                    if (v.Length < 1e-12) v = b.Second - b.Start;
                    u = new Vector(u.X * 12, u.Y * 200); v = new Vector(v.X * 12, v.Y * 200);
                    Require(Math.Abs(Vector.CrossProduct(u, v)) / Math.Max(1e-20, u.Length * v.Length) < 1e-8,
                        "A subdivision or real sampling knot introduced a visible corner.");
                }
            }
            previous = actual; oldPoints = points; oldOrigin = chartOrigin; oldEnd = end;
        }
        Require(checkedValues > 50000, "The stream did not exercise enough historical coordinates.");
        Console.WriteLine($"Live curve stability PASS: {checkedValues} visible coordinates unchanged over 160s; 4.2/5.8s arrivals, fixed 5s grid, unchanged 7s buffer, varying Y scale, pruning, true knots, bounded smooth joins.");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
