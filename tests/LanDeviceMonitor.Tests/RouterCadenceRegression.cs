using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class RouterCadenceRegression
{
    public static async Task<int> RunAsync()
    {
        var points = new[] { new System.Windows.Point(0,0), new System.Windows.Point(5,30), new System.Windows.Point(10,0) };
        var slopes = LanDeviceMonitor.Controls.SampleCurve.Slopes(points);
        double Value(int i, double t)
        {
            var a=points[i]; var b=points[i+1]; var h=(b.X-a.X)/3; var u=1-t;
            return u*u*u*a.Y+3*u*u*t*(a.Y+slopes[i]*h)+3*u*t*t*(b.Y-slopes[i+1]*h)+t*t*t*b.Y;
        }
        for(var i=0;i<=100;i++)
        {
            var t=i/100d;
            Require(Math.Abs(Value(0,t)-Value(1,1-t)) < 1e-9, "Equal-time 0,30,0 curve is asymmetric.");
            Require(Value(0,t)>=0 && Value(0,t)<=30, "Curve overshot measured values.");
        }
        Require(slopes[1]==0, "Peak does not have a continuous tangent.");
        var buffered = new LanDeviceMonitor.Controls.BufferedChartClock();
        buffered.Observe(10, 5, 100);
        Require(buffered.Read(100) == 5 && buffered.Read(102.5) == 7.5 && buffered.Read(106) == 10,
            "Buffered viewport should traverse only known samples.");
        buffered.Observe(15, 10, 105);
        Require(buffered.Read(105) == 10 && buffered.Read(107.5) == 12.5 && buffered.Read(200) == 15,
            "New frames jumped the viewport or advanced into unknown time.");
        var observed = new TrafficRateTracker();
        var values = new long[] { 0, 30, 0, 0 };
        for (var i = 0; i < values.Length; i++)
        {
            var sample = observed.Observe(DateTimeOffset.UnixEpoch.AddSeconds(5 * i), values[i], 100,
                0, i == 0 ? 0 : values[i - 1], TimeSpan.FromSeconds(5), newFrame: true);
            Require(sample.IsFresh && sample.GraphSpeed == values[i], "A confirmed router frame was skipped or its zero delayed.");
        }
        var origin = DateTimeOffset.UnixEpoch.AddDays(1);
        DeviceSnapshot Sample(long total, long online) => new("02:00:00:00:79:01", "测试设备", "test", true,
            "192.0.2.30", 0, online, 1000, 1000, total, total);
        foreach (var period in new[] { 1d, 2, 2.5, 3, 5, 10, 20 })
        {
            var detector = new RouterSamplingCadence();
            for (var second = 0; second <= period * 8; second++)
                detector.Observe([Sample((long)(second / period) * 10000, second)], origin.AddSeconds(second));
            Require(detector.IntervalSeconds == period, $"Expected {period}s, detected {detector.IntervalSeconds}s.");
            detector.Reset();
            Require(detector.IntervalSeconds is null, "Reconnection kept a previous router's cadence.");
        }
        var idle = new RouterSamplingCadence();
        for (var second = 0; second < 90; second++)
            idle.Observe([Sample(1000, second)], origin.AddSeconds(second));
        Require(idle.IntervalSeconds is null, "Online duration or idle cached traffic was mistaken for a source clock.");

        var staggered = new RouterSamplingCadence();
        for (var second = 0; second < 40; second++)
            staggered.Observe([Sample(second / 5 * 10000, second) with { DownloadSpeedBytes = second * 100 }], origin.AddSeconds(second));
        Require(staggered.IntervalSeconds == 5, "Asynchronous speed fields disguised a cached counter source as faster sampling.");

        var interval = TimeSpan.FromMilliseconds(30);
        var stamps = new List<long>();
        using var cancellation = new CancellationTokenSource();
        var loop = new MonitoringLoop(() => interval, token =>
        {
            stamps.Add(System.Diagnostics.Stopwatch.GetTimestamp());
            if (stamps.Count == 2) interval = TimeSpan.FromMilliseconds(150);
            if (stamps.Count == 5) cancellation.Cancel();
            return Task.CompletedTask;
        });
        try { await loop.RunAsync(cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        var gaps = stamps.Zip(stamps.Skip(1), (a, b) => System.Diagnostics.Stopwatch.GetElapsedTime(a, b).TotalMilliseconds).ToArray();
        Require(gaps.Length == 4 && gaps.Skip(1).All(g => g >= 130), "Detected cadence did not update the running loop.");
        Require(typeof(LanDeviceMonitor.MainWindow).GetField("RefreshIntervalCombo",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) is null,
            "Settings still expose a manual refresh control.");
        Console.WriteLine("Router cadence PASS: 1/2/2.5/3/5/10/20-second sources, duplicate idle frames, live online clock, reconnect reset, dynamic polling and no manual setting.");
        return 0;
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
