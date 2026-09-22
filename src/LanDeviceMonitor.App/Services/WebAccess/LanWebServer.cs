using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using LanDeviceMonitor.ViewModels;
using LanDeviceMonitor.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LanDeviceMonitor.Services.WebAccess;

internal sealed class LanWebServer : IAsyncDisposable
{
    private readonly ProtectedWebStore _store;
    private readonly WebPushService _push;
    private WebApplication? _app;
    private X509Certificate2? _certificate;
    private readonly object _certificateGate = new();
    private readonly List<X509Certificate2> _retiredCertificates = [];
    private readonly object _dataGate = new();
    private byte[] _snapshot = "{\"devices\":[],\"refreshedAt\":null,\"connection\":\"等待监控数据\",\"mesh\":[]}"u8.ToArray();
    private readonly Dictionary<string, Queue<object>> _history = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<MeshNodeInfo> _mesh = [];
    private string _connection = "等待监控数据";
    private DateTimeOffset _lastTest;
    private readonly SemaphoreSlim _testGate = new(1, 1);
    public bool IsRunning => _app is not null;
    public string[] Urls { get; private set; } = [];
    public LanWebServer(ProtectedWebStore store, WebPushService push) { _store = store; _push = push; }
    public void SetConnection(string state)
    {
        lock (_dataGate)
        {
            _connection = state;
            var data = JsonNode.Parse(_snapshot)!; data["connection"] = state;
            _snapshot = JsonSerializer.SerializeToUtf8Bytes(data);
        }
    }
    public void SetMesh(IReadOnlyList<MeshNodeInfo> mesh) { lock (_dataGate) _mesh = mesh; }
    public void Publish(MonitorUpdate update)
    {
        lock (_dataGate)
        {
            // Enabling web access after monitoring is already connected does not
            // trigger MonitoringService's deduplicated connection-state event.
            if (_connection == "等待监控数据" && update.RefreshedAt > DateTimeOffset.MinValue)
                _connection = "已连接";
            var devices = update.Devices.Select(d =>
            {
                var p = d.Profile;
                if (!_history.TryGetValue(p.MacAddress, out var history)) _history[p.MacAddress] = history = new();
                if (p.HasFreshUploadGraphSample || p.HasFreshDownloadGraphSample)
                {
                    history.Enqueue(new { at = update.RefreshedAt, upload = p.HasFreshUploadGraphSample ? (long?)p.LastUploadGraphSpeedBytes : null, download = p.HasFreshDownloadGraphSample ? (long?)p.LastDownloadGraphSpeedBytes : null });
                    while (history.Count > 300) history.Dequeue();
                }
                return new { id = p.MacAddress, name = d.DisplayName, type = d.TypeName, online = p.LastKnownOnline,
                    ip = p.LastIpAddress, mac = p.MacAddress, connection = DeviceItemViewModel.ConnectionTypeToText(p.LastConnectionType),
                    upload = p.LastUploadSpeedBytes, download = p.LastDownloadSpeedBytes,
                    totalUpload = p.LastTotalUploadBytes, totalDownload = p.LastTotalDownloadBytes,
                    firstSeen = p.FirstSeenAt, lastSeen = p.LastSeenAt,
                    notifyOnline = p.NotifyWhenOnline, notifyOffline = p.NotifyWhenOffline,
                    history = history.ToArray(), sessions = d.Sessions.OrderByDescending(s => s.OnlineAt).Take(100).Select(s => new { onlineAt = s.OnlineAt, offlineAt = s.OfflineAt }).ToArray() };
            }).ToArray();
            var ids = devices.Select(d => d.id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var id in _history.Keys.Where(k => !ids.Contains(k)).ToArray()) _history.Remove(id);
            _snapshot = JsonSerializer.SerializeToUtf8Bytes(new { devices, refreshedAt = update.RefreshedAt, connection = _connection, internet = update.InternetInfo, mesh = _mesh }, JsonOptions);
        }
    }
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task StartAsync(int httpsPort, int setupPort, bool loopbackOnly = false, bool previewHttp = false)
    {
        if (previewHttp && !loopbackOnly) throw new ArgumentException("预览仅允许回环地址。");
        await StopAsync();
        var addresses = loopbackOnly ? [IPAddress.Loopback] : NetworkPolicy.Addresses();
        _certificate = _store.CreateServerCertificate(addresses);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Limits.MaxRequestBodySize = 8192;
            k.Limits.MaxConcurrentConnections = 64;
            foreach (var ip in addresses)
            {
                k.Listen(ip, httpsPort, o => o.UseHttps(tls => tls.ServerCertificateSelector = (_, _) => GetCertificate(addresses)));
                k.Listen(ip, setupPort);
            }
        });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            var remote = context.Connection.RemoteIpAddress;
            var host = context.Request.Host.Host;
            if (remote is null || !NetworkPolicy.IsPrivate(remote) ||
                !(host == "localhost" || addresses.Any(ip => ip.ToString() == host)))
            { context.Response.StatusCode = 403; return; }
            if (!context.Request.IsHttps && !previewHttp)
            {
                if (context.Request.Path == "/ca.cer")
                { context.Response.ContentType = "application/x-x509-ca-cert"; context.Response.Headers.ContentDisposition = "attachment; filename=LAN-Device-Assistant-CA.cer"; await context.Response.Body.WriteAsync(_store.RootCertificate); }
                else if (context.Request.Path == "/")
                {
                    context.Response.ContentType = "text/html; charset=utf-8";
                    await context.Response.WriteAsync("<!doctype html><html lang=zh-CN><meta name=viewport content='width=device-width,initial-scale=1'><title>局域网设备助手 · 安全连接</title><h1>准备安全连接</h1><p>此页面仅提供公开证书，不提供监控数据。请先与电脑设置中的 SHA-256 指纹核对。</p><pre>" + _store.Fingerprint + "</pre><p><a href='/ca.cer'>下载本机专用 CA 证书</a></p><p>iPhone/iPad：设置 → 已下载描述文件 → 安装；随后在 通用 → 关于本机 → 证书信任设置 中开启完全信任。</p><p>安卓：设置中搜索“安装 CA 证书”，选择下载的证书。部分浏览器或受管理设备不接受用户 CA，请使用支持的浏览器或由管理员提供可信 HTTPS 证书。</p><p>完成后返回电脑提供的 HTTPS 网址，并输入访问码。不要仅跳过浏览器证书警告，跳过警告不能启用 Web Push。</p><p>此证书私钥只保存在本台 Windows 用户的加密存储中。不再使用时，可在手机设置中删除该 CA。</p></html>");
                }
                else context.Response.StatusCode = 404;
                return;
            }
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Items["push-generation"] = _push.Generation;
                var auth = context.Request.Headers.Authorization.ToString();
                if (!auth.StartsWith("Bearer ", StringComparison.Ordinal) || !_store.Authenticate(auth[7..]))
                { context.Response.StatusCode = 401; return; }
                var origin = context.Request.Headers.Origin.ToString();
                if (origin.Length != 0 && origin != context.Request.Scheme + "://" + context.Request.Host)
                { context.Response.StatusCode = 403; return; }
            }
            await next(context);
        });
        app.MapGet("/api/snapshot", () => { lock (_dataGate) return Results.Bytes(_snapshot, "application/json"); });
        app.MapGet("/api/push", () => Results.Json(new { publicKey = _store.PublicKey, enabled = _push.Enabled, subscriptions = _store.Subscriptions.Count, status = _push.Status }));
        app.MapPost("/api/subscription-status", async (HttpContext ctx) =>
        {
            var s = await ctx.Request.ReadFromJsonAsync<WebSubscription>();
            return Results.Json(new { registered = s is not null && _store.Contains(s.Endpoint) });
        });
        app.MapPost("/api/subscribe", async (HttpContext ctx) =>
        {
            var s = await ctx.Request.ReadFromJsonAsync<WebSubscription>();
            if (s is null || !WebPushService.IsValidSubscription(s)) return Results.BadRequest(new { error = "浏览器订阅无效或推送服务暂不支持" });
            if (!_push.Enabled) return Results.BadRequest(new { error = "电脑端已关闭网页通知" });
            try { _store.Add(s, ctx.Request.Headers.Authorization.ToString()[7..]); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
            catch (InvalidOperationException e) { return Results.BadRequest(new { error = e.Message }); }
            return Results.Ok(new { status = "已保存订阅，可发送测试通知" });
        });
        app.MapPost("/api/unsubscribe", async (HttpContext ctx) =>
        {
            var s = await ctx.Request.ReadFromJsonAsync<WebSubscription>();
            if (s is null) return Results.BadRequest();
            _store.Remove(s.Endpoint); return Results.Ok();
        });
        app.MapPost("/api/test", async (HttpContext ctx) =>
        {
            if (!await _testGate.WaitAsync(0)) return Results.StatusCode(429);
            try
            {
                if (DateTimeOffset.UtcNow - _lastTest < TimeSpan.FromSeconds(15)) return Results.StatusCode(429);
                var s = await ctx.Request.ReadFromJsonAsync<WebSubscription>();
                if (!_store.Authenticate(ctx.Request.Headers.Authorization.ToString()[7..])) return Results.Unauthorized();
                var saved = _store.Subscriptions.FirstOrDefault(x => x.Endpoint == s?.Endpoint);
                if (saved is null) return Results.BadRequest(new { error = "请先启用本设备通知" });
                _lastTest = DateTimeOffset.UtcNow;
                var status = await _push.SendAsync(saved, "局域网设备助手", "测试通知：请确认本设备已显示系统通知。", "test", ctx.RequestAborted, (long)ctx.Items["push-generation"]!);
                return Results.Ok(new { status });
            }
            finally { _testGate.Release(); }
        });
        var files = new Dictionary<string, string> { ["/"] = "index.html", ["/app.js"] = "app.js", ["/app.css"] = "app.css", ["/sw.js"] = "sw.js", ["/manifest.webmanifest"] = "manifest.webmanifest", ["/icon.png"] = "icon.png" };
        foreach (var file in files)
        {
            var name = file.Value;
            app.MapGet(file.Key, () => Results.File(Path.Combine(AppContext.BaseDirectory, "Web", name), name.EndsWith(".js") ? "text/javascript" : name.EndsWith(".css") ? "text/css" : name.EndsWith(".png") ? "image/png" : name.EndsWith(".webmanifest") ? "application/manifest+json" : "text/html; charset=utf-8"));
        }
        try
        {
            await app.StartAsync();
            Urls = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.ToArray();
            _app = app;
        }
        catch { await app.DisposeAsync(); _certificate.Dispose(); _certificate = null; throw; }
    }
    public async Task StopAsync()
    {
        _push.Enabled = false;
        var app = _app; _app = null; Urls = [];
        if (app is not null) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); try { await app.StopAsync(timeout.Token); } finally { await app.DisposeAsync(); } }
        lock (_certificateGate)
        {
            _certificate?.Dispose(); _certificate = null;
            foreach (var certificate in _retiredCertificates) certificate.Dispose();
            _retiredCertificates.Clear();
        }
    }
    private X509Certificate2 GetCertificate(IPAddress[] addresses)
    {
        lock (_certificateGate)
        {
            if (_certificate is null) throw new InvalidOperationException("HTTPS 服务未启动");
            if (_certificate.NotAfter.ToUniversalTime() < DateTime.UtcNow.AddDays(7))
            {
                var replacement = _store.CreateServerCertificate(addresses);
                _retiredCertificates.Add(_certificate); _certificate = replacement;
            }
            return _certificate;
        }
    }
    public async ValueTask DisposeAsync() => await StopAsync();
}
