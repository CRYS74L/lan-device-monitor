namespace LanDeviceMonitor.Services;

internal sealed class MonitoringLoop
{
    private readonly TimeSpan _interval;
    private readonly Func<CancellationToken, Task> _iterationAsync;

    public MonitoringLoop(
        TimeSpan interval,
        Func<CancellationToken, Task> iterationAsync)
    {
        _interval = interval > TimeSpan.Zero ? interval : TimeSpan.FromSeconds(1);
        _iterationAsync = iterationAsync;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await _iterationAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
