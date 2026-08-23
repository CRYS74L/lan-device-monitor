namespace LanDeviceMonitor.Services;

internal sealed class MonitoringLoop
{
    private readonly TimeSpan _interval;
    private readonly Func<CancellationToken, Task> _iterationAsync;

    public MonitoringLoop(
        TimeSpan interval,
        Func<CancellationToken, Task> iterationAsync)
    {
        _interval = interval;
        _iterationAsync = iterationAsync;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            // 在每次轮询完成后再等待一个完整周期，避免慢请求或超时
            // 触发“立即追赶”的连续轮询，给系统和 UI 留出稳定的喘息间隔。
            await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await _iterationAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
