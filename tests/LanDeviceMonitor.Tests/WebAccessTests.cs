using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services.WebAccess;

internal static class WebAccessTests
{
    public static async Task<int> RunAsync()
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
        Check(NetworkPolicy.IsPrivate(IPAddress.Parse("10.77.0.10")), "private LAN");
        Check(!NetworkPolicy.IsPrivate(IPAddress.Parse("8.8.8.8")), "public address rejected");
        Check(WebPushService.IsAllowedEndpoint("https://web.push.apple.com/test"), "Apple push endpoint");
        Check(!WebPushService.IsAllowedEndpoint("https://web.push.apple.com.evil.test/test"), "suffix spoof rejected");
        Check(!WebPushService.IsAllowedEndpoint("http://fcm.googleapis.com/test"), "HTTP push rejected");
        Check(!WebPushService.IsAllowedEndpoint("https://127.0.0.1/test"), "local push SSRF rejected");
        Check(!WebPushService.IsAllowedEndpoint("https://fcm.googleapis.com:8443/test"), "custom push port rejected");
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store = new ProtectedWebStore(folder);
            var token = store.AccessToken;
            Check(token.Length >= 40, "high entropy access code");
            var restored = new ProtectedWebStore(folder);
            Check(restored.AccessToken == token && restored.PublicKey == store.PublicKey, "stable identity after restart");
            Check(!File.ReadAllText(Path.Combine(folder, "web-access.dat")).Contains(token), "credentials not plaintext");
            Check(store.Authenticate(token) && !store.Authenticate("wrong"), "constant time authentication contract");
            store.ResetAccess();
            Check(!store.Authenticate(token) && store.Subscriptions.Count == 0, "revoke access");
            using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var point = ecdh.ExportParameters(false).Q;
            string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+','-').Replace('/','_');
            var subscription = new WebSubscription("https://web.push.apple.com/test", B64([4, ..point.X!, ..point.Y!]), B64(RandomNumberGenerator.GetBytes(16)));
            Check(WebPushService.IsValidSubscription(subscription), "valid P256 subscription");
            Check(!WebPushService.IsValidSubscription(subscription with { P256dh = "bad" }), "bad public key rejected");
            var denied = false;
            try { store.Add(subscription, token); } catch (UnauthorizedAccessException) { denied = true; }
            Check(denied && store.Subscriptions.Count == 0, "revoked in-flight registration denied atomically");
            store.Add(subscription, store.AccessToken);
            using var transport = new FakePushHandler();
            using (var sender = new WebPushService(store, transport))
            {
                sender.Enabled = true;
                Check((await sender.SendAsync(subscription, "设备测试", "secret test payload", "test", default)).Contains("已接受"), "encrypted VAPID request accepted by mock transport");
                Check(transport.Encrypted && transport.Authorized && transport.Ttl == "300", "encrypted body, VAPID and five minute TTL");
                transport.Code = HttpStatusCode.Gone;
                Check((await sender.SendAsync(subscription, "test", "test", "test", default)).Contains("过期") && store.Subscriptions.Count == 0, "expired subscription removed");
                transport.WaitUntilCancelled = true;
                var sending = sender.SendAsync(subscription, "test", "test", "test", default);
                await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
                sender.Enabled = false;
                Check((await sending.WaitAsync(TimeSpan.FromSeconds(3))).Contains("取消"), "disabling cancels in-flight push");
                var oldGeneration = sender.Generation;
                sender.Enabled = true;
                Check((await sender.SendAsync(subscription, "test", "test", "test", default, oldGeneration)).Contains("关闭"), "old request cannot use a new push generation");
            }
            using var push = new WebPushService(store);
            await using var server = new LanWebServer(store, push);
            await server.StartAsync(0, 0, true);
            using var root = new X509Certificate2(store.RootCertificate);
            using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, cert, _, errors) =>
            {
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(root);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                return cert is not null && !errors.HasFlag(System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch) && chain.Build(cert);
            }, UseProxy = false };
            using var http = new HttpClient(handler);
            var url = server.Urls.First(u => u.StartsWith("https://"));
            Check((await http.GetAsync(url + "/api/snapshot")).StatusCode == HttpStatusCode.Unauthorized, "unauthenticated denied");
            http.DefaultRequestHeaders.Authorization = new("Bearer", store.AccessToken);
            Check((await http.GetAsync(url + "/api/snapshot")).IsSuccessStatusCode, "authenticated snapshot");
            server.Publish(new MonitorUpdate([], [], DateTimeOffset.Now));
            using (var current = JsonDocument.Parse(await http.GetStringAsync(url + "/api/snapshot")))
                Check(current.RootElement.GetProperty("connection").GetString() == "已连接", "enabling web during active monitoring reports connected");
            Check((await http.GetStringAsync(url + "/")).Contains("局域网设备助手"), "web assets served");
            var setup = server.Urls.First(u => u.StartsWith("http://"));
            Check((await http.GetAsync(setup + "/api/snapshot")).StatusCode == HttpStatusCode.NotFound, "plaintext cannot read data even with credential");
            Check((await http.GetByteArrayAsync(setup + "/ca.cer")).SequenceEqual(store.RootCertificate), "setup exports public certificate only");
            push.Enabled = true;
            Check((await http.PostAsJsonAsync(url + "/api/subscribe", subscription with { Endpoint = "https://127.0.0.1/secret" })).StatusCode == HttpStatusCode.BadRequest, "SSRF rejected over real API");
            Check((await http.PostAsJsonAsync(url + "/api/subscribe", subscription)).IsSuccessStatusCode, "subscription API persists");
            Check(store.Subscriptions.Count == 1, "one registered browser");
            http.DefaultRequestHeaders.Add("Origin", "https://evil.example");
            Check((await http.GetAsync(url + "/api/snapshot")).StatusCode == HttpStatusCode.Forbidden, "cross-origin request blocked");
            http.DefaultRequestHeaders.Remove("Origin");
            store.ResetAccess();
            Check((await http.GetAsync(url + "/api/snapshot")).StatusCode == HttpStatusCode.Unauthorized, "reset invalidates HTTP credential");
            await server.StopAsync();
            Check(!server.IsRunning, "service disabled");
            var stopped = false;
            try { await http.GetAsync(url + "/"); } catch (HttpRequestException) { stopped = true; }
            Check(stopped && !push.Enabled, "disabled listener cannot be reached and push is off");
        }
        finally { Directory.Delete(folder, true); }
        await TestDesktopDisableAsync();
        return 0;
    }

    private static Task TestDesktopDisableAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var app = new LanDeviceMonitor.App(); app.InitializeComponent();
            app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            var window = new LanDeviceMonitor.MainWindow(new LanDeviceMonitor.Services.NotificationService());
            var context = new TestUiContext(); SynchronizationContext.SetSynchronizationContext(context);
            async Task ExecuteAsync()
            {
                try
                {
                    var repo = new LanDeviceMonitor.Services.DeviceRepository(Path.Combine(folder,"state.json"));
                    await repo.UpdateWebSettingsAsync(true,true,18732,18731);
                    var store = new ProtectedWebStore(folder);
                    using var push = new WebPushService(store);
                    await using var server = new LanWebServer(store,push);
                    await server.StartAsync(0,0,true); push.Enabled=true;
                    void Field(string name,object value) => typeof(LanDeviceMonitor.MainWindow).GetField(name,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(window,value);
                    Field("_repository",repo); Field("_webStore",store);Field("_webPush",push);Field("_lanWebServer",server);
                    ((System.Windows.Controls.TextBox)window.FindName("LanHttpsPortBox")).Text="invalid";
                    ((System.Windows.Controls.TextBox)window.FindName("LanSetupPortBox")).Text="";
                    ((System.Windows.Controls.CheckBox)window.FindName("WebPushCheck")).IsChecked=false;
                    await (Task)typeof(LanDeviceMonitor.MainWindow).GetMethod("ApplyWebPushToggleAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(window,null)!;
                    if(!server.IsRunning || push.Enabled || repo.Settings.WebPushEnabled)throw new Exception("invalid editor prevented disabling only push");
                    Console.WriteLine("PASS actual WPF push toggle ignores invalid port editors and preserves web access");
                    ((System.Windows.Controls.CheckBox)window.FindName("LanAccessCheck")).IsChecked=false;
                    await (Task)typeof(LanDeviceMonitor.MainWindow).GetMethod("ApplyWebAccessAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(window,null)!;
                    if(server.IsRunning || push.Enabled || repo.Settings.LanAccessEnabled) throw new Exception("invalid editor prevented disabling");
                    var reloaded = new LanDeviceMonitor.Services.DeviceRepository(Path.Combine(folder,"state.json"));
                    await reloaded.LoadAsync();
                    if(reloaded.Settings.LanAccessEnabled)throw new Exception("disable not persisted");
                    Console.WriteLine("PASS actual WPF settings disable despite invalid port editors, persisted across reload");
                    completion.TrySetResult();
                }
                catch(Exception e){completion.TrySetException(e);}
                finally { window.PrepareForExit();  Directory.Delete(folder,true); }
            }
            var task = ExecuteAsync();
            while (!task.IsCompleted) context.RunOne();
            task.GetAwaiter().GetResult();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class FakePushHandler : HttpMessageHandler
    {
        public bool Encrypted, Authorized, WaitUntilCancelled;
        public string? Ttl;
        public HttpStatusCode Code = HttpStatusCode.Created;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsByteArrayAsync(ct);
            Encrypted = body.Length > 32 && !Encoding.UTF8.GetString(body).Contains("secret test payload");
            Authorized = request.Headers.Contains("Authorization");
            Ttl = request.Headers.TryGetValues("TTL", out var values) ? values.First() : null;
            if (WaitUntilCancelled) { Started.TrySetResult(); await Task.Delay(Timeout.Infinite,ct); }
            return new HttpResponseMessage(Code);
        }
    }

    private sealed class TestUiContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.BlockingCollection<Action> _queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Add(() => callback(state));
        public void RunOne() { if (_queue.TryTake(out var work, 1000)) work(); }
    }

    public static async Task<int> PreviewAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lan-web-preview-" + Guid.NewGuid().ToString("N"));
        var store = new ProtectedWebStore(folder);
        using var push = new WebPushService(store);
        await using var server = new LanWebServer(store, push);
        await server.StartAsync(0, 0, loopbackOnly: true, previewHttp: true);
        var address = server.Urls.First(u => u.StartsWith("http://"));
        Console.WriteLine("PREVIEW_URL=" + address + "/#access=" + store.AccessToken);
        // Synthetic fixtures, never the user's router or settings; no outbound pushes.
        var names = new[] { "客厅电视", "我的手机", "工作电脑", "书房音箱", "卧室空调" };
        var devices = names.Select((name,i) => new DeviceViewData {
            Profile = new DeviceProfile { MacAddress=$"02:00:00:00:00:0{i}", CustomName=name, HasObservedState=true, LastKnownOnline=i!=4,
                LastIpAddress=$"192.168.31.{101+i}", LastConnectionType=i%3, LastUploadSpeedBytes=i==4?0:1234*(i+1), LastDownloadSpeedBytes=i==4?0:234567*(i+1),
                LastTotalUploadBytes=67891234, LastTotalDownloadBytes=3456789123, NotifyWhenOnline=true, NotifyWhenOffline=true,
                HasFreshUploadGraphSample=true, HasFreshDownloadGraphSample=true },
            Identity=new DeviceIdentity("智能设备","",""),
            Sessions=[new DeviceSession { OnlineAt=DateTimeOffset.Now.AddHours(-2), OfflineAt=i==4?DateTimeOffset.Now.AddMinutes(-5):null }]
        }).ToArray();
        server.SetConnection("界面预览 · 示例数据（非实时设备）");
        server.SetMesh([new MeshNodeInfo("客厅主路由","10.77.0.1","AX3000T","网线接入",4,true),new MeshNodeInfo("书房节点","10.77.0.2","AX3000T","5GHz",1,false)]);
        var internet = new RouterInternetInfo(true,"DHCP","203.0.113.5","255.255.255.0","203.0.113.1","192.0.2.53","Native","2001:db8::5","fe80::1","2001:db8:1::1","2001:db8:1::/64","2001:db8::53");
        for(var i=0;i<80;i++)
        {
            foreach(var d in devices) { d.Profile.LastUploadGraphSpeedBytes=(long)(30000+Math.Sin(i*.3)*15000);d.Profile.LastDownloadGraphSpeedBytes=(long)(400000+Math.Cos(i*.25)*220000); }
            server.Publish(new MonitorUpdate(devices,[],DateTimeOffset.Now.AddSeconds(i-79),internet));
        }
        var end = DateTimeOffset.UtcNow.AddHours(2);
        while (DateTimeOffset.UtcNow < end)
        {
            await Task.Delay(2000);
            server.Publish(new MonitorUpdate(devices,[],DateTimeOffset.Now,internet));
        }
        return 0;
    }
}
