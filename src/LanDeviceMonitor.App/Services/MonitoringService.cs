using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Services;

public sealed class MonitoringService : IDisposable
{
    private readonly XiaomiRouterClient _routerClient;
    private readonly DeviceRepository _repository;
    private CancellationTokenSource? _cancellation;
    private Task? _loopTask;

    public MonitoringService(XiaomiRouterClient routerClient, DeviceRepository repository)
    {
        _routerClient = routerClient;
        _repository = repository;
    }

    public event EventHandler<MonitorUpdate>? Updated;
    public event EventHandler<string>? ConnectionStateChanged;
    public event EventHandler<Exception>? MonitoringError;

    public async Task ConnectAndStartAsync(
        string routerAddress,
        string password,
        int refreshIntervalSeconds,
        CancellationToken cancellationToken = default)
    {
        await StopAsync();
        _routerClient.Configure(routerAddress, password);
        ConnectionStateChanged?.Invoke(this, "正在连接路由器…");
        await _routerClient.AuthenticateAsync(cancellationToken);
        ConnectionStateChanged?.Invoke(this, "已连接");

        var firstUpdate = await PollOnceAsync(cancellationToken);
        Updated?.Invoke(this, firstUpdate);

        _cancellation = new CancellationTokenSource();
        _loopTask = RunLoopAsync(
            Math.Clamp(refreshIntervalSeconds, 2, 60),
            _cancellation.Token);
    }

    public async Task StopAsync()
    {
        if (_cancellation is null)
        {
            return;
        }

        _cancellation.Cancel();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cancellation.Dispose();
        _cancellation = null;
        _loopTask = null;
    }

    private async Task RunLoopAsync(int refreshIntervalSeconds, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(refreshIntervalSeconds));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                var update = await PollOnceAsync(cancellationToken);
                Updated?.Invoke(this, update);
                ConnectionStateChanged?.Invoke(this, "已连接");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                ConnectionStateChanged?.Invoke(this, "连接异常，正在重试");
                MonitoringError?.Invoke(this, exception);
            }
        }
    }

    private async Task<MonitorUpdate> PollOnceAsync(CancellationToken cancellationToken)
    {
        var snapshots = await _routerClient.GetDeviceSnapshotsAsync(cancellationToken);
        return await _repository.ApplyPollAsync(
            snapshots,
            DateTimeOffset.Now,
            cancellationToken);
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _routerClient.Dispose();
    }
}
