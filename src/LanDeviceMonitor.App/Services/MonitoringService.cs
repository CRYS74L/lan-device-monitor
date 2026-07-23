using System.Net.Http;
using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Services;

public sealed class MonitoringService : IDisposable
{
    private readonly XiaomiRouterClient _routerClient;
    private readonly DeviceRepository _repository;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private CancellationTokenSource? _cancellation;
    private Task? _loopTask;
    private int _refreshIntervalSeconds = 3;
    private bool _isDisposed;

    public MonitoringService(XiaomiRouterClient routerClient, DeviceRepository repository)
    {
        _routerClient = routerClient;
        _repository = repository;
    }

    public event EventHandler<MonitorUpdate>? Updated;
    public event EventHandler<string>? ConnectionStateChanged;
    public event EventHandler<Exception>? MonitoringError;
    public event EventHandler<string>? RouterRestartProgressChanged;

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

        _refreshIntervalSeconds = Math.Clamp(refreshIntervalSeconds, 2, 60);
        StartLoop();
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

    public Task<RouterInternetInfo> GetInternetInfoAsync(
        CancellationToken cancellationToken = default) =>
        _routerClient.GetInternetInfoAsync(cancellationToken);

    public async Task RestartRouterAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);
        cancellationToken = linkedCancellation.Token;
        await StopAsync();
        RouterRestartProgressChanged?.Invoke(this, "正在向路由器发送重启指令…");

        try
        {
            await _routerClient.RestartRouterAsync(cancellationToken);
            RouterRestartProgressChanged?.Invoke(this, "路由器正在重启，请保持应用开启…");

            // 给路由器留出真正进入重启阶段的时间，避免旧会话在关机前被误判为恢复。
            await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
            var startedAt = DateTimeOffset.Now;
            Exception? lastError = null;
            while (DateTimeOffset.Now - startedAt < TimeSpan.FromMinutes(2))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await _routerClient.AuthenticateAsync(cancellationToken);
                    var internetInfo = await _routerClient.GetInternetInfoAsync(cancellationToken);
                    if (!internetInfo.IsConnected)
                    {
                        throw new RouterApiException("路由器已启动，正在等待外网连接。");
                    }

                    var update = await PollOnceAsync(cancellationToken);
                    Updated?.Invoke(this, update);
                    ConnectionStateChanged?.Invoke(this, "已连接");
                    RouterRestartProgressChanged?.Invoke(this, "路由器已重启并恢复连接");
                    StartLoop();
                    return;
                }
                catch (Exception exception) when (
                    exception is RouterApiException or HttpRequestException or TaskCanceledException)
                {
                    lastError = exception;
                }

                var elapsed = (int)(DateTimeOffset.Now - startedAt).TotalSeconds;
                RouterRestartProgressChanged?.Invoke(
                    this,
                    $"路由器正在重启，已等待 {elapsed} 秒…");
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }

            throw new RouterApiException(
                "路由器重启等待超时，请稍后检查网络连接。",
                innerException: lastError);
        }
        catch
        {
            if (!_isDisposed)
            {
                StartLoop();
            }

            throw;
        }
    }

    private void StartLoop()
    {
        if (_isDisposed)
        {
            return;
        }

        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_refreshIntervalSeconds, _cancellation.Token);
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
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _disposeCancellation.Cancel();
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _routerClient.Dispose();
        _disposeCancellation.Dispose();
    }
}
