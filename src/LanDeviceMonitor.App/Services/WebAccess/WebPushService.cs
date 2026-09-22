using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using WebPush;

namespace LanDeviceMonitor.Services.WebAccess;

internal sealed class WebPushService : IDisposable
{
    private readonly ProtectedWebStore _store;
    private readonly HttpClient _http;
    private readonly WebPushClient _client;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Channel<(string Title, string Body, string Tag, long Generation)> _queue = Channel.CreateBounded<(string,string,string,long)>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private readonly Task _worker;
    private long _generation;
    public long Generation => Interlocked.Read(ref _generation);
    private readonly object _lifecycleGate = new();
    private CancellationTokenSource _delivery = new();
    private volatile bool _enabled;
    public string Status { get; private set; } = "尚未发送通知";
    public bool Enabled
    {
        get => _enabled;
        set
        {
            lock (_lifecycleGate)
            {
                _enabled = value;
                Interlocked.Increment(ref _generation);
                _delivery.Cancel(); _delivery.Dispose(); _delivery = new();
                if (!value) _delivery.Cancel();
            }
        }
    }
    public WebPushService(ProtectedWebStore store, HttpMessageHandler? handler = null)
    {
        _store = store;
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        _client = new WebPushClient(_http);
        _worker = RunAsync();
    }
    public static bool IsAllowedEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
        var host = uri.IdnHost;
        return host == "fcm.googleapis.com" || host == "updates.push.services.mozilla.com" ||
            host.EndsWith(".push.apple.com", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".notify.windows.com", StringComparison.OrdinalIgnoreCase);
    }
    public static bool IsValidSubscription(WebSubscription s)
    {
        if (string.IsNullOrEmpty(s.Endpoint) || string.IsNullOrEmpty(s.P256dh) || string.IsNullOrEmpty(s.Auth)) return false;
        if (!IsAllowedEndpoint(s.Endpoint) || s.Endpoint.Length > 2048) return false;
        try
        {
            static byte[] Decode(string text) => Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/').PadRight((text.Length + 3) / 4 * 4, '='));
            var key = Decode(s.P256dh); var auth = Decode(s.Auth);
            if (key.Length != 65 || key[0] != 4 || auth.Length != 16) return false;
            using var curve = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = key[1..33], Y = key[33..65] } });
            return true;
        }
        catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException) { return false; }
    }
    public void Enqueue(string title, string body, string tag)
    {
        if (Enabled && !_queue.Writer.TryWrite((title, body, tag, Interlocked.Read(ref _generation)))) Status = "通知队列已满，本条未发送";
    }
    public async Task<string> SendAsync(WebSubscription s, string title, string body, string tag, CancellationToken ct, long? generation = null)
    {
        CancellationTokenSource linked;
        lock (_lifecycleGate)
        {
            if (!Enabled || generation.HasValue && generation != _generation) return "网页通知已关闭";
            linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _delivery.Token, _shutdown.Token);
        }
        using var requestLifetime = linked;
        if (!IsValidSubscription(s)) return "无效订阅";
        try
        {
            var payload = JsonSerializer.Serialize(new { title, body, tag });
            var options = new Dictionary<string, object> { ["vapidDetails"] = _store.Vapid, ["TTL"] = 300 };
            await _client.SendNotificationAsync(new PushSubscription(s.Endpoint, s.P256dh, s.Auth), payload, options, linked.Token);
            return Status = $"{DateTime.Now:HH:mm:ss} 推送服务已接受；请在手机确认是否收到";
        }
        catch (WebPushException e) when (e.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
        { _store.Remove(s.Endpoint); return Status = "订阅已过期，请在该设备重新启用通知"; }
        catch (OperationCanceledException) { return Status = "推送已取消或超时"; }
        catch (Exception e) when (e is HttpRequestException or WebPushException or CryptographicException)
        { return Status = "推送失败，请检查网络和浏览器推送服务的可达性"; }
    }
    private async Task RunAsync()
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(_shutdown.Token))
                foreach (var s in _store.Subscriptions)
                {
                    if (!Enabled || item.Generation != Interlocked.Read(ref _generation)) break;
                    await SendAsync(s, item.Title, item.Body, item.Tag, _shutdown.Token, item.Generation);
                }
        }
        catch (OperationCanceledException) { }
        catch { Status = "推送后台任务异常，请重新启动局域网访问"; }
    }
    public void Dispose() { Enabled = false; _queue.Writer.TryComplete(); _shutdown.Cancel(); _http.Dispose(); }
}
