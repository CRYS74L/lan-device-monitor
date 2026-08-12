using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Services;

public sealed class TrafficHistoryBuffer
{
    public const int WindowMinutes = 10;

    public const int SampleIntervalSeconds = 1;

    private const int MaximumSamples = WindowMinutes * 60 + 1;
    private readonly LinkedList<TrafficSample> _samples = [];

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
            _samples.Last.Value = normalized;
            return;
        }

        var nextTimestamp = last.Timestamp.AddSeconds(SampleIntervalSeconds);
        while (nextTimestamp < normalized.Timestamp)
        {
            // 时间槽缺失表示“没有拿到新数据”，不是路由器报告了真实 0。
            // 沿用最近一次有效采样，避免 HTTP 抖动在曲线上制造垂直断崖；
            // 成功返回的 (0, 0) 仍会作为真实零值写入并被后续槽位沿用。
            _samples.AddLast(new TrafficSample(
                nextTimestamp,
                last.UploadBytesPerSecond,
                last.DownloadBytesPerSecond));
            nextTimestamp = nextTimestamp.AddSeconds(SampleIntervalSeconds);
        }

        _samples.AddLast(normalized);
        Trim(normalized.Timestamp);
    }

    public IReadOnlyList<TrafficSample> Snapshot(DateTimeOffset now)
    {
        Trim(NormalizeTimestamp(now));
        return _samples.ToArray();
    }

    private static TrafficSample Normalize(TrafficSample sample) =>
        new(
            NormalizeTimestamp(sample.Timestamp),
            Math.Max(0, sample.UploadBytesPerSecond),
            Math.Max(0, sample.DownloadBytesPerSecond));

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset timestamp) =>
        timestamp.AddTicks(-(timestamp.Ticks % TimeSpan.TicksPerSecond));

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
