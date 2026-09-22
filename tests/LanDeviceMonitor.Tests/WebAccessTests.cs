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
        foreach(var removed in new[]{"/api/push","/api/subscribe","/ca.cer","/sw.js","/manifest.webmanifest"})
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
                    foreach(var removed in new[]{"WebPushCheck","LanHttpsPortBox","LanSetupPortBox","LanAccessCodeBox","WebCertificateText"})
                        if(window.FindName(removed) is not null)throw new Exception("removed control still exists: "+removed);
                    var toggle = (System.Windows.Controls.CheckBox)window.FindName("LanAccessCheck"); toggle.IsChecked=true;
                    var starting = (Task)type.GetMethod("InitializeWebAccessAsync",flags)!.Invoke(window,null)!;
                    if(!starting.IsCompleted && toggle.IsEnabled)throw new Exception("startup allows conflicting toggle");
                    await starting;
                    var server = (LanWebServer)type.GetField("_lanWebServer",flags)!.GetValue(window)!;
                    if(!server.IsRunning)throw new Exception("startup did not start server");
                    var link = ((System.Windows.Controls.TextBox)window.FindName("LanAddressBox")).Text;
                    if(!link.StartsWith("http://") || link.Contains('#') || link.Contains('?'))throw new Exception("not a direct link");
                    toggle.IsChecked=false;
                    await (Task)type.GetMethod("ApplyWebAccessAsync",flags)!.Invoke(window,null)!;
                    if(server.IsRunning || repo.Settings.LanAccessEnabled)throw new Exception("desktop toggle did not stop service");
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
    public static async Task<int> PreviewAsync()
    {
        await using var server = new LanWebServer();
        await server.StartAsync(0,true);
        Console.WriteLine("PREVIEW_URL="+server.Urls.Single()+"/");
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
