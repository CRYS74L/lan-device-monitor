using System.Net.Http;
using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Services;

public sealed class MonitoringService : IDisposable
{
    private readonly XiaomiRouterClient _routerClient;
    private readonly DeviceRepository _repository;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly SemaphoreSlim _loopRestartGate = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private Task? _loopTask;
    private RouterInternetInfo? _lastInternetInfo;
    private int _refreshIntervalSeconds = 1;
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
        await StopAsync().ConfigureAwait(false);
        _routerClient.Configure(routerAddress, password);
        ConnectionStateChanged?.Invoke(this, "正在连接路由器…");
        await Task.Run(
                () => _routerClient.AuthenticateAsync(cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        ConnectionStateChanged?.Invoke(this, "已连接");

        var firstUpdate = await Task.Run(
                () => PollOnceAsync(cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        Updated?.Invoke(this, firstUpdate);

        _refreshIntervalSeconds = Math.Clamp(refreshIntervalSeconds, 1, 60);
        StartLoop();
    }

    public async Task StopAsync()
    {
        var cancellation = _cancellation;
        var loopTask = _loopTask;
        _cancellation = null;
        _loopTask = null;

        if (cancellation is null && loopTask is null)
        {
            return;
        }

        cancellation?.Cancel();
        if (loopTask is not null)
        {
            try
            {
                await loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation?.Dispose();
    }

    public Task<RouterInternetInfo> GetInternetInfoAsync(
        CancellationToken cancellationToken = default) =>
        _routerClient.GetInternetInfoAsync(cancellationToken);

    public Task<IReadOnlyList<MeshNodeInfo>> GetMeshNodesAsync(
        CancellationToken cancellationToken = default) =>
        _routerClient.GetMeshNodesAsync(cancellationToken);

    public void UpdateRefreshInterval(int refreshIntervalSeconds)
    {
        _refreshIntervalSeconds = Math.Clamp(refreshIntervalSeconds, 1, 60);
        if (_loopTask is not null && !_loopTask.IsCompleted)
        {
            _ = RestartLoopAsync();
        }
    }

    public async Task RestartRouterAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);
        cancellationToken = linkedCancellation.Token;
        await StopAsync().ConfigureAwait(false);
        RouterRestartProgressChanged?.Invoke(this, "正在向路由器发送重启指令…");

        try
        {
            await _routerClient.RestartRouterAsync(cancellationToken).ConfigureAwait(false);
            RouterRestartProgressChanged?.Invoke(this, "路由器正在重启，请保持应用开启…");

            // 给路由器留出真正进入重启阶段的时间，避免旧会话在关机前被误判为恢复。
            await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false);
            var startedAt = DateTimeOffset.Now;
            Exception? lastError = null;
            while (DateTimeOffset.Now - startedAt < TimeSpan.FromMinutes(2))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await _routerClient.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
                    var internetInfo = await _routerClient.GetInternetInfoAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (!internetInfo.IsConnected)
                    {
                        throw new RouterApiException("路由器已启动，正在等待外网连接。");
                    }

                    var update = await PollOnceAsync(cancellationToken).ConfigureAwait(false);
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
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
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
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _loopTask = RunLoopAsync(_refreshIntervalSeconds, cancellation.Token);
    }

    private async Task RunLoopAsync(int refreshIntervalSeconds, CancellationToken cancellationToken)
    {
        var loop = new MonitoringLoop(
            TimeSpan.FromSeconds(Math.Max(1, refreshIntervalSeconds)),
            PollAndPublishAsync);
        await loop.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task PollAndPublishAsync(CancellationToken cancellationToken)
    {
        try
        {
            var update = await PollOnceAsync(cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            Updated?.Invoke(this, update);
            ConnectionStateChanged?.Invoke(this, "已连接");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ConnectionStateChanged?.Invoke(this, "连接异常，正在重试");
            // 失败不是“真实零流量”。UI 会保留最近有效采样，待下一次
            // 成功轮询后继续推进固定 1 秒时间槽。
            MonitoringError?.Invoke(this, exception);
        }
    }

    private async Task<MonitorUpdate> PollOnceAsync(CancellationToken cancellationToken)
    {
        var snapshotsTask = _routerClient.GetDeviceSnapshotsAsync(cancellationToken);
        // 上网信息与设备快照在同一个轮询周期启动并等待。这样启用 1 秒
        // 刷新时，详情页看到的是同一轮的完整数据，而不是沿用跨轮次的旧任务。
        var internetInfoTask = TryGetInternetInfoAsync(cancellationToken);
        var snapshots = await snapshotsTask.ConfigureAwait(false);
        var internetInfo = await internetInfoTask.ConfigureAwait(false);

        var update = await _repository.ApplyPollAsync(
            snapshots,
            DateTimeOffset.Now,
            cancellationToken).ConfigureAwait(false);

        if (internetInfo is not null)
        {
            _lastInternetInfo = internetInfo;
        }

        return update with { InternetInfo = _lastInternetInfo };
    }

    private async Task<RouterInternetInfo?> TryGetInternetInfoAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _routerClient.GetInternetInfoAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested &&
            exception is RouterApiException or HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _disposeCancellation.Cancel();
        var cancellation = _cancellation;
        var loopTask = _loopTask;
        _cancellation = null;
        _loopTask = null;
        cancellation?.Cancel();

        if (loopTask is null || loopTask.IsCompleted)
        {
            cancellation?.Dispose();
            _routerClient.Dispose();
            return;
        }

        // Dispose 可能由窗口关闭事件同步调用。不要在 UI 线程阻塞等待
        // 正在取消的 HTTP 请求；等轮询任务退出后再释放 HttpClient/CTS。
        _ = FinishDisposeAsync(loopTask, cancellation);
    }

    private async Task RestartLoopAsync()
    {
        await _loopRestartGate.WaitAsync();
        try
        {
            if (_isDisposed)
            {
                return;
            }

            await StopAsync();
            if (!_isDisposed)
            {
                StartLoop();
            }
        }
        finally
        {
            _loopRestartGate.Release();
        }
    }

    private async Task FinishDisposeAsync(
        Task loopTask,
        CancellationTokenSource? cancellation)
    {
        try
        {
            await loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // 应用正在退出；轮询异常不应阻止资源释放。
        }
        finally
        {
            cancellation?.Dispose();
            _routerClient.Dispose();
        }
    }
}
