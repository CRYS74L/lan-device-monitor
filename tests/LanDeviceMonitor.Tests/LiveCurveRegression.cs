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
        VerifySamplingClock();
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
            var samples = projected.TakeLast(20).ToArray();
            var chartOrigin = samples[0].Timestamp;
            var points = samples.Select(s => new Point((s.Timestamp - chartOrigin).TotalSeconds, -s.DownloadBytesPerSecond / 1024d)).ToArray();
            clock.Observe(i + 1, 5, i);
            Require(clock.Read(i) == i - 6, "Playback delay changed from its original seven seconds.");
            var end = (origin.AddSeconds(clock.Read(i)) - chartOrigin).TotalSeconds;
            series.SetSamples(samples, chartOrigin, i, .22);
            series.SetProjection(7.483333333333333, 184.0522875816993 + i % 7 * .1234567);
            series.VisibleBounds(end - 60, end, i, .22);
            series.VisibleBounds(end - 60, end, i + .22, .22);
            var actual = (EChartsCurve.Segment[])typeof(TrafficSeriesDrawing).GetField("_currentSegments", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(series)!;
            if (previous.Length > 0)
            {
                var shift = (oldOrigin - chartOrigin).TotalSeconds;
                var left = Math.Max(oldEnd - 60, Math.Max(oldPoints[0].X, points[0].X - shift));
                var right = oldPoints[^2].X;
                for (var x = left; x <= right; x += .071)
                {
                    Require(Math.Abs(SampleCurve.ValueAt(oldPoints, previous, x) - SampleCurve.ValueAt(points, actual, x + shift)) < 1e-8,
                        $"Settled history changed at wall={i}s, point={x}s.");
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
                Require(actual.Length == points.Length - 1, "A sampling interval was split into a seam.");
                var basis = (double)typeof(TrafficSeriesDrawing).GetField("_curveScaleY", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(series)!;
                var expected = EChartsCurve.Build(points, 7.483333333333333, basis);
                for (var j = 0; j < actual.Length; j++)
                foreach (var pair in new[] { (actual[j].First, expected[j].First), (actual[j].Second, expected[j].Second) })
                    Require((pair.Item1 - pair.Item2).Length < 1e-8, "Settled path differs from the original ECharts controls.");
            }
            previous = actual; oldPoints = points; oldOrigin = chartOrigin; oldEnd = end;
        }
        Require(checkedValues > 50000, "The stream did not exercise enough historical coordinates.");
        Console.WriteLine($"Live curve stability PASS: {checkedValues} settled coordinates stable over 160s; complete ECharts cubics, no subdivided seams, 4.2/5.8s arrivals, fixed grid, unchanged buffer, varying Y scale, pruning and true knots.");
    }

    private static void VerifySamplingClock()
    {
        foreach (var period in new[] { 2.5, 5, 10 })
        {
            var timeline = new ChartTimeline(); var sampling = new ChartSamplingClock();
            var samples = new List<TrafficSample>(); var next = -20;
            double? previous = null; var minimumCoverage = double.PositiveInfinity;
            for (var i = 0; i < 36000; i++)
            {
                var wall = i / 10d;
                double Receive(int index) => index * (period + .02) + (index % 2 == 0 ? .25 : -.25);
                while (Receive(next) <= wall)
                {
                    samples.Add(new(DateTimeOffset.UnixEpoch.AddSeconds(next * period - 4), 123, 456)
                        { ReceivedAt = DateTimeOffset.UnixEpoch.AddSeconds(Receive(next)), IntervalSeconds = period });
                    next++; sampling.Observe(samples);
                }
                timeline.Observe(wall, period, wall);
                var end = timeline.Read(wall) + sampling.Read(wall);
                var latest = samples[^1]; var at = (latest.Timestamp - DateTimeOffset.UnixEpoch).TotalSeconds;
                minimumCoverage = Math.Min(minimumCoverage, at - end);
                if (previous is { } old) Require(end - old is >= .089999 and <= .110001, "Phase correction paused or jumped playback.");
                var phase = (latest.Timestamp - latest.ReceivedAt!.Value).TotalSeconds;
                Require(Math.Abs(wall - (end - phase) - (period + 2)) < 1.1, "Phase correction extended latency beyond sampling jitter.");
                previous = end;
            }
            Require(minimumCoverage > 0, $"Right edge became empty after cadence drift at {period}s: {minimumCoverage}.");
        }
        Console.WriteLine("Sampling clock PASS: 3 simulated hours at 2.5/5/10s, receive jitter and slow drift; continuously covered right edge, fixed buffer, monotonic motion.");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
