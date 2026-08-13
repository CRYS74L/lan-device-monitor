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
            // UI 调度或快速重连可能让同一秒落入两次结果。后一份 0 值不应
            // 覆盖这一秒已经确认的有效速率，否则图表会出现单像素宽的竖直
            // 归零线。两条方向独立取可信值，真实持续空闲会在后续整秒经
            // 数据层确认后自然写入 0。
            _samples.Last.Value = MergeSameSlot(last, normalized);
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
        RepairIsolatedZeroSample();
        Trim(normalized.Timestamp);
    }

    public IReadOnlyList<TrafficSample> Snapshot(DateTimeOffset now)
    {
        Trim(NormalizeTimestamp(now));
        return _samples.ToArray();
    }

    public bool HasSamples => _samples.Count > 0;

    private static TrafficSample Normalize(TrafficSample sample) =>
        new(
            NormalizeTimestamp(sample.Timestamp),
            Math.Max(0, sample.UploadBytesPerSecond),
            Math.Max(0, sample.DownloadBytesPerSecond));

    private static TrafficSample MergeSameSlot(
        TrafficSample previous,
        TrafficSample current) => new(
            current.Timestamp,
            MergeSameDirection(previous.UploadBytesPerSecond, current.UploadBytesPerSecond),
            MergeSameDirection(previous.DownloadBytesPerSecond, current.DownloadBytesPerSecond));

    private static long MergeSameDirection(long previous, long current) =>
        previous > 0 && current == 0 ? previous : current;

    private void RepairIsolatedZeroSample()
    {
        // 新点到达后才能证明前一个 0 是孤立的。只修复“正值 → 0 → 正值”
        // 的中间点；两端任意一侧持续为 0 都保留，确保真实停止传输仍能显示。
        var current = _samples.Last;
        var previous = current?.Previous;
        var beforePrevious = previous?.Previous;
        if (beforePrevious is null || previous is null || current is null)
        {
            return;
        }

        var repaired = previous.Value with
        {
            UploadBytesPerSecond = RepairDirection(
                beforePrevious.Value.UploadBytesPerSecond,
                previous.Value.UploadBytesPerSecond,
                current.Value.UploadBytesPerSecond),
            DownloadBytesPerSecond = RepairDirection(
                beforePrevious.Value.DownloadBytesPerSecond,
                previous.Value.DownloadBytesPerSecond,
                current.Value.DownloadBytesPerSecond)
        };
        previous.Value = repaired;
    }

    private static long RepairDirection(long before, long candidate, long after) =>
        candidate == 0 && before > 0 && after > 0
            ? InterpolatePositiveRate(before, after)
            : candidate;

    private static long InterpolatePositiveRate(long before, long after) =>
        before >= long.MaxValue - after
            ? Math.Max(before, after)
            : (before + after) / 2;

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
