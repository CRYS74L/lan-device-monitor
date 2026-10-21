using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class TrafficSnapshotRegression
{
    public static async Task<int> RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LanDeviceMonitor.Tests", $"traffic-snapshot-{Guid.NewGuid():N}");
        try
        {
            var repository = new DeviceRepository(Path.Combine(directory, "state.json"));
            var origin = new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);
            const long rate = 2 * 1024 * 1024;
            const long baseline = 100_000_000;
            var freshCount = 0;
            for (var second = 0; second < 65; second++)
            {
                var batch = second / 5;
                // Alternate real five-second snapshots with a zero speed field
                // whose advancing byte counter still measures continuous traffic.
                var snapshot = Snapshot(batch % 2 == 0 ? rate : 0, baseline + batch * 5 * rate, 100 + second);
                var update = await repository.ApplyPollAsync([snapshot], origin.AddSeconds(second));
                var p = update.Devices.Single().Profile;
                Require(p.LastDownloadSpeedBytes == rate, $"Continuous load was cleared at second {second}.");
                Require(p.HasFreshDownloadGraphSample == (second % 5 == 0), $"Cached response became a fresh sample at second {second}.");
                if (p.HasFreshDownloadGraphSample)
                {
                    Require(p.LastDownloadGraphSpeedBytes == rate, "Advancing counters produced a zero graph point.");
                    freshCount++;
                }
            }
            Require(freshCount == 13, "A minute of five-second source snapshots must have thirteen actual observations.");
            var lastTotal = baseline + 12 * 5 * rate;
            var partialZero = await repository.ApplyPollAsync([Snapshot(0, lastTotal, 160)], origin.AddSeconds(65));
            Require(partialZero.Devices.Single().Profile is { LastDownloadSpeedBytes: rate, HasFreshDownloadGraphSample: false },
                "The first uncorroborated zero cleared the measured rate.");
            var missing = await repository.ApplyPollAsync([Snapshot(0, 0, 160) with
            {
                HasValidDownloadSpeedData = false, HasValidTotalDownloadData = false
            }], origin.AddSeconds(66));
            Require(missing.Devices.Single().Profile is { LastDownloadSpeedBytes: rate, HasFreshDownloadGraphSample: false },
                "Missing fields became a zero rate.");
            var idle = await repository.ApplyPollAsync([Snapshot(0, lastTotal, 165)], origin.AddSeconds(70));
            Require(idle.Devices.Single().Profile is { LastDownloadSpeedBytes: 0, LastDownloadGraphSpeedBytes: 0, HasFreshDownloadGraphSample: true },
                "A full source interval of idle observations must reach zero.");
            Require(idle.Devices.Single().Profile.ObservedTrafficIntervalSeconds == 5, "The source interval was lost.");
            Require(idle.Devices.Single().Profile.LastUploadSpeedBytes == 0, "Download data leaked into idle upload.");

            var delayedRepository = new DeviceRepository(Path.Combine(directory, "delayed-state.json"));
            await delayedRepository.ApplyPollAsync([Snapshot(rate, baseline, 100)], origin);
            DeviceSnapshot WithoutCounter(long revision) => Snapshot(0, 0, revision) with { HasValidTotalDownloadData = false };
            for (var second = 5; second <= 6; second++)
            {
                var uncertain = await delayedRepository.ApplyPollAsync([WithoutCounter(105)], origin.AddSeconds(second));
                Require(uncertain.Devices.Single().Profile is { LastDownloadSpeedBytes: rate, HasFreshDownloadGraphSample: false },
                    "A zero without its companion counter was accepted before the next source snapshot.");
            }
            var delayedCounter = await delayedRepository.ApplyPollAsync([Snapshot(0, baseline + 5 * rate, 105)], origin.AddSeconds(7));
            Require(delayedCounter.Devices.Single().Profile is { LastDownloadSpeedBytes: > 0, LastDownloadGraphSpeedBytes: > 0, HasFreshDownloadGraphSample: true },
                "A late positive counter did not resolve the pending zero.");
            await delayedRepository.ApplyPollAsync([WithoutCounter(110)], origin.AddSeconds(10));
            var counterlessIdle = await delayedRepository.ApplyPollAsync([WithoutCounter(115)], origin.AddSeconds(15));
            Require(counterlessIdle.Devices.Single().Profile is { LastDownloadSpeedBytes: 0, HasFreshDownloadGraphSample: true },
                "Sustained idle without companion counters must still reach zero.");

            var cumulativeRepository = new DeviceRepository(Path.Combine(directory, "cumulative-state.json"));
            DeviceSnapshot OnlyCounter(long total, long revision) => Snapshot(0, total, revision) with { HasValidDownloadSpeedData = false };
            await cumulativeRepository.ApplyPollAsync([OnlyCounter(baseline, 100)], origin);
            var cumulativeIdle = await cumulativeRepository.ApplyPollAsync([OnlyCounter(baseline, 105)], origin.AddSeconds(5));
            Require(cumulativeIdle.Devices.Single().Profile is { LastDownloadSpeedBytes: 0, HasFreshDownloadGraphSample: true },
                "A new unchanged cumulative snapshot must measure idle even without instantaneous fields.");
            await cumulativeRepository.ApplyPollAsync([OnlyCounter(baseline, 130)], origin.AddSeconds(30));
            var resumed = await cumulativeRepository.ApplyPollAsync([OnlyCounter(baseline + 5 * rate, 135)], origin.AddSeconds(35));
            Require(resumed.Devices.Single().Profile.LastDownloadSpeedBytes == rate,
                "Traffic after idle was averaged over stale idle time.");

            await CheckHttpCadenceAsync(directory);
            using var stalledHttp = new HttpClient(new RouterHandler(3000));
            using var stalledClient = new XiaomiRouterClient(stalledHttp);
            stalledClient.Configure("router.example.test", "test-password");
            var stalledWatch = Stopwatch.StartNew();
            var fallback = await stalledClient.GetDeviceSnapshotsAsync();
            Require(stalledWatch.Elapsed < TimeSpan.FromSeconds(1.2) && fallback.Single().DownloadSpeedBytes == rate,
                "A stalled auxiliary endpoint blocked device polling or cleared its rate.");
            using var cancel = new CancellationTokenSource(50);
            try
            {
                await stalledClient.GetDeviceSnapshotsAsync(cancel.Token);
                throw new InvalidOperationException("Caller cancellation was swallowed.");
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            Console.WriteLine("Traffic snapshot PASS: 65-second replay, cached/partial/missing data, counter-backed rates, true idle, independent directions, slow HTTP cadence and cancellation.");
            return 0;
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task CheckHttpCadenceAsync(string directory)
    {
        using var http = new HttpClient(new RouterHandler());
        using var client = new XiaomiRouterClient(http);
        using var monitor = new MonitoringService(client, new DeviceRepository(Path.Combine(directory, "http-state.json")));
        var times = new List<double>();
        var watch = Stopwatch.StartNew();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new List<Exception>();
        var statusSamples = 0;
        client.TrafficSampleObserved += (_, sample) => { if (sample.StatusTotalDownload.HasValue) statusSamples++; };
        monitor.MonitoringError += (_, error) => errors.Add(error);
        monitor.Updated += (_, _) =>
        {
            times.Add(watch.Elapsed.TotalSeconds);
            if (times.Count >= 7) done.TrySetResult();
        };
        try
        {
            await monitor.ConnectAndStartAsync("router.example.test", "test-password", 1);
            await done.Task.WaitAsync(TimeSpan.FromSeconds(12));
            await monitor.StopAsync();
            Require(errors.Count == 0, "Slow optional requests failed monitoring.");
            Require(statusSamples == times.Count, "A normal 450 ms status response lost its traffic counters.");
            // Exclude the deliberately delayed first background iteration.
            var gaps = times.Zip(times.Skip(1), (a, b) => b - a).Skip(1).ToArray();
            Require(gaps.All(gap => gap is > .75 and < 1.3), $"One-second updates were delayed: {string.Join(", ", gaps.Select(g => g.ToString("F3")))}.");
            var stopped = times.Count;
            await Task.Delay(1100);
            Require(times.Count == stopped, "Stopped monitoring still published a sample.");
        }
        finally { await monitor.StopAsync(); }
    }

    private static DeviceSnapshot Snapshot(long download, long total, long revision) => new(
        "02:00:00:00:77:01", "采样测试设备", "snapshot-test", true, "192.0.2.20", 2,
        revision, 0, download, 0, total);

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class RouterHandler(int statusDelayMilliseconds = 450) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var endpoint = request.RequestUri!.AbsolutePath.Split('/')[^1];
            if (endpoint == "status") await Task.Delay(statusDelayMilliseconds, cancellationToken);
            if (endpoint == "wan_info") await Task.Delay(2000, cancellationToken);
            var body = endpoint switch
            {
                "web" => "key: 'test-key'; var deviceId = 'test-device';",
                "init_info" => """{"code":0,"newEncryptMode":1}""",
                "login" => """{"code":0,"token":"test-token"}""",
                "devicelist" => """{"code":0,"list":[{"mac":"02:00:00:00:77:01","name":"采样测试设备","online":1,"statistics":{"online":100,"upspeed":0,"downspeed":2097152}}]}""",
                "status" => """{"code":0,"dev":[{"mac":"02:00:00:00:77:01","online":100,"upspeed":0,"downspeed":2097152,"upload":0,"download":100000000}]}""",
                "topo_graph" => """{"code":0,"graph":{}}""",
                "wan_info" => """{"code":0,"info":{}}""",
                _ => throw new InvalidOperationException($"Unexpected test endpoint: {endpoint}")
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
