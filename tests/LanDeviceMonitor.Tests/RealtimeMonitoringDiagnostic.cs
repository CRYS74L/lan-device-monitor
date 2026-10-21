using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class RealtimeMonitoringDiagnostic
{
    // Explicit live mode: read the saved address/credential without copying them
    // into fixtures or reports. All writes go to an isolated repository.
    public static async Task<int> RunAsync(string[] args)
    {
        var duration = int.Parse(Option("--seconds=", "90"));
        var outputPath = Path.GetFullPath(Option("--output=", Path.Combine(
            ".tools", "diagnostics", $"realtime-{DateTime.Now:yyyyMMdd-HHmmss}.json")));
        var statePath = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "LanDeviceMonitor", "state.json");
        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(statePath));
        var address = saved.RootElement.GetProperty("settings").GetProperty("routerAddress").GetString()!;
        var password = new RouterCredentialService().ReadPassword(address)
            ?? throw new InvalidOperationException("No saved router credential.");
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();
        var localMacs = interfaces
            .Select(n => n.GetPhysicalAddress().ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var localIps = interfaces.SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address.ToString()).ToHashSet();
        var directory = Path.Combine(Path.GetTempPath(), "LanDeviceMonitor.Tests", $"live-{Guid.NewGuid():N}");
        var requests = new ConcurrentQueue<RequestTiming>();
        var rows = new List<object>();
        var errors = new ConcurrentQueue<string>();
        var watch = Stopwatch.StartNew();
        using var http = new HttpClient(new TimingHandler(requests, watch)) { Timeout = TimeSpan.FromSeconds(8) };
        using var client = new XiaomiRouterClient(http);
        using var monitor = new MonitoringService(client, new DeviceRepository(Path.Combine(directory, "state.json")));
        var raw = new ConcurrentDictionary<string, TrafficSampleDiagnostics>(StringComparer.OrdinalIgnoreCase);
        string? target = null;
        var previousAt = 0d;
        long? previousReceived = null;
        NetworkInterface? targetInterface = null;
        client.TrafficSampleObserved += (_, sample) => raw[sample.MacAddress] = sample;
        monitor.MonitoringError += (_, error) => errors.Enqueue(error.GetType().Name);
        monitor.Updated += (_, update) =>
        {
            var device = target is null
                ? update.Devices.FirstOrDefault(d => localMacs.Contains(d.Profile.MacAddress.Replace(":", "")) || localIps.Contains(d.Profile.LastIpAddress))
                : update.Devices.FirstOrDefault(d => d.Profile.MacAddress == target);
            if (device is null) throw new InvalidOperationException("The current computer was not found in the router device list.");
            var p = device.Profile;
            target = p.MacAddress;
            raw.TryGetValue(target, out var sample);
            var elapsed = watch.Elapsed.TotalSeconds;
            var interval = previousAt == 0 ? 0 : elapsed - previousAt;
            targetInterface ??= interfaces.FirstOrDefault(n => n.GetPhysicalAddress().ToString().Equals(target.Replace(":", ""), StringComparison.OrdinalIgnoreCase));
            var received = targetInterface?.GetIPv4Statistics().BytesReceived;
            var nicDownload = received.HasValue && previousReceived.HasValue && interval > 0
                ? (received.Value - previousReceived.Value) / interval : (double?)null;
            lock (rows)
                rows.Add(new
                {
                    elapsed, interval, nicDownload,
                    listUpload = sample?.DeviceListUploadSpeed, listDownload = sample?.DeviceListDownloadSpeed,
                    statusUpload = sample?.StatusUploadSpeed, statusDownload = sample?.StatusDownloadSpeed,
                    listTotalUpload = sample?.DeviceListTotalUpload, listTotalDownload = sample?.DeviceListTotalDownload,
                    statusTotalUpload = sample?.StatusTotalUpload, statusTotalDownload = sample?.StatusTotalDownload,
                    displayUpload = p.LastUploadSpeedBytes, displayDownload = p.LastDownloadSpeedBytes,
                    graphUpload = p.LastUploadGraphSpeedBytes, graphDownload = p.LastDownloadGraphSpeedBytes,
                    freshUpload = p.HasFreshUploadGraphSample, freshDownload = p.HasFreshDownloadGraphSample,
                    online = p.LastKnownOnline, sampledAt = p.LastTrafficSampleAt,
                    observedInterval = p.ObservedTrafficIntervalSeconds,
                    localSampler = p.UsesLocalTrafficSampler
                });
            previousAt = elapsed;
            previousReceived = received;
            Console.WriteLine($"t={elapsed:F2}s interval={interval:F2}s list={sample?.DeviceListDownloadSpeed} status={sample?.StatusDownloadSpeed} display={p.LastDownloadSpeedBytes} graph={p.LastDownloadGraphSpeedBytes} fresh={p.HasFreshDownloadGraphSample}");
        };
        try
        {
            await monitor.ConnectAndStartAsync(address, password, 1);
            await Task.Delay(TimeSpan.FromSeconds(duration));
            await monitor.StopAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(new
            {
                durationSeconds = watch.Elapsed.TotalSeconds, rows, requests = requests.ToArray(), errors = errors.ToArray()
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Recorded {rows.Count} updates to {outputPath}; errors={errors.Count}.");
            return rows.Count > 0 ? 0 : 1;
        }
        finally
        {
            await monitor.StopAsync();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        string Option(string prefix, string fallback) => args.FirstOrDefault(a => a.StartsWith(prefix))?[prefix.Length..] ?? fallback;
    }

    private sealed record RequestTiming(string Endpoint, double Started, double Duration, bool Completed);

    private sealed class TimingHandler(ConcurrentQueue<RequestTiming> timings, Stopwatch watch) : DelegatingHandler(
        new HttpClientHandler { UseProxy = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.All })
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var start = watch.Elapsed.TotalSeconds;
            var completed = false;
            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                completed = response.IsSuccessStatusCode;
                return response;
            }
            finally
            {
                timings.Enqueue(new(request.RequestUri!.AbsolutePath.Split('/')[^1], start, watch.Elapsed.TotalSeconds - start, completed));
            }
        }
    }
}
