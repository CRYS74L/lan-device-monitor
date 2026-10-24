using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Services;

public sealed class TrafficHistoryBuffer
{
    public const int WindowMinutes = 10;

    public const int SampleIntervalSeconds = 1;

    private const int MaximumSamples = (WindowMinutes + 2) * 60 + 3;
    private readonly LinkedList<TrafficSample> _samples = [];
    private Dictionary<DateTimeOffset, TrafficSample> _chartSamples = new();

    public void Append(TrafficSample sample)
    {
        var normalized = Normalize(sample);
        if (_samples.Last is null)
        {
            _samples.AddLast(normalized);
            Trim(normalized.Timestamp);
            return;
        }

        var last = _samples.Last.Value;
        if (normalized.Timestamp < last.Timestamp)
        {
            return;
        }

        if (normalized.Timestamp == last.Timestamp)
        {
            // Only identical observation times replace a point. Distinct
            // observations within the same second keep their full precision.
            _samples.Last.Value = normalized;
            return;
        }

        _samples.AddLast(normalized);
        Trim(normalized.Timestamp);
    }

    public IReadOnlyList<TrafficSample> Snapshot(DateTimeOffset now)
    {
        Trim(now);
        return _samples.ToArray();
    }

    public IReadOnlyList<TrafficSample> ChartSnapshot(DateTimeOffset now)
    {
        var projected = TrafficSampleGrid.Project(Snapshot(now), _chartSamples);
        _chartSamples = projected.ToDictionary(s => s.ReceivedAt ?? s.Timestamp);
        return projected;
    }

    public bool HasSamples => _samples.Count > 0;

    private static TrafficSample Normalize(TrafficSample sample) =>
        sample with { UploadBytesPerSecond = Math.Max(0, sample.UploadBytesPerSecond),
            DownloadBytesPerSecond = Math.Max(0, sample.DownloadBytesPerSecond) };

    private void Trim(DateTimeOffset now)
    {
        var cutoff = now - TimeSpan.FromMinutes(WindowMinutes + 2);
        while (_samples.First is { Value.Timestamp: var timestamp } &&
               timestamp < cutoff)
        {
            _samples.RemoveFirst();
        }

        while (_samples.Count > MaximumSamples)
        {
            _samples.RemoveFirst();
        }
    }
}
