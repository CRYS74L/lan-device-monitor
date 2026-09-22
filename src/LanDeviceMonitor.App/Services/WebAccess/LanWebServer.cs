using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using LanDeviceMonitor.ViewModels;
using LanDeviceMonitor.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LanDeviceMonitor.Services.WebAccess;

internal sealed class LanWebServer : IAsyncDisposable
{
    private WebApplication? _app;
    private readonly object _dataGate = new();
    private byte[] _snapshot = "{\"devices\":[],\"refreshedAt\":null,\"connection\":\"等待监控数据\",\"mesh\":[]}"u8.ToArray();
    private readonly Dictionary<string, Queue<object>> _history = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<MeshNodeInfo> _mesh = [];
    private string _connection = "等待监控数据";
    public bool IsRunning => _app is not null;
    public string[] Urls { get; private set; } = [];
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
    public async Task StartAsync(int preferredPort = 18731, bool loopbackOnly = false)
    {
        await StopAsync();
        var addresses = loopbackOnly ? [IPAddress.Loopback] : NetworkPolicy.Addresses();
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var port = preferredPort == 0 ? 0 : 1024 + (preferredPort - 1024 + attempt) % (65536 - 1024);
            if (port == 0 && !loopbackOnly) throw new ArgumentException("动态端口仅用于本机测试。");
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k =>
            {
                k.Limits.MaxRequestBodySize = 1024;
                k.Limits.MaxConcurrentConnections = 64;
                foreach (var ip in addresses) k.Listen(ip, port);
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
                var origin = context.Request.Headers.Origin.ToString();
                if (origin.Length != 0 && origin != "http://" + context.Request.Host)
                { context.Response.StatusCode = 403; return; }
                if (context.Request.Method is not ("GET" or "HEAD"))
                { context.Response.StatusCode = 405; return; }
                await next(context);
            });
            app.MapGet("/api/snapshot", () => { lock (_dataGate) return Results.Bytes(_snapshot, "application/json"); });
            var files = new Dictionary<string, string> { ["/"] = "index.html", ["/app.js"] = "app.js", ["/app.css"] = "app.css", ["/icon.png"] = "icon.png" };
            foreach (var file in files)
            {
                var name = file.Value;
                app.MapGet(file.Key, () => Results.File(Path.Combine(AppContext.BaseDirectory, "Web", name), name.EndsWith(".js") ? "text/javascript" : name.EndsWith(".css") ? "text/css" : name.EndsWith(".png") ? "image/png" : "text/html; charset=utf-8"));
            }
            try
            {
                await app.StartAsync();
                Urls = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.ToArray();
                _app = app;
                return;
            }
            catch (IOException e) when (IsAddressInUse(e) && attempt < 29)
            { await app.DisposeAsync(); }
            catch { await app.DisposeAsync(); throw; }
        }
    }
    private static bool IsAddressInUse(Exception e) => e is AddressInUseException || e.InnerException is not null && IsAddressInUse(e.InnerException);
    public async Task StopAsync()
    {
        var app = _app; _app = null; Urls = [];
        if (app is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await app.StopAsync(timeout.Token); } finally { await app.DisposeAsync(); }
        }
    }
    public async ValueTask DisposeAsync() => await StopAsync();
}