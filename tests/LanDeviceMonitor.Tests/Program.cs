using LanDeviceMonitor.Services;
using LanDeviceMonitor.ViewModels;

var failures = new List<string>();

Check(
    DeviceIdentityService.Identify("", "yeelink-light-lamp27_mibt053F", 1).TypeName == "灯",
    "Yeelight 型号应识别为灯");
Check(
    DeviceIdentityService.Identify("", "cgllc-airm-cgd1st_mibtEC5B", 1).TypeName == "空气检测仪",
    "CGLLC 空气设备应识别为空气检测仪");
Check(
    DeviceIdentityService.Identify("", "MiAiSoundbox-L15A", 1).SuggestedName == "小爱音箱",
    "小米音箱应使用中文建议名称");
Check(
    DeviceIdentityService.Identify("", "Redmi-Note-11T-Pro", 2).TypeName == "手机",
    "Redmi Note 应识别为手机");
Check(DeviceItemViewModel.FormatSpeed(1024) == "1.0 KB/s", "速度单位换算不正确");
Check(DeviceItemViewModel.FormatBytes(1024L * 1024 * 1024) == "1.00 GB", "流量单位换算不正确");
Check(NotificationSchedule.TryParseTime("22:30", out var startMinutes) &&
      startMinutes == 1350,
    "免打扰开始时间解析不正确");
Check(!NotificationSchedule.TryParseTime("25:00", out _),
    "无效的免打扰时间不应被接受");
Check(NotificationSchedule.IsMuted(
        new DateTimeOffset(2026, 7, 24, 23, 0, 0, TimeSpan.FromHours(8)),
        true,
        22 * 60,
        8 * 60),
    "跨零点免打扰时段的夜间部分判断不正确");
Check(NotificationSchedule.IsMuted(
        new DateTimeOffset(2026, 7, 24, 7, 30, 0, TimeSpan.FromHours(8)),
        true,
        22 * 60,
        8 * 60),
    "跨零点免打扰时段的清晨部分判断不正确");
Check(!NotificationSchedule.IsMuted(
        new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.FromHours(8)),
        true,
        22 * 60,
        8 * 60),
    "免打扰时段以外不应静音通知");

if (args.Contains("--router", StringComparer.OrdinalIgnoreCase))
{
    var credentialService = new RouterCredentialService();
    var password = credentialService.ReadPassword("10.77.0.1");
    Check(!string.IsNullOrWhiteSpace(password), "Windows 凭据管理器中没有路由器密码");
    if (!string.IsNullOrWhiteSpace(password))
    {
        using var client = new XiaomiRouterClient();
        client.Configure("10.77.0.1", password);
        var snapshots = await client.GetDeviceSnapshotsAsync();
        Check(snapshots.Count > 0, "路由器没有返回设备快照");
        Check(
            snapshots.All(snapshot =>
                snapshot.UploadSpeedBytes >= 0 &&
                snapshot.DownloadSpeedBytes >= 0 &&
                snapshot.TotalUploadBytes >= 0 &&
                snapshot.TotalDownloadBytes >= 0),
            "设备流量字段包含无效值");
        var internetInfo = await client.GetInternetInfoAsync();
        Check(
            !string.IsNullOrWhiteSpace(internetInfo.Ipv4ConnectionType) &&
            !string.IsNullOrWhiteSpace(internetInfo.Ipv4Address),
            "路由器没有返回有效的 IPv4 上网信息");
        Check(
            !string.IsNullOrWhiteSpace(internetInfo.Ipv6ConnectionType),
            "路由器没有返回 IPv6 连接模式");
        var meshNodes = await client.GetMeshNodesAsync();
        Check(meshNodes.Count > 0, "路由器没有返回 Mesh 节点");
        Check(
            meshNodes.Any(node => node.IsGateway) &&
            meshNodes.All(node => node.ConnectedDeviceCount >= 0),
            "Mesh 节点或设备数量无效");
        Console.WriteLine(
            $"路由器联调：已读取 {snapshots.Count} 台在线设备和 {meshNodes.Count} 个 Mesh 节点。");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"自检失败：{failures.Count} 项");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    return 1;
}

Console.WriteLine("自检通过：设备识别、单位换算与通知时段共 11 项；路由器模式另验证上网与 Mesh 信息。");
return 0;

void Check(bool condition, string message)
{
    if (!condition)
    {
        failures.Add(message);
    }
}
