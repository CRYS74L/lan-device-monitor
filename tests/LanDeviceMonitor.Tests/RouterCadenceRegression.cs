using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class RouterCadenceRegression
{
    public static async Task<int> RunAsync()
    {
        foreach (var pair in new[] {
            (new System.Windows.Point(0, 0), new System.Windows.Point(30, 100)),
            (new System.Windows.Point(30, 100), new System.Windows.Point(35, 0)),
            (new System.Windows.Point(0, 20), new System.Windows.Point(10, 20)) })
        {
            var controls = LanDeviceMonitor.Controls.TrafficChart.GetSampleCurveControls(pair.Item1, pair.Item2);
            for (var step = 0; step <= 100; step++)
            {
                var t = step / 100d;
                var u = 1 - t;
                var y = u*u*u*pair.Item1.Y + 3*u*u*t*controls.First.Y + 3*u*t*t*controls.Second.Y + t*t*t*pair.Item2.Y;
                Require(y >= Math.Min(pair.Item1.Y, pair.Item2.Y) - 1e-9 && y <= Math.Max(pair.Item1.Y, pair.Item2.Y) + 1e-9,
                    "Curve invented a peak or dip between actual samples.");
                if (step == 100) Require(Math.Abs(y - pair.Item2.Y) < 1e-9, "Curve missed the actual sample.");
            }
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
