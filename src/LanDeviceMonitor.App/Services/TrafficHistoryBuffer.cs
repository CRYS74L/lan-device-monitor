using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Services;

public sealed class TrafficHistoryBuffer
{
    public const int WindowMinutes = 10;

    private const int MaximumSamples = WindowMinutes * 60 + 1;
    private readonly LinkedList<TrafficSample> _samples = [];

    public void Append(TrafficSample sample)
    {
        _samples.AddLast(sample);
        Trim(sample.Timestamp);
    }

    public IReadOnlyList<TrafficSample> Snapshot(DateTimeOffset now)
    {
        Trim(now);
        return _samples.ToArray();
    }

    private void Trim(DateTimeOffset now)
    {
        var cutoff = now - TimeSpan.FromMinutes(WindowMinutes);
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
