using System.Diagnostics;
using System.Net.NetworkInformation;
using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Services;

internal sealed class LocalTrafficSampler
{
    internal sealed record Reading(string Id, string Mac, long Sent, long Received);
    private readonly Func<IReadOnlyList<Reading>> _read;
    private readonly Func<double> _clock;
    private readonly Dictionary<string, (Reading Reading, double At)> _previous = new();

    public LocalTrafficSampler() : this(ReadInterfaces, () => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency) { }

    internal LocalTrafficSampler(Func<IReadOnlyList<Reading>> read, Func<double> clock)
    { _read = read; _clock = clock; }

    public void Reset() => _previous.Clear();

    public IReadOnlyList<DeviceSnapshot> Apply(IReadOnlyList<DeviceSnapshot> snapshots)
    {
        var readings = _read();
        var now = _clock();
        var rates = new Dictionary<string, LocalTrafficRate>(StringComparer.OrdinalIgnoreCase);
        // A duplicated MAC is ambiguous (for example a virtual switch sharing
        // an adapter address). Do not sum interfaces or attribute by IP alone.
        foreach (var group in readings.GroupBy(r => NormalizeMac(r.Mac), StringComparer.OrdinalIgnoreCase))
        {
            if (group.Key.Length != 12 || group.Key.All(c => c == '0') || group.Count() != 1) continue;
            var current = group.Single();
            if (_previous.TryGetValue(current.Id, out var previous) &&
                NormalizeMac(previous.Reading.Mac) == group.Key && now > previous.At &&
                current.Sent >= previous.Reading.Sent && current.Received >= previous.Reading.Received)
            {
                var interval = now - previous.At;
                rates[group.Key] = new(
                    Rate(current.Sent - previous.Reading.Sent, interval),
                    Rate(current.Received - previous.Reading.Received, interval), interval);
            }
        }
        _previous.Clear();
        foreach (var r in readings) _previous[r.Id] = (r, now);
        return snapshots.Select(s => s.IsOnline && rates.TryGetValue(NormalizeMac(s.MacAddress), out var rate)
            ? s with { LocalTraffic = rate } : s).ToArray();
    }

    private static long Rate(long delta, double seconds)
    {
        var value = delta / seconds;
        return value >= long.MaxValue ? long.MaxValue : (long)Math.Round(value);
    }

    private static string NormalizeMac(string mac) => mac.Replace(":", "").Replace("-", "").ToUpperInvariant();

    private static IReadOnlyList<Reading> ReadInterfaces()
    {
        var result = new List<Reading>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)) continue;
            var statistics = nic.GetIPStatistics();
            result.Add(new(nic.Id, nic.GetPhysicalAddress().ToString(), statistics.BytesSent, statistics.BytesReceived));
        }
        return result;
    }
}
