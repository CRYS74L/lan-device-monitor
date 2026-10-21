using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Services;

internal sealed class RouterSamplingCadence
{
    private readonly Dictionary<string, DeviceSnapshot> _previous = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<double> _intervals = new();
    private DateTimeOffset? _changedAt;
    private bool _hasCounterSource;
    public double? IntervalSeconds { get; private set; }

    public void Reset()
    {
        _previous.Clear();
        _intervals.Clear();
        _changedAt = null;
        _hasCounterSource = false;
        IntervalSeconds = null;
    }

    public void Observe(IReadOnlyList<DeviceSnapshot> snapshots, DateTimeOffset now)
    {
        if (IntervalSeconds.HasValue) return;
        _hasCounterSource |= snapshots.Any(s => s.IsOnline && (s.HasValidTotalUploadData || s.HasValidTotalDownloadData));
        var changed = false;
        var current = new Dictionary<string, DeviceSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in snapshots.Where(s => s.IsOnline))
        {
            current[sample.MacAddress] = sample;
            if (!_previous.TryGetValue(sample.MacAddress, out var old)) continue;
            // Online duration is a live clock even in a cached traffic frame.
            // Only actual traffic fields provide evidence of a new sample.
            changed |= sample.HasValidTotalUploadData && old.HasValidTotalUploadData && sample.TotalUploadBytes > old.TotalUploadBytes;
            changed |= sample.HasValidTotalDownloadData && old.HasValidTotalDownloadData && sample.TotalDownloadBytes > old.TotalDownloadBytes;
            if (!_hasCounterSource)
            {
                changed |= sample.HasValidUploadSpeedData && old.HasValidUploadSpeedData && sample.UploadSpeedBytes != old.UploadSpeedBytes;
                changed |= sample.HasValidDownloadSpeedData && old.HasValidDownloadSpeedData && sample.DownloadSpeedBytes != old.DownloadSpeedBytes;
            }
            // A partial response does not erase the previous counter baseline.
            current[sample.MacAddress] = sample with
            {
                TotalUploadBytes = sample.HasValidTotalUploadData ? sample.TotalUploadBytes : old.TotalUploadBytes,
                TotalDownloadBytes = sample.HasValidTotalDownloadData ? sample.TotalDownloadBytes : old.TotalDownloadBytes,
                HasValidTotalUploadData = sample.HasValidTotalUploadData || old.HasValidTotalUploadData,
                HasValidTotalDownloadData = sample.HasValidTotalDownloadData || old.HasValidTotalDownloadData
            };
        }
        _previous.Clear();
        foreach (var pair in current) _previous[pair.Key] = pair.Value;
        if (!changed) return;
        if (_changedAt is { } last)
        {
            var seconds = (now - last).TotalSeconds;
            if (seconds is >= .5 and <= 60)
            {
                _intervals.Enqueue(seconds);
                if (_intervals.Count > 8) _intervals.Dequeue();
            }
            else _intervals.Clear();
        }
        _changedAt = now;
        if (_intervals.Count < 4) return;
        var sorted = _intervals.Order().ToArray();
        var median = sorted[sorted.Length / 2];
        // A one-second probe quantizes a fractional source period into adjacent
        // intervals (for example 2,3,2,3 for 2.5 seconds).
        var consistent = sorted.Where(s => Math.Abs(s - median) <= Math.Max(1.1, median * .15)).ToArray();
        if (consistent.Length < 4 || consistent.Length < sorted.Length * .75) return;
        var average = consistent.Average();
        // Remove request/clock jitter, retaining non-integral source periods.
        var rounded = Math.Round(average);
        IntervalSeconds = Math.Max(1, Math.Abs(average - rounded) <= .15 ? rounded : Math.Round(average, 1));
    }
}
