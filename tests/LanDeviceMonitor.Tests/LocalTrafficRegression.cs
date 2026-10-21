using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class LocalTrafficRegression
{
    public static async Task<int> RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LanDeviceMonitor.Tests", $"local-traffic-{Guid.NewGuid():N}");
        const string mac = "02:00:00:00:78:01";
        const long baseline = 9_000_000_000;
        var origin = new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);
        var clock = 0d;
        long sent = 100_000, received = 1_000_000;
        IReadOnlyList<LocalTrafficSampler.Reading> readings = [];
        var sampler = new LocalTrafficSampler(() => readings, () => clock);
        var local = new DeviceSnapshot(mac, "测试电脑", "test-pc", true, "192.0.2.21", 0,
            100, 777, 999, baseline, baseline);
        var remote = local with { MacAddress = "02:00:00:00:78:02", IpAddress = "192.0.2.22" };
        try
        {
            var repository = new DeviceRepository(Path.Combine(directory, "state.json"));
            for (var second = 0; second <= 90; second++)
            {
                var expectedDownload = second <= 70 ? (second <= 40 ? 1024 * 1024 : 512 * 1024) : 0;
                var expectedUpload = second <= 70 ? 2048 : 0;
                if (second > 0) { sent += expectedUpload; received += expectedDownload; }
                clock = second;
                readings = [new("nic-a", mac, sent, received)];
                var samples = sampler.Apply([local, remote]);
                Require(samples[1].LocalTraffic is null, "Local counters leaked to another device.");
                var update = await repository.ApplyPollAsync(samples, origin.AddSeconds(second));
                if (second == 0) { Require(samples[0].LocalTraffic is null, "Initial bytes became a speed spike."); continue; }
                var profile = update.Devices.Single(d => d.Profile.MacAddress == mac).Profile;
                Require(profile.LastDownloadSpeedBytes == expectedDownload && profile.LastUploadSpeedBytes == expectedUpload,
                    $"Local one-second counters were replaced by cached router rates at {second}.");
                Require(profile.HasFreshDownloadGraphSample && profile.HasFreshUploadGraphSample && profile.UsesLocalTrafficSampler,
                    "Constant or zero local rates were incorrectly deduplicated.");
                Require(profile.LastTotalDownloadBytes == baseline && profile.ObservedTrafficIntervalSeconds == 1,
                    "The interface lifetime counter replaced router totals or changed sampling cadence.");
            }

            clock = 91.25;
            readings = [new("nic-a", mac, sent + 1250, received + 2500)];
            Require(sampler.Apply([local])[0].LocalTraffic is { Upload: 1000, Download: 2000, IntervalSeconds: 1.25 },
                "Rates used the requested interval instead of elapsed time.");
            clock = 92.25;
            readings = [new("nic-a", mac, 0, 0)];
            Require(sampler.Apply([local])[0].LocalTraffic is null, "Reset counters generated a negative or zero sample.");
            clock = 93.25;
            readings = [new("nic-a", mac, 10, 20)];
            Require(sampler.Apply([local])[0].LocalTraffic is { Upload: 10, Download: 20 }, "Reset counters did not recover.");
            clock = 94.25;
            readings = [new("nic-a", mac, 20, 40), new("nic-b", mac, 1000, 2000)];
            Require(sampler.Apply([local])[0].LocalTraffic is null, "Ambiguous interfaces were summed.");
            sampler.Reset();
            readings = [new("nic-a", mac, 10000, 20000)];
            Require(sampler.Apply([local])[0].LocalTraffic is null, "Reconnect reused an old baseline.");

            // Router fallback retains its own rate, not the last local zero.
            var fallback = await repository.ApplyPollAsync([local, remote], origin.AddSeconds(95));
            var fallbackProfile = fallback.Devices.Single(d => d.Profile.MacAddress == mac).Profile;
            Require(!fallbackProfile.UsesLocalTrafficSampler && fallbackProfile.LastDownloadSpeedBytes == 999,
                "Switching sources reused a rate from the wrong source.");
            Console.WriteLine("Local traffic PASS: 90-second replay, constant rate, step change, immediate idle, directions, MAC attribution, duplicates, counter reset, elapsed time and source fallback.");
            return 0;
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
