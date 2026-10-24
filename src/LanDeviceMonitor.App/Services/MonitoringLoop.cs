using System.Diagnostics;

namespace LanDeviceMonitor.Services;

internal sealed class MonitoringLoop
{
    private readonly Func<TimeSpan> _interval;
    private readonly Func<CancellationToken, Task> _iterationAsync;

    public MonitoringLoop(
        TimeSpan interval,
        Func<CancellationToken, Task> iterationAsync)
    {
        _interval = () => interval;
        _iterationAsync = iterationAsync;
    }

    public MonitoringLoop(Func<TimeSpan> interval, Func<CancellationToken, Task> iterationAsync)
    {
        _interval = interval;
        _iterationAsync = iterationAsync;
    }

    internal static TimeSpan GetDelayAfterIteration(
        TimeSpan interval,
        TimeSpan iterationDuration)
    {
        interval = interval > TimeSpan.Zero
            ? interval
            : TimeSpan.FromSeconds(1);
        iterationDuration = iterationDuration > TimeSpan.Zero
            ? iterationDuration
            : TimeSpan.Zero;

        var remaining = interval - iterationDuration;
        // 请求未超过当前采样周期时，只等待剩余时间，让“轮询开始”
        // 按周期推进；请求超过周期时不再额外等待一个完整周期，但保留
        // 100ms 最小退避，避免慢请求完成后形成无间隔重试压力。循环本身
        // 仍然等待当前请求完成，不会并发重叠请求。
        return remaining > TimeSpan.Zero
            ? remaining
            : TimeSpan.FromMilliseconds(100);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var interval = _interval() > TimeSpan.Zero
            ? _interval()
            : TimeSpan.FromSeconds(1);

        var next = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency + interval.TotalSeconds;
        while (true)
        {
            var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, next - now)), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await _iterationAsync(cancellationToken).ConfigureAwait(false);
            var currentInterval = _interval();
            if (currentInterval <= TimeSpan.Zero) currentInterval = TimeSpan.FromSeconds(1);
            now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            next = currentInterval == interval ? next + interval.TotalSeconds : now + currentInterval.TotalSeconds;
            interval = currentInterval;
            if (next <= now) next = now + .1; // Slow requests never replay missed polls.
        }
    }
}
