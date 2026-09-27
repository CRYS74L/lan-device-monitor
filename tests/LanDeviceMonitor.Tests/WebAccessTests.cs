using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;
using LanDeviceMonitor.Services.WebAccess;

internal static class WebAccessTests
{
    public static async Task<int> RunAsync()
    {
        void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
        Check(NetworkPolicy.IsPrivate(IPAddress.Parse("10.77.0.10")) && !NetworkPolicy.IsPrivate(IPAddress.Parse("8.8.8.8")),"LAN address policy");
        Check(LanFirewallAccess.TryParsePort([LanFirewallAccess.HelperArgument,"18731"],out var parsed) && parsed==18731,"firewall helper accepts only a bounded port");
        Check(!LanFirewallAccess.TryParsePort([LanFirewallAccess.HelperArgument,"80"],out _) && !LanFirewallAccess.TryParsePort([LanFirewallAccess.HelperArgument,"18731","extra"],out _),"firewall helper rejects privileged ports and extra arguments");
        Check(LanFirewallAccess.RemoteScope=="LocalSubnet" && LanFirewallAccess.Profiles==7,"firewall scoped to local subnet across Windows network profiles");
        Check(!LanFirewallAccess.HasRule(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"),"not-installed.exe"),18731),"missing Windows rule is a normal setup condition");
        LanFirewallAccess.ValidatePolicy(0);
        foreach(var blockedState in new[]{1,2})
        {
            bool rejected=false;try{LanFirewallAccess.ValidatePolicy(blockedState);}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"effective policy override rejected: "+blockedState);
        }
        Check(LanFirewallAccess.RuleName(@"C:\Apps\tool.exe")==LanFirewallAccess.RuleName(@"c:\apps\TOOL.exe") && LanFirewallAccess.RuleName(@"C:\Apps\tool.exe")!=LanFirewallAccess.RuleName(@"D:\Apps\tool.exe"),"firewall rule stable per executable location");
        using var occupied = new TcpListener(IPAddress.Loopback,0); occupied.Start();
        var port=((IPEndPoint)occupied.LocalEndpoint).Port;
        await using var server = new LanWebServer();
        await server.StartAsync(port,true);
        var url=server.Urls.Single();
        Check(new Uri(url).Port!=port,"occupied port automatically avoided");
        using var http = new HttpClient(new HttpClientHandler {UseProxy=false});
        Check((await http.GetAsync(url+"/api/snapshot")).IsSuccessStatusCode,"open link reads data without credentials or certificate");
        var html=await http.GetStringAsync(url+"/");
        Check(!html.Contains("访问码") && !html.Contains("通知与连接") && !html.Contains("证书"),"no technical setup or mobile notifications in webpage");
        foreach(var removed in new[]{"/api/push","/api/subscribe","/ca.cer","/sw.js"})
            Check((await http.GetAsync(url+removed)).StatusCode==HttpStatusCode.NotFound,"removed route "+removed);
        Check((await http.PostAsync(url+"/api/snapshot",new StringContent("{}"))).StatusCode==HttpStatusCode.MethodNotAllowed,"web access is read-only");
        http.DefaultRequestHeaders.Host="evil.example";
        Check((await http.GetAsync(url+"/api/snapshot")).StatusCode==HttpStatusCode.Forbidden,"DNS rebinding host rejected");
        http.DefaultRequestHeaders.Host=null;
        http.DefaultRequestHeaders.Add("Origin","http://evil.example");
        Check((await http.GetAsync(url+"/api/snapshot")).StatusCode==HttpStatusCode.Forbidden,"cross origin rejected");
        http.DefaultRequestHeaders.Remove("Origin");
        server.Publish(new MonitorUpdate([],[],DateTimeOffset.Now));
        using(var doc=JsonDocument.Parse(await http.GetStringAsync(url+"/api/snapshot")))
            Check(doc.RootElement.GetProperty("connection").GetString()=="已连接","active monitoring status correct");
        var now=DateTimeOffset.Now;
        var profile=new DeviceProfile {MacAddress="02:00:00:00:00:11",LastOriginalName="original-device",LastRouterName="router-device",CustomName="我的设备",LastKnownOnline=true,HasObservedState=true,HasFreshUploadGraphSample=true,HasFreshDownloadGraphSample=true,LastUploadGraphSpeedBytes=100,LastDownloadGraphSpeedBytes=200,FirstSeenAt=now.AddDays(-30),LastSeenAt=now};
        var sessions=Enumerable.Range(0,135).Select(i=>new DeviceSession{MacAddress=profile.MacAddress,OnlineAt=now.AddHours(-i-1),OfflineAt=i==0?null:now.AddHours(-i).AddMinutes(-5)}).ToArray();
        var device=new DeviceViewData {Profile=profile,Identity=new DeviceIdentity("手机","\uE8EA","手机"),Sessions=sessions};
        server.SetPreferences(new AppSettings{RefreshIntervalSeconds=5});
        server.Publish(new MonitorUpdate([device],[],now));
        server.SeedHistory(new Dictionary<string,IReadOnlyList<TrafficSample>> { [profile.MacAddress]=[new TrafficSample(now.AddMinutes(-2),777,888)] });
        using(var seeded=JsonDocument.Parse(await http.GetStringAsync(url+"/api/device/"+Uri.EscapeDataString(profile.MacAddress))))
            Check(seeded.RootElement.GetProperty("history").EnumerateArray().Any(p=>p.GetProperty("upload").GetInt64()==777),"desktop retained traffic history backfills an already running web monitor");
        // A fresh MAC isolates chronological history from the preceding live-update merge test.
        profile.MacAddress="02:00:00:00:00:12";
        foreach(var session in sessions)session.MacAddress=profile.MacAddress;
        for(int i=650;i>=0;i--)server.Publish(new MonitorUpdate([device],[],now.AddSeconds(-i)));
        using(var snapshot=JsonDocument.Parse(await http.GetStringAsync(url+"/api/snapshot")))
        {
            var d=snapshot.RootElement.GetProperty("devices")[0];
            Check(d.GetProperty("originalName").GetString()=="original-device" && d.GetProperty("sessionCount").GetInt32()==135 && !d.TryGetProperty("history",out _),"complete device metadata without duplicating every chart in overview");
            Check(snapshot.RootElement.GetProperty("preferences").GetProperty("refreshIntervalSeconds").GetInt32()==5,"desktop preferences shared without credentials");
        }
        using(var detail=JsonDocument.Parse(await http.GetStringAsync(url+"/api/device/"+Uri.EscapeDataString(profile.MacAddress))))
        {
            var d=detail.RootElement;Check(d.GetProperty("history").GetArrayLength()>=599,"ten minute real sample history retained");
            Check(d.GetProperty("sessions").GetArrayLength()==30 && d.GetProperty("totalSessions").GetInt32()==135 && d.GetProperty("hasMore").GetBoolean(),"history paginates beyond the previous 100 session limit");
            var cursor=d.GetProperty("nextBefore").GetString()!;
            using(var next=JsonDocument.Parse(await http.GetStringAsync(url+"/api/device/"+Uri.EscapeDataString(profile.MacAddress)+"?before="+Uri.EscapeDataString(cursor))))
                Check(next.RootElement.GetProperty("sessions")[0].GetProperty("onlineAt").GetDateTimeOffset()<DateTimeOffset.Parse(cursor),"history cursor excludes previous page boundary");
        }
        Check((await http.GetAsync(url+"/api/device/missing")).StatusCode==HttpStatusCode.NotFound,"missing device has explicit 404");
        Check((await http.GetAsync(url+"/manifest.webmanifest")).IsSuccessStatusCode,"home screen manifest available without push");
        foreach(var size in new[]{32,152,167,180,192,512,1024})
        {
            var png=await http.GetByteArrayAsync(url+$"/icons/network-v195-{size}.png");
            Check(png[25]==2,$"icon {size}px PNG has RGB pixels without an alpha channel");
            using var stream=new MemoryStream(png);
            var decoder=new System.Windows.Media.Imaging.PngBitmapDecoder(stream,System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var bitmap=new System.Windows.Media.Imaging.FormatConvertedBitmap(decoder.Frames[0],System.Windows.Media.PixelFormats.Bgra32,null,0);
            var pixels=new byte[size*size*4];bitmap.CopyPixels(pixels,size*4,0);
            Check(bitmap.PixelWidth==size && bitmap.PixelHeight==size && Enumerable.Range(0,size*size).All(i=>pixels[i*4+3]==255),$"icon {size}px is square and fully opaque");
        }
        await server.StopAsync();
        bool stopped=false;try{await http.GetAsync(url+"/");}catch(HttpRequestException){stopped=true;}
        Check(stopped && !server.IsRunning,"switch off actually stops listener");
        var folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var path=Path.Combine(folder,"state.json");
            await File.WriteAllTextAsync(path,"{\"settings\":{\"lanAccessEnabled\":false,\"webPushEnabled\":true,\"lanHttpsPort\":18732,\"lanSetupPort\":18731}}");
            var repo=new DeviceRepository(path); await repo.LoadAsync();await repo.UpdateWebSettingsAsync(true);
            var reload=new DeviceRepository(path);await reload.LoadAsync();Check(reload.Settings.LanAccessEnabled,"LAN toggle persists and old configuration migrates");
            var saved=await File.ReadAllTextAsync(path);Check(!saved.Contains("webPushEnabled")&&!saved.Contains("lanHttpsPort"),"obsolete settings removed on save");
            await reload.UpdateWebSettingsAsync(false);
            var persisted = new PersistedState { Sessions=sessions.ToList(), Devices=new(StringComparer.OrdinalIgnoreCase) { [profile.MacAddress]=profile } };
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(persisted,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var realRepo=new DeviceRepository(path);await realRepo.LoadAsync();
            var desktopData=await realRepo.GetDevicesAsync();
            Check(desktopData.Single().Sessions.Count==100,"desktop recent-history limit remains unchanged");
            await using var backedServer=new LanWebServer(realRepo.GetAllDeviceSessionsAsync);
            backedServer.Publish(new MonitorUpdate(desktopData,[],now));await backedServer.StartAsync(0,true);
            using var complete=JsonDocument.Parse(await http.GetStringAsync(backedServer.Urls.Single()+"/api/device/"+Uri.EscapeDataString(profile.MacAddress)+"?offset=120"));
            Check(complete.RootElement.GetProperty("totalSessions").GetInt32()==135 && complete.RootElement.GetProperty("sessions").GetArrayLength()==15,"real persisted repository exposes records beyond desktop's 100-session window");
        }
        finally {Directory.Delete(folder,true);}
        Check(typeof(AppSettings).GetProperty("NotifyWhenIpv6NotNative") is not null,"Windows notification setting preserved");
        await DesktopSettingsAsync();
        return 0;
    }
    private static Task DesktopSettingsAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            var app = new LanDeviceMonitor.App(); app.InitializeComponent(); app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            var window = new LanDeviceMonitor.MainWindow(new NotificationService());
            var context = new TestUiContext(); SynchronizationContext.SetSynchronizationContext(context);
            async Task Run()
            {
                try
                {
                    var repo = new DeviceRepository(Path.Combine(folder,"state.json")); await repo.UpdateWebSettingsAsync(true);
                    var type = window.GetType(); const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    type.GetField("_repository",flags)!.SetValue(window,repo);
                    var firewall = new TestFirewallAccess();
                    type.GetField("_lanFirewallAccess",flags)!.SetValue(window,firewall);
                    foreach(var removed in new[]{"WebPushCheck","LanHttpsPortBox","LanSetupPortBox","LanAccessCodeBox","WebCertificateText"})
                        if(window.FindName(removed) is not null)throw new Exception("removed control still exists: "+removed);
                    var toggle = (System.Windows.Controls.CheckBox)window.FindName("LanAccessCheck"); toggle.IsChecked=true;
                    var starting = (Task)type.GetMethod("InitializeWebAccessAsync",flags)!.Invoke(window,null)!;
                    if(!starting.IsCompleted && toggle.IsEnabled)throw new Exception("startup allows conflicting toggle");
                    await starting;
                    var server = (LanWebServer)type.GetField("_lanWebServer",flags)!.GetValue(window)!;
                    if(!server.IsRunning)throw new Exception("startup did not start server");
                    if(server.Urls.Any(u=>new Uri(u).Host!="127.0.0.1") && firewall.Port!=new Uri(server.Urls[0]).Port)throw new Exception("actual server port not passed to firewall setup");
                    var link = ((System.Windows.Controls.TextBox)window.FindName("LanAddressBox")).Text;
                    if(!link.StartsWith("http://") || link.Contains('#') || link.Contains('?'))throw new Exception("not a direct link");
                    toggle.IsChecked=false;
                    await (Task)type.GetMethod("ApplyWebAccessAsync",flags)!.Invoke(window,null)!;
                    if(server.IsRunning || repo.Settings.LanAccessEnabled)throw new Exception("desktop toggle did not stop service");
                    if(firewall.Port!=0)
                    {
                        firewall.Fail=true;toggle.IsChecked=true;
                        await (Task)type.GetMethod("ApplyWebAccessAsync",flags)!.Invoke(window,null)!;
                        if(server.IsRunning || repo.Settings.LanAccessEnabled || toggle.IsChecked==true)throw new Exception("failed permission incorrectly reported as enabled");
                        Console.WriteLine("PASS failed Windows authorization stops listener and resets saved toggle");
                    }
                    Console.WriteLine("PASS actual desktop simple controls, startup serialized, direct link and shutdown");
                }
                finally { window.PrepareForExit(); Directory.Delete(folder,true); }
            }
            try { var task=Run(); while(!task.IsCompleted)context.RunOne(); task.GetAwaiter().GetResult();done.TrySetResult(); }
            catch(Exception e){done.TrySetException(e);}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();return done.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }
    private sealed class TestUiContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.BlockingCollection<Action> _queue = new();
        public override void Post(SendOrPostCallback callback,object? state)=>_queue.Add(()=>callback(state));
        public void RunOne(){if(_queue.TryTake(out var action,1000))action();}
    }
    private sealed class TestFirewallAccess : ILanFirewallAccess
    {
        public int Port;
        public bool Fail;
        public Task EnsureAsync(int port) {Port=port;return Fail?Task.FromException(new InvalidOperationException("测试：授权被拒绝")):Task.CompletedTask;}
    }
    public static async Task<int> PreviewAsync()
    {
        await using var server = new LanWebServer();
        await server.StartAsync(0,true);
        Console.WriteLine("PREVIEW_URL="+server.Urls.Single()+"/");
        // Synthetic fixtures, never the user's router or settings; no outbound pushes.
        var names = new[] { "客厅电视", "我的手机", "工作电脑", "书房音箱", "卧室空调" };
        var types = new[] { "电视", "手机", "笔记本电脑", "智能音箱", "空调" };
        var devices = names.Select((name,i) => new DeviceViewData {
            Profile = new DeviceProfile { MacAddress=$"02:00:00:00:00:0{i}", CustomName=name, HasObservedState=true, LastKnownOnline=i!=4, LastOriginalName=$"Device-{i+1}", LastRouterName=name, FirstSeenAt=DateTimeOffset.Now.AddDays(-20),LastSeenAt=DateTimeOffset.Now.AddMinutes(i==4?-5:0),
                LastIpAddress=$"192.168.31.{101+i}", LastConnectionType=i%3, LastUploadSpeedBytes=i==4?0:1234*(i+1), LastDownloadSpeedBytes=i==4?0:234567*(i+1),
                LastTotalUploadBytes=67891234, LastTotalDownloadBytes=3456789123, NotifyWhenOnline=true, NotifyWhenOffline=true,
                HasFreshUploadGraphSample=true, HasFreshDownloadGraphSample=true },
            Identity=DeviceIdentityService.AvailableTypes.First(t=>t.TypeName==types[i]),
            Sessions=Enumerable.Range(0,i==1?95:3).Select(j=>new DeviceSession { OnlineAt=DateTimeOffset.Now.AddHours(-2-j*4), OfflineAt=j==0?(i==4?DateTimeOffset.Now.AddMinutes(-5):null):DateTimeOffset.Now.AddHours(-1-j*4) }).ToArray()
        }).ToArray();
        server.SetConnection("界面预览 · 示例数据（非实时设备）");
        server.SetPreferences(new AppSettings());
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
