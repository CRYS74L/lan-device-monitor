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
    private readonly Dictionary<string, TrafficHistoryBuffer> _history = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WebDeviceSnapshot> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (IReadOnlyList<DeviceSession> Source, DeviceSession[] Sorted)> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private object _preferences = new { refreshIntervalSeconds = 1 };
    private readonly Func<string, Task<IReadOnlyList<DeviceSession>>>? _sessionReader;
    public LanWebServer(Func<string, Task<IReadOnlyList<DeviceSession>>>? sessionReader = null) => _sessionReader = sessionReader;
    private IReadOnlyList<MeshNodeInfo> _mesh = [];
    private DateTimeOffset? _meshUpdatedAt;
    private string _connection = "等待监控数据";
    public bool IsRunning => _app is not null;
    public string[] Urls { get; private set; } = [];
    public void SeedHistory(IReadOnlyDictionary<string, IReadOnlyList<TrafficSample>> histories)
    {
        lock (_dataGate)
        {
            foreach (var (id, samples) in histories)
            {
                var current = _history.TryGetValue(id, out var buffer) ? buffer.Snapshot(DateTimeOffset.Now) : [];
                var merged = new TrafficHistoryBuffer();
                // A monitor update may arrive while the UI supplies its retained
                // ten-minute buffer. Merge chronologically and prefer newer evidence.
                foreach (var sample in samples.Concat(current).OrderBy(p => p.Timestamp)) merged.Append(sample);
                _history[id] = merged;
            }
        }
    }
    public void SetPreferences(AppSettings settings)
    {
        lock (_dataGate) _preferences = new {
            refreshIntervalSeconds = settings.RefreshIntervalSeconds,
            routerAddress = settings.RouterAddress,
            desktopTheme = settings.ThemeMode, startWithWindows = settings.StartWithWindows,
            closeToTray = settings.CloseToTray, doNotDisturb = settings.DoNotDisturbEnabled,
            quietStart = NotificationSchedule.FormatTime(settings.DoNotDisturbStartMinutes),
            quietEnd = NotificationSchedule.FormatTime(settings.DoNotDisturbEndMinutes),
            ipv6Notification = settings.NotifyWhenIpv6NotNative,
            emergencyHotKeyEnabled = settings.EmergencyHotKeyEnabled,
            emergencyHotKey = EmergencyHotKeyGesture.TryCreate(settings.EmergencyHotKeyModifiers, settings.EmergencyHotKeyVirtualKey, out var gesture) ? gesture.DisplayText : "未设置",
            version = MainWindow.GetDisplayVersion()
        };
    }
    public void SetConnection(string state)
    {
        lock (_dataGate)
        {
            _connection = state;
            var data = JsonNode.Parse(_snapshot)!; data["connection"] = state;
            _snapshot = JsonSerializer.SerializeToUtf8Bytes(data);
        }
    }
    public void SetMesh(IReadOnlyList<MeshNodeInfo> mesh) { lock (_dataGate) { _mesh = mesh; _meshUpdatedAt = DateTimeOffset.Now; } }
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
                if (update.RefreshedAt > DateTimeOffset.MinValue && (p.HasFreshUploadGraphSample || p.HasFreshDownloadGraphSample))
                    history.Append(new TrafficSample(update.RefreshedAt, MainWindow.GetGraphSpeed(p.LastUploadGraphSpeedBytes, p.LastUploadSpeedBytes), MainWindow.GetGraphSpeed(p.LastDownloadGraphSpeedBytes, p.LastDownloadSpeedBytes)) {
                        HasUploadSample = p.HasFreshUploadGraphSample, HasDownloadSample = p.HasFreshDownloadGraphSample });
                if (!_sessions.TryGetValue(p.MacAddress, out var cached) || !ReferenceEquals(cached.Source, d.Sessions))
                    _sessions[p.MacAddress] = (d.Sessions, d.Sessions.OrderByDescending(s => s.OnlineAt).Select(s => new DeviceSession { MacAddress = s.MacAddress, OnlineAt = s.OnlineAt, OfflineAt = s.OfflineAt }).ToArray());
                var device = WebDeviceSnapshot.From(d);
                _devices[p.MacAddress] = device;
                return device;            }).ToArray();
            var ids = devices.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var id in _history.Keys.Where(k => !ids.Contains(k)).ToArray()) { _history.Remove(id); _devices.Remove(id); _sessions.Remove(id); }
            _snapshot = JsonSerializer.SerializeToUtf8Bytes(new { devices, refreshedAt = update.RefreshedAt, connection = _connection, internet = update.InternetInfo, mesh = _mesh, meshUpdatedAt = _meshUpdatedAt, preferences = _preferences }, JsonOptions);
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
            app.MapGet("/api/device/{id}", GetDeviceAsync);
            var files = new Dictionary<string, string> { ["/"] = "index.html", ["/app.js"] = "app.js", ["/ui.js"] = "ui.js", ["/chart.js"] = "chart.js", ["/app.css"] = "app.css", ["/icon.png"] = "icons/network-v195-512.png", ["/apple-touch-icon.png"] = "icons/network-v195-180.png", ["/manifest.webmanifest"] = "manifest.webmanifest" };
            foreach (var size in new[] { 32, 152, 167, 180, 192, 512, 1024 }) files[$"/icons/network-v195-{size}.png"] = $"icons/network-v195-{size}.png";
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
                return;
            }
            catch (IOException e) when (IsAddressInUse(e) && attempt < 29)
            { await app.DisposeAsync(); }
            catch { await app.DisposeAsync(); throw; }
        }
    }
    private async Task<IResult> GetDeviceAsync(string id, int? offset, string? before)
    {
        WebDeviceSnapshot device;
        IReadOnlyList<TrafficSample> history;
        IReadOnlyList<DeviceSession> sessions;
        var now = DateTimeOffset.Now;
        lock (_dataGate)
        {
            if (!_devices.TryGetValue(id, out var found)) return Results.NotFound();
            device = found;
            history = _history[id].Snapshot(now);
            sessions = _sessions[id].Sorted;
        }
        if (_sessionReader is not null) sessions = await _sessionReader(id);
        var pageOffset = Math.Max(0, offset ?? 0);
        var subset = sessions.Skip(pageOffset);
        if (before is not null)
        {
            if (!DateTimeOffset.TryParse(before, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var cursor)) return Results.BadRequest();
            subset = sessions.Where(s => s.OnlineAt < cursor);
        }
        var page = subset.Take(31).ToArray();
        return Results.Json(new { device = device with { SessionCount = sessions.Count }, serverTime = now,
            history = history.Select(p => new { at = p.Timestamp, upload = p.HasUploadSample ? (long?)p.UploadBytesPerSecond : null, download = p.HasDownloadSample ? (long?)p.DownloadBytesPerSecond : null }),
            sessions = page.Take(30).Select(p => new { onlineAt = p.OnlineAt, offlineAt = p.OfflineAt }),
            totalSessions = sessions.Count, offset = pageOffset, hasMore = page.Length > 30,
            nextBefore = page.Length > 0 ? page[Math.Min(29, page.Length - 1)].OnlineAt.ToString("O") : null
        }, JsonOptions);
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
