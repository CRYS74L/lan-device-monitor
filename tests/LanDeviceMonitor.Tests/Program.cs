using LanDeviceMonitor;
using LanDeviceMonitor.Controls;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;
using LanDeviceMonitor.ViewModels;
using System.Diagnostics;
using System.Reflection;

if (args.Contains("--startup-smoke", StringComparer.OrdinalIgnoreCase))
{
    return RunStartupSmoke();
}

if (args.Contains("--hotkey-registration-smoke", StringComparer.OrdinalIgnoreCase))
{
    return RunHotKeyRegistrationSmoke();
}

if (args.Contains("--traffic-diagnostic", StringComparer.OrdinalIgnoreCase))
{
    return await RunTrafficDiagnosticAsync();
}

var failures = new List<string>();
var checksRun = 0;

Check(
    await MonitoringLoopRunsOffUiContextAsync(),
    "监控轮询应在 UI 同步上下文之外运行，并在慢轮询完成后保留完整刷新间隔");

Check(
    EmergencyHotKeyService.DisplayShortcut ==
        "Ctrl + Alt + Shift + Win + F1" &&
    EmergencyHotKeyService.VirtualKey == 0x70 &&
    EmergencyHotKeyService.HasAllFourModifiers &&
    (EmergencyHotKeyService.Modifiers & EmergencyHotKeyService.NoRepeatModifier) != 0,
    "紧急退出快捷键应使用 Ctrl、Alt、Shift、Win 与 F1，并禁止长按重复触发");
Check(
    EmergencyHotKeyService.GetStatusText(
        EmergencyHotKeyRegistrationStatus.Registered).Contains("已注册", StringComparison.Ordinal) &&
    EmergencyHotKeyService.GetStatusText(
        EmergencyHotKeyRegistrationStatus.Conflict).Contains("冲突", StringComparison.Ordinal) &&
    EmergencyHotKeyService.GetStatusText(
        EmergencyHotKeyRegistrationStatus.Failed).Contains("失败", StringComparison.Ordinal),
    "紧急退出快捷键注册状态应能区分成功、冲突与其他失败");
Check(
    TrayService.BuildNotifyIconText(
        EmergencyHotKeyRegistrationStatus.Registered).Contains(
            EmergencyHotKeyService.DisplayShortcut,
            StringComparison.Ordinal) &&
    TrayService.BuildNotifyIconText(
        EmergencyHotKeyRegistrationStatus.Conflict).Contains("冲突", StringComparison.Ordinal),
    "托盘提示应显示快捷键本身或冲突状态");
var applicationPath = @"C:\Program Files\LanDeviceMonitor\局域网设备助手.exe";
var terminationOrder = EmergencyProcessTerminator.BuildTerminationOrder(
    [
        new EmergencyProcessDescriptor(101, applicationPath),
        new EmergencyProcessDescriptor(202, @"C:\Other\局域网设备助手.exe"),
        new EmergencyProcessDescriptor(303, applicationPath),
        new EmergencyProcessDescriptor(404, null)
    ],
    applicationPath,
    currentProcessId: 303);
Check(
    terminationOrder.SequenceEqual([101, 303]),
    "紧急退出只能处理同一路径的软件实例，并应最后结束当前进程");

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
Check(DeviceItemViewModel.FormatSpeed(1024) == "1.00 KB/s", "速度单位换算不正确");
Check(DeviceItemViewModel.FormatBytes(1024L * 1024 * 1024) == "1.00 GB", "流量单位换算不正确");
Check(
    DeviceItemViewModel.FormatSpeedValue(6L * 1024 * 1024 + 760 * 1024) == "6.74" &&
    DeviceItemViewModel.FormatSpeedUnit(6L * 1024 * 1024 + 760 * 1024) == "MB/s",
    "流量卡片的数值与单位拆分不正确");
Check(new AppSettings().RefreshIntervalSeconds == 1,
    "刷新间隔默认值应为 1 秒");

var pollPersistenceStatePath = Path.Combine(
    Path.GetTempPath(),
    "LanDeviceMonitor.Tests",
    Guid.NewGuid().ToString("N"),
    "state.json");
var pollPersistenceStateDirectory = Path.GetDirectoryName(pollPersistenceStatePath)!;
try
{
    var pollPersistenceRepository = new DeviceRepository(pollPersistenceStatePath);
    var pollPersistenceStartedAt = new DateTimeOffset(
        2026, 8, 23, 12, 0, 0, TimeSpan.FromHours(8));
    var pollPersistenceSnapshot = new DeviceSnapshot(
        "00:00:00:00:20:03",
        "轮询落盘测试设备",
        "poll-persistence-test",
        true,
        "10.77.0.242",
        2,
        30,
        1_024,
        2_048,
        10_000,
        20_000);

    await pollPersistenceRepository.ApplyPollAsync(
        [pollPersistenceSnapshot],
        pollPersistenceStartedAt);
    var stateAfterInitialPoll = await File.ReadAllTextAsync(pollPersistenceStatePath);

    await pollPersistenceRepository.ApplyPollAsync(
        [pollPersistenceSnapshot],
        pollPersistenceStartedAt.AddSeconds(1));
    var stateBeforeCheckpoint = await File.ReadAllTextAsync(pollPersistenceStatePath);

    await pollPersistenceRepository.ApplyPollAsync(
        [pollPersistenceSnapshot],
        pollPersistenceStartedAt.AddMinutes(5));
    var stateAfterCheckpoint = await File.ReadAllTextAsync(pollPersistenceStatePath);

    await pollPersistenceRepository.ApplyPollAsync(
        [],
        pollPersistenceStartedAt.AddMinutes(5).AddSeconds(1));
    await pollPersistenceRepository.ApplyPollAsync(
        [],
        pollPersistenceStartedAt.AddMinutes(5).AddSeconds(16));
    var reloadedPollPersistenceRepository = new DeviceRepository(pollPersistenceStatePath);
    await reloadedPollPersistenceRepository.LoadAsync();
    var persistedOfflineTransition =
        (await reloadedPollPersistenceRepository.GetDevicesAsync()).Single().Profile.LastKnownOnline == false;

    Check(
        stateAfterInitialPoll == stateBeforeCheckpoint,
        "常规轮询不应每秒重写 state.json，避免长期高频磁盘写入");
    Check(
        stateAfterCheckpoint != stateBeforeCheckpoint,
        "轮询状态应在五分钟检查点写入 state.json");
    Check(
        persistedOfflineTransition,
        "确认的上下线状态变化应立即写入 state.json，避免丢失设备历史");
}
finally
{
    if (Directory.Exists(pollPersistenceStateDirectory))
    {
        Directory.Delete(pollPersistenceStateDirectory, recursive: true);
    }
}

Check(
    MainWindow.GetNextTrafficRangeMinutes(1) == 5 &&
    MainWindow.GetNextTrafficRangeMinutes(5) == 10 &&
    MainWindow.GetNextTrafficRangeMinutes(10) == 1,
    "流量统计区间按钮应按 1、5、10 分钟循环切换");

Check(
    TrafficChart.GetFixedSlotCount(1) == 61 &&
    TrafficChart.GetFixedSlotCount(5) == 301 &&
    TrafficChart.GetFixedSlotCount(10) == 601,
    "流量图表的 1、5、10 分钟时间窗应分别使用固定的一秒采样槽");
Check(
    TrafficChart.GetTimeAxisMaximumLabelCount(1) == 6 &&
    TrafficChart.GetTimeAxisMaximumLabelCount(5) == 6 &&
    TrafficChart.GetTimeAxisMaximumLabelCount(10) == 8,
    "流量图表时间轴标签数量应与 Clash Verge 的区间策略一致");
Check(
    TrafficChart.FormatRate(0) == "0.00 B" &&
    TrafficChart.FormatRate(1024) == "1.00 KB" &&
    TrafficChart.FormatRate(1024 * 1024) == "1.00 MB" &&
    TrafficChart.FormatRate(1000) == "1000 B",
    "图表 Y 轴与悬停提示应使用 Clash Verge 的三位有效数字单位换算");

var trafficHistoryNow = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.FromHours(8));
var trafficHistory = new TrafficHistoryBuffer();
trafficHistory.Append(new TrafficSample(trafficHistoryNow.AddMinutes(-11), 1, 2));
trafficHistory.Append(new TrafficSample(trafficHistoryNow.AddMinutes(-5), 3, 4));
trafficHistory.Append(new TrafficSample(trafficHistoryNow, 5, 6));
var recentTraffic = trafficHistory.Snapshot(trafficHistoryNow);
Check(
    recentTraffic.Count == 601 &&
    recentTraffic[0].Timestamp == trafficHistoryNow.AddMinutes(-10) &&
    recentTraffic[0].UploadBytesPerSecond == 1 &&
    recentTraffic[300].Timestamp == trafficHistoryNow.AddMinutes(-5) &&
    recentTraffic[300].UploadBytesPerSecond == 3 &&
    recentTraffic[^1].DownloadBytesPerSecond == 6,
    "流量图表应按 Clash Verge 只绘制当前时间窗内已有采样");
var replacedTraffic = new TrafficHistoryBuffer();
replacedTraffic.Append(new TrafficSample(trafficHistoryNow, 1, 2));
replacedTraffic.Append(new TrafficSample(trafficHistoryNow, 7, 8));
var replacedTrafficSnapshot = replacedTraffic.Snapshot(trafficHistoryNow);
Check(
    replacedTrafficSnapshot.Count == 1 &&
    replacedTrafficSnapshot[0].UploadBytesPerSecond == 7 &&
    replacedTrafficSnapshot[0].DownloadBytesPerSecond == 8,
    "同一秒的流量采样应更新当前槽位而不是制造重复点");
var sparseTraffic = new TrafficHistoryBuffer();
sparseTraffic.Append(new TrafficSample(trafficHistoryNow, 9, 11));
sparseTraffic.Append(new TrafficSample(trafficHistoryNow.AddSeconds(2), 13, 17));
var sparseTrafficSnapshot = sparseTraffic.Snapshot(trafficHistoryNow.AddSeconds(2));
Check(
    sparseTrafficSnapshot.Count == 3 &&
    sparseTrafficSnapshot[1].UploadBytesPerSecond == 9 &&
    sparseTrafficSnapshot[1].DownloadBytesPerSecond == 11,
    "缺失的一秒采样槽应沿用最近有效值而不是归零");
var sameSecondZeroTraffic = new TrafficHistoryBuffer();
sameSecondZeroTraffic.Append(new TrafficSample(trafficHistoryNow, 9, 11));
sameSecondZeroTraffic.Append(new TrafficSample(trafficHistoryNow, 0, 0));
var sameSecondZeroTrafficSnapshot = sameSecondZeroTraffic.Snapshot(trafficHistoryNow);
var isolatedZeroTraffic = new TrafficHistoryBuffer();
isolatedZeroTraffic.Append(new TrafficSample(trafficHistoryNow, 100, 30));
isolatedZeroTraffic.Append(new TrafficSample(trafficHistoryNow.AddSeconds(1), 0, 0));
isolatedZeroTraffic.Append(new TrafficSample(trafficHistoryNow.AddSeconds(2), 140, 50));
var isolatedZeroTrafficSnapshot = isolatedZeroTraffic.Snapshot(
    trafficHistoryNow.AddSeconds(2));
var sustainedZeroTraffic = new TrafficHistoryBuffer();
sustainedZeroTraffic.Append(new TrafficSample(trafficHistoryNow, 100, 30));
sustainedZeroTraffic.Append(new TrafficSample(trafficHistoryNow.AddSeconds(1), 0, 0));
sustainedZeroTraffic.Append(new TrafficSample(trafficHistoryNow.AddSeconds(2), 0, 0));
var sustainedZeroTrafficSnapshot = sustainedZeroTraffic.Snapshot(
    trafficHistoryNow.AddSeconds(2));
Check(
    sameSecondZeroTrafficSnapshot is [{ UploadBytesPerSecond: 9, DownloadBytesPerSecond: 11 }] &&
    isolatedZeroTrafficSnapshot[1].UploadBytesPerSecond == 120 &&
    isolatedZeroTrafficSnapshot[1].DownloadBytesPerSecond == 40 &&
    sustainedZeroTrafficSnapshot[1].UploadBytesPerSecond == 0 &&
    sustainedZeroTrafficSnapshot[1].DownloadBytesPerSecond == 0,
    "同秒晚到的零样本与正值-零值-正值的孤立样本不得留下归零竖线，连续零样本仍应保留");
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
Check(
    TrayService.CalculateMenuPosition(
        new System.Drawing.Point(1200, 800),
        new System.Drawing.Rectangle(0, 0, 1920, 1040),
        180,
        90,
        1d) == new System.Drawing.Point(1191, 719),
    "托盘菜单可见区域的左下角应与托盘图标右上角对齐");
Check(
    TrayService.CalculateMenuPosition(
        new System.Drawing.Point(4, 1000),
        new System.Drawing.Rectangle(0, 0, 1920, 1040),
        180,
        90,
        1d).X == 0,
    "托盘菜单不应超出工作区左侧");
Check(
    TrayService.CalculateMenuPosition(
        new System.Drawing.Point(100, 20),
        new System.Drawing.Rectangle(0, 0, 1920, 1040),
        180,
        90,
        1d).Y == 11,
    "顶部空间不足时托盘菜单应显示在右键位置下方");
Check(
    Math.Abs(SmoothScrollBehavior.EaseOutQuadratic(0.25d) - 0.4375d) < 0.0001d &&
    Math.Abs(SmoothScrollBehavior.EaseOutQuadratic(0.5d) - 0.75d) < 0.0001d &&
    Math.Abs(SmoothScrollBehavior.EaseOutQuadratic(0.75d) - 0.9375d) < 0.0001d,
    "滚轮平滑滚动应恢复为上一版的二次非线性缓出曲线");
Check(
    Math.Abs(SmoothScrollBehavior.GetDeviceListWheelDelta(-120) - 170d) < 0.0001d,
    "左侧设备栏单次标准滚轮距离应为 170 DIP");
Check(
    Math.Abs(SmoothScrollBehavior.GetDeviceListWheelDelta(-18) - 25.5d) < 0.0001d,
    "左侧设备栏应按高精度滚轮输入幅度滚动，不能把小输入放大成整格");
Check(
    Math.Abs(SmoothScrollBehavior.GetContentWheelDelta(-120) - 180d) < 0.0001d,
    "右侧内容板块应使用 180 DIP 的非线性平滑滚动");
Check(
    Math.Abs(SmoothScrollBehavior.GetDropDownWheelDelta(-120) - 10d) < 0.0001d,
    "下拉栏应使用 10 DIP 的非线性平滑滚动");
Check(
    Math.Abs(SmoothScrollBehavior.GetAnimationDurationMilliseconds() - 400d) < 0.0001d,
    "左右板块平滑滚动动画时长均应为 400 ms");
Check(
    Math.Abs(
        SmoothScrollBehavior.GetDropDownAnimationDurationMilliseconds() -
        150d) < 0.0001d,
    "下拉栏平滑滚动应在 150 ms 内跟手停止");
Check(
    Math.Abs(
        SmoothScrollBehavior.GetBoundedTargetOffset(
            100d,
            108d,
            10d,
            500d,
            10d) -
        110d) < 0.0001d,
    "下拉栏连续滚轮输入最多只应保留一格待滚动距离");
Check(
    Math.Abs(SmoothScrollBehavior.GetBoundedTargetOffset(100d, 400d, 170d, 500d) - 440d) < 0.0001d,
    "左侧连续滚轮输入的待滚动距离应限制为 340 DIP");
Check(
    Math.Abs(SmoothScrollBehavior.GetBoundedTargetOffset(100d, 164d, -100d, 500d) - 0d) < 0.0001d,
    "反向滚轮输入应立即反向，不能延续上一方向的惯性");
Check(
    MainWindow.GetDisplayVersion() == "v0.1.0-dev.136",
    "设置界面应显示当前实际软件版本");
Check(
    NotificationService.GetDeliveryStatusText(
        NotificationDeliveryChannel.WindowsToast,
        null).Contains("已提交到通知中心", StringComparison.Ordinal) &&
    NotificationService.GetDeliveryStatusText(
        NotificationDeliveryChannel.TrayFallback,
        "COMException").Contains("托盘提示", StringComparison.Ordinal),
    "通知状态应区分 Windows 原生投递和托盘兜底");
Check(
    new NotificationDeliveryResult(
        NotificationDeliveryChannel.Pending,
        "已就绪").IsNativeWindowsNotificationReady &&
    !new NotificationDeliveryResult(
        NotificationDeliveryChannel.TrayFallback,
        "已降级").IsNativeWindowsNotificationReady,
    "通知注册就绪状态不应被标记为故障");
Check(
    new LanDeviceMonitor.ViewModels.SessionItemViewModel(
        new LanDeviceMonitor.Models.DeviceSession
        {
            OnlineAt = new DateTimeOffset(
                2026, 8, 3, 9, 5, 6, TimeSpan.FromHours(8)),
            OfflineAt = new DateTimeOffset(
                2026, 8, 3, 9, 40, 59, TimeSpan.FromHours(8))
        }) is
        {
            OnlineDateText: "08月03日",
            OnlineTimeText: "09:05",
            OfflineDateText: "08月03日",
            OfflineTimeText: "09:40"
        },
    "上下线历史界面应分别显示日期与时间，隐藏年份和秒，后台仍记录完整日期时间");
Check(
    new LanDeviceMonitor.ViewModels.SessionItemViewModel(
        new LanDeviceMonitor.Models.DeviceSession
        {
            OnlineAt = new DateTimeOffset(
                2026, 8, 3, 9, 5, 6, TimeSpan.FromHours(8))
        }) is
        {
            OfflineDateText: "当前在线",
            OfflineTimeText: ""
        },
    "没有离线时间时应在日期部分显示“当前在线”");
var removalStatePath = Path.Combine(
    Path.GetTempPath(),
    "LanDeviceMonitor.Tests",
    Guid.NewGuid().ToString("N"),
    "state.json");
var removalRepository = new DeviceRepository(removalStatePath);
var removalSnapshot = new LanDeviceMonitor.Models.DeviceSnapshot(
    "00:00:00:00:20:01",
    "移除测试设备",
    "remove-test",
    true,
    "10.77.0.240",
    2,
    30,
    0,
    0,
    0,
    0);
var removalNow = new DateTimeOffset(
    2026, 7, 30, 12, 0, 0, TimeSpan.FromHours(8));
await removalRepository.ApplyPollAsync([removalSnapshot], removalNow);
await removalRepository.RemoveDeviceUntilNextOnlineAsync(
    removalSnapshot.MacAddress);
var hiddenImmediately =
    (await removalRepository.GetDevicesAsync()).Count == 0;
await removalRepository.ApplyPollAsync(
    [removalSnapshot],
    removalNow.AddSeconds(3));
var hiddenWhileStillOnline =
    (await removalRepository.GetDevicesAsync()).Count == 0;
await removalRepository.ApplyPollAsync(
    [],
    removalNow.AddSeconds(6));
await removalRepository.ApplyPollAsync(
    [removalSnapshot],
    removalNow.AddSeconds(24));
var restoredAfterNextOnline =
    (await removalRepository.GetDevicesAsync()).Count == 1;
Check(
    hiddenImmediately &&
    hiddenWhileStillOnline &&
    restoredAfterNextOnline,
    "移除的设备应立即隐藏、持续在线时保持隐藏，并仅在离线后再次上线时恢复");
var removalStateDirectory = Path.GetDirectoryName(removalStatePath);
if (!string.IsNullOrWhiteSpace(removalStateDirectory) &&
    Directory.Exists(removalStateDirectory))
{
    Directory.Delete(removalStateDirectory, recursive: true);
}

var trafficFilterStatePath = Path.Combine(
    Path.GetTempPath(),
    "LanDeviceMonitor.Tests",
    Guid.NewGuid().ToString("N"),
    "state.json");
var trafficFilterRepository = new DeviceRepository(trafficFilterStatePath);
var trafficFilterNow = new DateTimeOffset(
    2026, 8, 13, 18, 0, 0, TimeSpan.FromHours(8));
var trafficFilterMac = "00:00:00:00:20:02";
DeviceSnapshot TrafficFilterSnapshot(
    long uploadSpeed,
    long totalUpload,
    bool online = true) => new(
        trafficFilterMac,
        "流量过滤测试设备",
        "traffic-filter-test",
        online,
        "10.77.0.241",
        2,
        30,
        uploadSpeed,
        0,
        totalUpload,
        0);

var firstTrafficUpdate = await trafficFilterRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(6_000_000, 100)],
    trafficFilterNow);
var heldTrafficUpdate = await trafficFilterRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 100)],
    trafficFilterNow.AddSeconds(7));
var expiredTrafficUpdate = await trafficFilterRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 100)],
    trafficFilterNow.AddSeconds(21));
var totalDerivedStatePath = Path.Combine(
    Path.GetTempPath(),
    "LanDeviceMonitor.Tests",
    Guid.NewGuid().ToString("N"),
    "state.json");
var totalDerivedRepository = new DeviceRepository(totalDerivedStatePath);
var firstDerivedUpdate = await totalDerivedRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 1_000)],
    trafficFilterNow);
var derivedTrafficUpdate = await totalDerivedRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(7_000_000, 4_001_000)],
    trafficFilterNow.AddSeconds(4));
var sustainedTrafficUpdate = await totalDerivedRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 4_001_000)],
    trafficFilterNow.AddSeconds(19));
var idleTrafficUpdate = await totalDerivedRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 4_001_000)],
    trafficFilterNow.AddSeconds(44));
var zeroConfirmationStatePath = Path.Combine(
    Path.GetTempPath(),
    "LanDeviceMonitor.Tests",
    Guid.NewGuid().ToString("N"),
    "state.json");
var zeroConfirmationRepository = new DeviceRepository(zeroConfirmationStatePath);
var zeroConfirmationFirst = await zeroConfirmationRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(6_000_000, 1_000)],
    trafficFilterNow);
var zeroConfirmationAfterGrace = await zeroConfirmationRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 1_000)],
    trafficFilterNow.AddSeconds(22));
var zeroConfirmationBeforeBoundary = await zeroConfirmationRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 1_000)],
    trafficFilterNow.AddSeconds(33));
var zeroConfirmationFinal = await zeroConfirmationRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 1_000)],
    trafficFilterNow.AddSeconds(35));
var directionIndependentStatePath = Path.Combine(
    Path.GetTempPath(),
    "LanDeviceMonitor.Tests",
    Guid.NewGuid().ToString("N"),
    "state.json");
var directionIndependentRepository = new DeviceRepository(directionIndependentStatePath);
DeviceSnapshot DirectionIndependentSnapshot(
    long uploadSpeed,
    long downloadSpeed,
    long totalUpload,
    long totalDownload) => new(
        trafficFilterMac,
        "流量方向独立测试设备",
        "traffic-direction-test",
        true,
        "10.77.0.241",
        2,
        30,
        uploadSpeed,
        downloadSpeed,
        totalUpload,
        totalDownload);
await directionIndependentRepository.ApplyPollAsync(
    [DirectionIndependentSnapshot(6_000_000, 800_000, 1_000, 1_000)],
    trafficFilterNow);
var directionIndependentUpdate = await directionIndependentRepository.ApplyPollAsync(
    [DirectionIndependentSnapshot(0, 850_000, 1_000, 20_000_000)],
    trafficFilterNow.AddSeconds(21));
var delayedSnapshotStatePath = Path.Combine(
    Path.GetTempPath(),
    "LanDeviceMonitor.Tests",
    Guid.NewGuid().ToString("N"),
    "state.json");
var delayedSnapshotRepository = new DeviceRepository(delayedSnapshotStatePath);
await delayedSnapshotRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(6_000_000, 1_000)],
    trafficFilterNow);
var delayedSnapshotUpdate = await delayedSnapshotRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(6_000_000, 31_000_000)],
    trafficFilterNow.AddSeconds(1));
var offlineDebounceStatePath = Path.Combine(
    Path.GetTempPath(),
    "LanDeviceMonitor.Tests",
    Guid.NewGuid().ToString("N"),
    "state.json");
var offlineDebounceRepository = new DeviceRepository(offlineDebounceStatePath);
var offlineDebounceInitial = await offlineDebounceRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(6_000_000, 100)],
    trafficFilterNow);
var transientOfflineUpdate = await offlineDebounceRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 100, online: false)],
    trafficFilterNow.AddSeconds(1));
var restoredAfterTransientOfflineUpdate = await offlineDebounceRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 100)],
    trafficFilterNow.AddSeconds(2));
var transientMissingUpdate = await offlineDebounceRepository.ApplyPollAsync(
    [],
    trafficFilterNow.AddSeconds(3));
var restoredAfterTransientMissingUpdate = await offlineDebounceRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 100)],
    trafficFilterNow.AddSeconds(4));
var confirmedOfflinePendingUpdate = await offlineDebounceRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 100, online: false)],
    trafficFilterNow.AddSeconds(20));
var confirmedOfflineBeforeBoundaryUpdate = await offlineDebounceRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 100, online: false)],
    trafficFilterNow.AddSeconds(34));
var confirmedOfflineFinalUpdate = await offlineDebounceRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 100, online: false)],
    trafficFilterNow.AddSeconds(35));
var restoredSpeedStatePath = Path.Combine(
    Path.GetTempPath(),
    "LanDeviceMonitor.Tests",
    Guid.NewGuid().ToString("N"),
    "state.json");
var restoredSpeedRepository = new DeviceRepository(restoredSpeedStatePath);
await restoredSpeedRepository.ApplyPollAsync(
    [TrafficFilterSnapshot(2_000_000, 500)],
    trafficFilterNow);
var restoredSpeedRepositoryAfterRestart = new DeviceRepository(restoredSpeedStatePath);
await restoredSpeedRepositoryAfterRestart.LoadAsync();
var restoredZeroUpdate = await restoredSpeedRepositoryAfterRestart.ApplyPollAsync(
    [TrafficFilterSnapshot(0, 500)],
    trafficFilterNow.AddSeconds(1));
Check(
    firstTrafficUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    heldTrafficUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    expiredTrafficUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    derivedTrafficUpdate.Devices.Single().Profile.LastUploadSpeedBytes is > 900_000 and < 1_100_000 &&
    sustainedTrafficUpdate.Devices.Single().Profile.LastUploadSpeedBytes is > 900_000 and < 1_100_000 &&
    idleTrafficUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 1_000_000 &&
    zeroConfirmationFirst.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    zeroConfirmationAfterGrace.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    zeroConfirmationBeforeBoundary.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    zeroConfirmationFinal.Devices.Single().Profile.LastUploadSpeedBytes == 0 &&
    directionIndependentUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    directionIndependentUpdate.Devices.Single().Profile.LastDownloadSpeedBytes > 800_000 &&
    delayedSnapshotUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    offlineDebounceInitial.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    transientOfflineUpdate.Devices.Single().Profile.LastKnownOnline &&
    transientOfflineUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    transientOfflineUpdate.Transitions.Count == 0 &&
    restoredAfterTransientOfflineUpdate.Devices.Single().Profile.LastKnownOnline &&
    restoredAfterTransientOfflineUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    restoredAfterTransientOfflineUpdate.Transitions.Count == 0 &&
    transientMissingUpdate.Devices.Single().Profile.LastKnownOnline &&
    transientMissingUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    transientMissingUpdate.Transitions.Count == 0 &&
    restoredAfterTransientMissingUpdate.Devices.Single().Profile.LastKnownOnline &&
    restoredAfterTransientMissingUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    restoredAfterTransientMissingUpdate.Transitions.Count == 0 &&
    confirmedOfflinePendingUpdate.Devices.Single().Profile.LastKnownOnline &&
    confirmedOfflinePendingUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    confirmedOfflineBeforeBoundaryUpdate.Devices.Single().Profile.LastKnownOnline &&
    confirmedOfflineBeforeBoundaryUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 6_000_000 &&
    !confirmedOfflineFinalUpdate.Devices.Single().Profile.LastKnownOnline &&
    confirmedOfflineFinalUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 0 &&
    confirmedOfflineFinalUpdate.Transitions is [{ IsOnline: false }] &&
    restoredZeroUpdate.Devices.Single().Profile.LastUploadSpeedBytes == 2_000_000,
    "累计字节应成为网速主数据源：原始瞬时速度不能覆盖正常累计差分；累计快照延迟造成的假尖峰要回退到瞬时时间基准，累计快照未更新时保持上一段速率，持续零证据确认后才归零，且上传下载必须独立判断，重启后的首轮占位 0 也不能制造断崖");
foreach (var trafficFilterDirectory in new[]
         {
              Path.GetDirectoryName(trafficFilterStatePath),
               Path.GetDirectoryName(totalDerivedStatePath),
              Path.GetDirectoryName(zeroConfirmationStatePath),
              Path.GetDirectoryName(directionIndependentStatePath),
               Path.GetDirectoryName(delayedSnapshotStatePath),
              Path.GetDirectoryName(restoredSpeedStatePath),
              Path.GetDirectoryName(offlineDebounceStatePath)
         }.Where(directory => !string.IsNullOrWhiteSpace(directory)))
{
    if (Directory.Exists(trafficFilterDirectory!))
    {
        Directory.Delete(trafficFilterDirectory!, recursive: true);
    }
}
Check(
    MainWindow.GetRouterLoadingVisibility(showLoadingMessage: false) ==
        System.Windows.Visibility.Collapsed &&
    MainWindow.GetRouterLoadingVisibility(showLoadingMessage: true) ==
        System.Windows.Visibility.Visible,
    "首次进入路由器页面时不显示读取提示，手动刷新时显示读取提示");
Check(
    MicaWindowService.GetPersistentAcrylicGradientColor(
        useDarkMode: false) == unchecked((int)0x14F5F5F5) &&
    MicaWindowService.GetPersistentAcrylicGradientColor(
        useDarkMode: true) == unchecked((int)0x30202020),
    "浅色和深色模式应使用固定的持久 Acrylic 染色值");
Exception? trayMenuOpenException = null;
var trayMenuOpenInvoked = false;
var verticalScrollBarVisible = false;
var verticalScrollBarWidth = 0d;
var verticalScrollThumbVisible = false;
var verticalScrollBarMaximum = 0d;
var verticalScrollBarViewport = 0d;
var verticalScrollThumbHeight = 0d;
var verticalScrollThumbMinimumHeight = 0d;
var scrollThumbDragAnimationConfigured = false;
var scrollThumbHoverAnimationConfigured = false;
var deviceListScrollBarVisible = false;
var settingsScrollBarVisible = false;
var deviceListScrollThumbHeight = 0d;
var settingsScrollThumbHeight = 0d;
var deviceListHoverAnimationConfigured = false;
var trayMenuHoverAnimationConfigured = false;
var trayOpenIconTextAligned = false;
var acrylicAccentApplied = false;
var systemBackdropStableDuringWindowTransitions = false;
var systemBackdropTransitionDiagnostics = "";
var themePaletteConfigured = false;
var dangerConfirmButtonConfigured = false;
var detailIconThemeConfigured = false;
var topButtonToolTipsReadableInDarkMode = false;
var closeCaptionHoverConfigured = false;
var detailSectionOrderConfigured = false;
var roundedControlGeometryMatchesDev48 = false;
var timelineDurationBadgeBorderConfigured = false;
var comboDropDownUsesPageScrollStyle = false;
var deviceProfileControlsHaveMatchingHeight = false;
var detailTitleSpacingConfigured = false;
var quietTimeEditorsConfigured = false;
var deviceSelectionStableDuringRefresh = false;
var deviceFilterAndSortConfigured = false;
var filterHoverAndPressConfigured = false;
var listFilterSlideConfigured = false;
var detailFilterOriginalAnimationConfigured = false;
var offlineDeviceManagementConfigured = false;
var confirmationBehaviorConfigured = false;
var listFilterSlideDiagnostics = "";
var detailFilterSlideDiagnostics = "";
var trayHoverColorAndSpacingConfigured = false;
var visualQaImagePath = Environment.GetEnvironmentVariable(
    "LAN_DEVICE_MONITOR_QA_IMAGE");
var darkVisualQaImagePath = Environment.GetEnvironmentVariable(
    "LAN_DEVICE_MONITOR_DARK_QA_IMAGE");
var trayVisualQaImagePath = Environment.GetEnvironmentVariable(
    "LAN_DEVICE_MONITOR_TRAY_QA_IMAGE");
var trayMenuThread = new Thread(() =>
{
    try
    {
        var application = new LanDeviceMonitor.App();
        application.InitializeComponent();
        ThemeService.Apply(ThemeService.LightMode);
        var lightScrollDefault =
            (System.Windows.Media.Color)application.Resources["ScrollThumbColor"];
        var lightScrollHover =
            (System.Windows.Media.Color)application.Resources["ScrollThumbHoverColor"];
        var lightCombo =
            (System.Windows.Media.Color)application.Resources["ComboBoxColor"];
        var lightControl =
            (System.Windows.Media.Color)application.Resources["ControlColor"];
        var lightPopup =
            (System.Windows.Media.Color)application.Resources["ComboBoxPopupColor"];
        var lightPanel =
            (System.Windows.Media.Color)application.Resources["MicaPanelColor"];
        var lightCard =
            (System.Windows.Media.Color)application.Resources["MicaCardColor"];
        var lightAccentSurface =
            (System.Windows.Media.Color)application.Resources["AccentLightColor"];
        var lightTimelineItem =
            (System.Windows.Media.Color)application.Resources["TimelineItemColor"];
        var lightTimelineDuration =
            (System.Windows.Media.Color)application.Resources["TimelineDurationColor"];
        var lightCaptionHover =
            (System.Windows.Media.Color)application.Resources["CaptionHoverColor"];
        var lightDeviceListItem =
            (System.Windows.Media.Color)application.Resources["DeviceListItemColor"];
        var lightDeviceListItemSelected =
            (System.Windows.Media.Color)application.Resources["DeviceListItemSelectedColor"];
        var lightDeviceListItemHover =
            (System.Windows.Media.Color)application.Resources["DeviceListItemHoverColor"];
        var lightDeviceListItemSelectedHover =
            (System.Windows.Media.Color)application.Resources["DeviceListItemSelectedHoverColor"];
        var lightOnlineCard =
            (System.Windows.Media.Color)application.Resources["OnlineCardColor"];
        var lightOfflineCard =
            (System.Windows.Media.Color)application.Resources["OfflineCardColor"];
        var lightOnlineCardBorder =
            (System.Windows.Media.Color)application.Resources["OnlineCardBorderColor"];
        var lightOfflineCardBorder =
            (System.Windows.Media.Color)application.Resources["OfflineCardBorderColor"];
        var lightOnlineDot =
            (System.Windows.Media.Color)application.Resources["TimelineOnlineDotColor"];
        var lightOfflineDot =
            (System.Windows.Media.Color)application.Resources["TimelineOfflineDotColor"];
        var dangerConfirmButton = new System.Windows.Controls.Button
        {
            Style = (System.Windows.Style)application.FindResource(
                "DangerConfirmButtonStyle")
        };
        dangerConfirmButton.ApplyTemplate();
        dangerConfirmButtonConfigured =
            dangerConfirmButton.Foreground is
                System.Windows.Media.SolidColorBrush dangerForeground &&
            dangerForeground.Color ==
                System.Windows.Media.Colors.White &&
            dangerConfirmButton.Background is
                System.Windows.Media.SolidColorBrush dangerBackground &&
            dangerBackground.Color ==
                (System.Windows.Media.Color)application.Resources[
                    "DangerConfirmColor"];
        ThemeService.Apply(ThemeService.DarkMode);
        var darkScrollDefault =
            (System.Windows.Media.Color)application.Resources["ScrollThumbColor"];
        var darkScrollHover =
            (System.Windows.Media.Color)application.Resources["ScrollThumbHoverColor"];
        var darkCombo =
            (System.Windows.Media.Color)application.Resources["ComboBoxColor"];
        var darkControl =
            (System.Windows.Media.Color)application.Resources["ControlColor"];
        var darkPopup =
            (System.Windows.Media.Color)application.Resources["ComboBoxPopupColor"];
        var darkPanel =
            (System.Windows.Media.Color)application.Resources["MicaPanelColor"];
        var darkCard =
            (System.Windows.Media.Color)application.Resources["MicaCardColor"];
        var darkAccentSurface =
            (System.Windows.Media.Color)application.Resources["AccentLightColor"];
        var darkTimelineItem =
            (System.Windows.Media.Color)application.Resources["TimelineItemColor"];
        var darkTimelineDuration =
            (System.Windows.Media.Color)application.Resources["TimelineDurationColor"];
        var darkCaptionHover =
            (System.Windows.Media.Color)application.Resources["CaptionHoverColor"];
        var darkOnlineCard =
            (System.Windows.Media.Color)application.Resources["OnlineCardColor"];
        var darkOfflineCard =
            (System.Windows.Media.Color)application.Resources["OfflineCardColor"];
        var darkOnlineCardBorder =
            (System.Windows.Media.Color)application.Resources["OnlineCardBorderColor"];
        var darkOfflineCardBorder =
            (System.Windows.Media.Color)application.Resources["OfflineCardBorderColor"];
        var darkOnlineDot =
            (System.Windows.Media.Color)application.Resources["TimelineOnlineDotColor"];
        var darkOfflineDot =
            (System.Windows.Media.Color)application.Resources["TimelineOfflineDotColor"];
        var darkToolTip =
            (System.Windows.Media.Color)application.Resources["ToolTipColor"];
        var darkText =
            (System.Windows.Media.Color)application.Resources["TextColor"];
        themePaletteConfigured =
            lightScrollDefault == System.Windows.Media.Color.FromArgb(0x88, 0x66, 0x66, 0x66) &&
            lightScrollHover == System.Windows.Media.Color.FromArgb(0x8F, 0x66, 0x66, 0x66) &&
            darkScrollDefault == System.Windows.Media.Color.FromArgb(0x96, 0xC2, 0xC2, 0xC2) &&
            darkScrollHover == System.Windows.Media.Color.FromArgb(0x9E, 0xC2, 0xC2, 0xC2) &&
            lightScrollHover.A == (byte)Math.Round(lightScrollDefault.A * 1.05d) &&
            darkScrollHover.A == (byte)Math.Round(darkScrollDefault.A * 1.05d) &&
            lightCombo == lightControl &&
            lightPopup.A == 0xE6 &&
            darkCombo == darkControl &&
            darkPopup.A == 0xAD &&
            lightPanel == System.Windows.Media.Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF) &&
            lightCard == System.Windows.Media.Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF) &&
            lightAccentSurface == System.Windows.Media.Color.FromArgb(0xDE, 0xFF, 0xFF, 0xFF) &&
            darkPanel == System.Windows.Media.Color.FromArgb(0x09, 0x20, 0x20, 0x20) &&
            darkCard == System.Windows.Media.Color.FromArgb(0x54, 0x50, 0x50, 0x50) &&
            darkAccentSurface == System.Windows.Media.Color.FromArgb(0x74, 0x61, 0x61, 0x61) &&
            lightTimelineItem == System.Windows.Media.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) &&
            lightTimelineDuration == System.Windows.Media.Color.FromArgb(0x80, 0xFA, 0xFA, 0xFA) &&
            lightCaptionHover == System.Windows.Media.Color.FromArgb(0x2B, 0x00, 0x00, 0x00) &&
            lightDeviceListItem == System.Windows.Media.Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF) &&
            lightDeviceListItemSelected == System.Windows.Media.Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF) &&
            lightDeviceListItemHover == System.Windows.Media.Color.FromArgb(0xBF, 0xFF, 0xFF, 0xFF) &&
            lightDeviceListItemSelectedHover == System.Windows.Media.Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF) &&
            Math.Abs(
                1d -
                ((1d - (lightDeviceListItem.A / 255d)) *
                 (1d - (lightDeviceListItemHover.A / 255d))) -
                0.90d) < 0.001d &&
            lightOnlineCard == System.Windows.Media.Color.FromArgb(0x84, 0xD9, 0xF8, 0xE6) &&
            lightOfflineCard == System.Windows.Media.Color.FromArgb(0x84, 0xFF, 0xEA, 0xEA) &&
            lightOnlineCardBorder == System.Windows.Media.Color.FromArgb(0x70, 0x4C, 0xC8, 0x8A) &&
            lightOfflineCardBorder == System.Windows.Media.Color.FromArgb(0x40, 0xE3, 0x6C, 0x6C) &&
            darkOnlineCard == System.Windows.Media.Color.FromArgb(0x63, 0x19, 0x58, 0x3D) &&
            darkOfflineCard == System.Windows.Media.Color.FromArgb(0x63, 0x5B, 0x1B, 0x2E) &&
            darkOnlineCardBorder == System.Windows.Media.Color.FromArgb(0x90, 0x67, 0xDD, 0xA6) &&
            darkOfflineCardBorder == System.Windows.Media.Color.FromArgb(0x8C, 0xE3, 0x6E, 0x82) &&
            lightOnlineDot == System.Windows.Media.Color.FromRgb(0x68, 0xD9, 0xA3) &&
            lightOfflineDot == System.Windows.Media.Color.FromRgb(0xFF, 0x85, 0x85) &&
            darkOnlineDot == System.Windows.Media.Color.FromRgb(0x6C, 0xEF, 0xAE) &&
            darkOfflineDot == System.Windows.Media.Color.FromRgb(0xFF, 0x9B, 0x9B) &&
            darkTimelineItem == System.Windows.Media.Color.FromArgb(0x30, 0x58, 0x58, 0x58) &&
            darkTimelineDuration == System.Windows.Media.Color.FromArgb(0xA0, 0x8E, 0x8E, 0x8E) &&
            darkCaptionHover == System.Windows.Media.Color.FromArgb(0x52, 0x00, 0x00, 0x00) &&
            lightTimelineDuration.R == lightTimelineDuration.G &&
            lightTimelineDuration.G == lightTimelineDuration.B &&
            darkTimelineDuration.R == darkTimelineDuration.G &&
            darkTimelineDuration.G == darkTimelineDuration.B;
        var toolTipStyle =
            application.FindResource(typeof(System.Windows.Controls.ToolTip))
                as System.Windows.Style;
        topButtonToolTipsReadableInDarkMode =
            toolTipStyle is not null &&
            darkToolTip == System.Windows.Media.Color.FromArgb(0xB8, 0x3E, 0x3E, 0x3E) &&
            darkToolTip != darkText;
        var comboBoxStyle =
            (System.Windows.Style)application.FindResource(typeof(System.Windows.Controls.ComboBox));
        var textBoxStyle =
            (System.Windows.Style)application.FindResource(typeof(System.Windows.Controls.TextBox));
        var passwordBoxStyle =
            (System.Windows.Style)application.FindResource(typeof(System.Windows.Controls.PasswordBox));
        var cardStyle =
            (System.Windows.Style)application.FindResource("CardStyle");
        var timelineDurationBadgeStyle =
            (System.Windows.Style)application.FindResource(
                "TimelineDurationBadgeStyle");
        timelineDurationBadgeBorderConfigured =
            (System.Windows.Thickness)GetStyleSetterValue(
                timelineDurationBadgeStyle,
                System.Windows.Controls.Border.BorderThicknessProperty) ==
                new System.Windows.Thickness(1);
        roundedControlGeometryMatchesDev48 =
            (System.Windows.Thickness)GetStyleSetterValue(
                comboBoxStyle,
                System.Windows.Controls.Control.PaddingProperty) ==
                new System.Windows.Thickness(10, 7, 10, 7) &&
            (double)GetStyleSetterValue(
                comboBoxStyle,
                System.Windows.FrameworkElement.MinHeightProperty) == 36d &&
            (System.Windows.Thickness)GetStyleSetterValue(
                textBoxStyle,
                System.Windows.Controls.Control.PaddingProperty) ==
                new System.Windows.Thickness(12, 8, 12, 8) &&
            (double)GetStyleSetterValue(
                textBoxStyle,
                System.Windows.FrameworkElement.MinHeightProperty) == 36d &&
            (System.Windows.Thickness)GetStyleSetterValue(
                passwordBoxStyle,
                System.Windows.Controls.Control.PaddingProperty) ==
                new System.Windows.Thickness(12, 8, 12, 8) &&
            (double)GetStyleSetterValue(
                passwordBoxStyle,
                System.Windows.FrameworkElement.MinHeightProperty) == 36d &&
            (System.Windows.CornerRadius)GetStyleSetterValue(
                cardStyle,
                System.Windows.Controls.Border.CornerRadiusProperty) ==
                new System.Windows.CornerRadius(15) &&
            (System.Windows.Thickness)GetStyleSetterValue(
                cardStyle,
                System.Windows.Controls.Border.PaddingProperty) ==
                new System.Windows.Thickness(18);
        ThemeService.Apply(ThemeService.LightMode);
        var overflowViewer = new System.Windows.Controls.ScrollViewer
        {
            Style = (System.Windows.Style)application.FindResource(
                "WindowsSettingsScrollViewerStyle"),
            Width = 220,
            Height = 120,
            VerticalScrollBarVisibility =
                System.Windows.Controls.ScrollBarVisibility.Auto,
            Content = new System.Windows.Controls.Border
            {
                Width = 180,
                Height = 600
            }
        };
        var scrollbarTestWindow = new System.Windows.Window
        {
            Width = 240,
            Height = 140,
            Left = -10000,
            Top = -10000,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = System.Windows.WindowStyle.None,
            Content = overflowViewer
        };
        scrollbarTestWindow.Show();
        scrollbarTestWindow.UpdateLayout();
        var verticalScrollBar =
            FindVisualDescendant<System.Windows.Controls.Primitives.ScrollBar>(
                overflowViewer,
                scrollBar =>
                    scrollBar.Orientation ==
                    System.Windows.Controls.Orientation.Vertical);
        var verticalThumb =
            FindVisualDescendant<System.Windows.Controls.Primitives.Thumb>(
                verticalScrollBar);
        verticalScrollBarVisible =
            verticalScrollBar?.Visibility == System.Windows.Visibility.Visible;
        verticalScrollBarWidth = verticalScrollBar?.ActualWidth ?? 0d;
        verticalScrollBarMaximum = verticalScrollBar?.Maximum ?? 0d;
        verticalScrollBarViewport = verticalScrollBar?.ViewportSize ?? 0d;
        verticalScrollThumbHeight = verticalThumb?.ActualHeight ?? 0d;
        verticalScrollThumbMinimumHeight = verticalThumb?.MinHeight ?? 0d;
        verticalScrollThumbVisible =
            verticalThumb?.Visibility == System.Windows.Visibility.Visible &&
            verticalThumb.MinHeight >= 28d;
        var draggingTrigger = verticalThumb?.Template.Triggers
            .OfType<System.Windows.Trigger>()
            .FirstOrDefault(trigger =>
                trigger.Property ==
                    System.Windows.Controls.Primitives.Thumb.IsDraggingProperty &&
                Equals(trigger.Value, true));
        var dragEnterAnimation = draggingTrigger?.EnterActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault();
        var dragExitAnimation = draggingTrigger?.ExitActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault();
        scrollThumbDragAnimationConfigured =
            dragEnterAnimation?.Duration.HasTimeSpan == true &&
            dragEnterAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(300) &&
            dragEnterAnimation.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode:
                        System.Windows.Media.Animation.EasingMode.EaseIn
                } &&
            dragExitAnimation?.Duration.HasTimeSpan == true &&
            dragExitAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(300) &&
            dragExitAnimation.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode:
                        System.Windows.Media.Animation.EasingMode.EaseOut
                };
        var hoverTrigger = verticalThumb?.Template.Triggers
            .OfType<System.Windows.Trigger>()
            .FirstOrDefault(trigger =>
                trigger.Property == System.Windows.UIElement.IsMouseOverProperty &&
                Equals(trigger.Value, true));
        var hoverEnterAnimation = hoverTrigger?.EnterActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault();
        var hoverExitAnimation = hoverTrigger?.ExitActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault();
        scrollThumbHoverAnimationConfigured =
            hoverEnterAnimation?.Duration.HasTimeSpan == true &&
            hoverEnterAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(300) &&
            hoverEnterAnimation.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase &&
            hoverExitAnimation?.Duration.HasTimeSpan == true &&
            hoverExitAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(300) &&
            hoverExitAnimation.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase;
        scrollbarTestWindow.Close();

        var smokeWindow = new MainWindow(new NotificationService())
        {
            Left = -10000,
            Top = -10000,
            Width = 900,
            Height = 560,
            ShowActivated = false,
            ShowInTaskbar = false
        };
        var testDevices = Enumerable.Range(1, 20)
            .Select(index => new DeviceItemViewModel(new LanDeviceMonitor.Models.DeviceViewData
            {
                Profile = new LanDeviceMonitor.Models.DeviceProfile
                {
                    MacAddress = $"00:00:00:00:00:{index:00}",
                    CustomName = index == 1
                        ? "iPhone 13 Pro Max"
                        : string.Empty,
                    LastKnownOnline = true,
                    LastRouterName = $"测试设备 {index}",
                    LastConnectionType = 2
                },
                Identity = new LanDeviceMonitor.Models.DeviceIdentity(
                    index == 1
                        ? "窗帘"
                        : index == 2
                            ? "空调"
                            : "手机",
                    index <= 2 ? string.Empty : "\uE8EA",
                    $"测试设备 {index}"),
                Sessions = index == 1
                    ?
                    [
                        new LanDeviceMonitor.Models.DeviceSession
                        {
                            MacAddress = "00:00:00:00:00:01",
                            OnlineAt = new DateTimeOffset(
                                2026, 7, 24, 18, 27, 7, TimeSpan.FromHours(8)),
                            OfflineAt = new DateTimeOffset(
                                2026, 7, 24, 20, 35, 7, TimeSpan.FromHours(8))
                        }
                    ]
                    : []
            }))
            .ToList();
        smokeWindow.DeviceList.ItemsSource = testDevices;
        smokeWindow.EmptyPanel.Visibility = System.Windows.Visibility.Collapsed;
        smokeWindow.SettingsPanel.Visibility = System.Windows.Visibility.Visible;
        smokeWindow.Show();
        smokeWindow.UpdateLayout();
        var closeCaptionStyle =
            (System.Windows.Style)smokeWindow.FindResource(
                "CloseCaptionButtonStyle");
        var closeHoverTrigger =
            closeCaptionStyle.Triggers
                .OfType<System.Windows.Trigger>()
                .FirstOrDefault(trigger =>
                    trigger.Property ==
                        System.Windows.UIElement.IsMouseOverProperty &&
                    Equals(trigger.Value, true));
        var closeHoverBrush =
            closeHoverTrigger?.Setters
                .OfType<System.Windows.Setter>()
                .FirstOrDefault(setter =>
                    setter.Property ==
                        System.Windows.Controls.Control.BorderBrushProperty)
                ?.Value as System.Windows.Media.SolidColorBrush;
        var closeProbe = new System.Windows.Controls.Button
        {
            Style = closeCaptionStyle
        };
        closeProbe.ApplyTemplate();
        var closeTemplate = closeProbe.Template;
        var closeGlyphLight =
            closeTemplate?.FindName(
                "CloseGlyphLight",
                closeProbe) as System.Windows.Controls.TextBlock;
        var closeHoverTemplateTrigger =
            closeTemplate?.Triggers
                .OfType<System.Windows.Trigger>()
                .FirstOrDefault(trigger =>
                    trigger.Property ==
                        System.Windows.UIElement.IsMouseOverProperty &&
                    Equals(trigger.Value, true));
        var closeEnterStoryboard =
            closeHoverTemplateTrigger?.EnterActions
                .OfType<
                    System.Windows.Media.Animation.BeginStoryboard>()
                .FirstOrDefault()
                ?.Storyboard;
        var closeExitStoryboard =
            closeHoverTemplateTrigger?.ExitActions
                .OfType<
                    System.Windows.Media.Animation.BeginStoryboard>()
                .FirstOrDefault()
                ?.Storyboard;
        var closeEnterAnimations =
            closeEnterStoryboard?.Children
                .OfType<
                    System.Windows.Media.Animation.DoubleAnimation>()
                .ToList() ?? [];
        var closeExitAnimations =
            closeExitStoryboard?.Children
                .OfType<
                    System.Windows.Media.Animation.DoubleAnimation>()
                .ToList() ?? [];
        static System.Windows.Media.Animation.DoubleAnimation?
            CloseAnimation(
                IReadOnlyList<
                    System.Windows.Media.Animation.DoubleAnimation>
                    animations,
                string targetName) =>
            animations.FirstOrDefault(animation =>
                System.Windows.Media.Animation.Storyboard
                    .GetTargetName(animation) == targetName);
        var closeSurfaceEnter = CloseAnimation(
            closeEnterAnimations,
            "CaptionHoverSurface");
        var closeGlyphEnter = CloseAnimation(
            closeEnterAnimations,
            "CloseGlyphLight");
        var closeSurfaceExit = CloseAnimation(
            closeExitAnimations,
            "CaptionHoverSurface");
        var closeGlyphExit = CloseAnimation(
            closeExitAnimations,
            "CloseGlyphLight");
        closeCaptionHoverConfigured =
            closeHoverBrush?.Color ==
                System.Windows.Media.Color.FromRgb(0xFF, 0x52, 0x5C) &&
            closeGlyphLight is not null &&
            closeGlyphLight.Foreground is
                System.Windows.Media.SolidColorBrush glyphBrush &&
            glyphBrush.Color == System.Windows.Media.Colors.White &&
            closeSurfaceEnter?.Duration.HasTimeSpan == true &&
            closeSurfaceEnter.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(220) &&
            closeSurfaceEnter.EasingFunction is
                System.Windows.Media.Animation.CubicEase &&
            closeGlyphEnter?.Duration.HasTimeSpan == true &&
            closeGlyphEnter.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(220) &&
            closeGlyphEnter.EasingFunction is
                System.Windows.Media.Animation.CubicEase &&
            closeSurfaceExit?.Duration.HasTimeSpan == true &&
            closeSurfaceExit.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(180) &&
            closeSurfaceExit.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase &&
            closeGlyphExit?.Duration.HasTimeSpan == true &&
            closeGlyphExit.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(180) &&
            closeGlyphExit.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase;
        smokeWindow.ThemeModeCombo.IsDropDownOpen = true;
        smokeWindow.UpdateLayout();
        var comboPopup =
            smokeWindow.ThemeModeCombo.Template.FindName(
                "PART_Popup",
                smokeWindow.ThemeModeCombo)
            as System.Windows.Controls.Primitives.Popup;
        var comboDropDownScrollViewer =
            FindVisualDescendant<System.Windows.Controls.ScrollViewer>(
                comboPopup?.Child);
        comboDropDownUsesPageScrollStyle =
            comboDropDownScrollViewer?.Style ==
            (System.Windows.Style)application.FindResource(
                "WindowsSettingsScrollViewerStyle") &&
            comboDropDownScrollViewer.CanContentScroll == false;
        smokeWindow.ThemeModeCombo.IsDropDownOpen = false;
        application.Dispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.ContextIdle);
        application.Dispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.ContextIdle);
        MicaWindowService.Apply(smokeWindow, useDarkMode: false);
        acrylicAccentApplied =
            MicaWindowService.LastAcrylicApplySucceeded &&
            MicaWindowService.LastSystemBackdropApplySucceeded &&
            MicaWindowService.LastFrameRefreshSucceeded;
        var transitionHandles = new List<string>
        {
            $"{new System.Windows.Interop.WindowInteropHelper(smokeWindow).Handle}/" +
            $"{MicaWindowService.ActiveBackdropWindowHandle}"
        };
        smokeWindow.WindowState = System.Windows.WindowState.Minimized;
        application.Dispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        transitionHandles.Add(
            $"{new System.Windows.Interop.WindowInteropHelper(smokeWindow).Handle}/" +
            $"{MicaWindowService.ActiveBackdropWindowHandle}");
        smokeWindow.WindowState = System.Windows.WindowState.Normal;
        application.Dispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        transitionHandles.Add(
            $"{new System.Windows.Interop.WindowInteropHelper(smokeWindow).Handle}/" +
            $"{MicaWindowService.ActiveBackdropWindowHandle}");
        smokeWindow.WindowState = System.Windows.WindowState.Maximized;
        application.Dispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        transitionHandles.Add(
            $"{new System.Windows.Interop.WindowInteropHelper(smokeWindow).Handle}/" +
            $"{MicaWindowService.ActiveBackdropWindowHandle}");
        smokeWindow.WindowState = System.Windows.WindowState.Normal;
        application.Dispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        transitionHandles.Add(
            $"{new System.Windows.Interop.WindowInteropHelper(smokeWindow).Handle}/" +
            $"{MicaWindowService.ActiveBackdropWindowHandle}");
        systemBackdropStableDuringWindowTransitions =
            MicaWindowService.ActiveBackdropWindowHandle ==
                new System.Windows.Interop.WindowInteropHelper(
                    smokeWindow).Handle &&
            MicaWindowService.LastAcrylicApplySucceeded;
        systemBackdropTransitionDiagnostics =
            $"最后写入 {MicaWindowService.LastAcrylicApplySucceeded}，" +
            $"窗口句柄 {string.Join(" -> ", transitionHandles)}，" +
            $"错误 {MicaWindowService.LastAcrylicError ?? "无"}";
        quietTimeEditorsConfigured =
            Math.Abs(smokeWindow.DoNotDisturbStartBox.ActualWidth - 86d) <= 0.1d &&
            Math.Abs(smokeWindow.DoNotDisturbEndBox.ActualWidth - 86d) <= 0.1d &&
            Math.Abs(smokeWindow.DoNotDisturbStartBox.ActualHeight - 40d) <= 0.1d &&
            Math.Abs(smokeWindow.DoNotDisturbEndBox.ActualHeight - 40d) <= 0.1d &&
            smokeWindow.DoNotDisturbStartBox.TextAlignment ==
                System.Windows.TextAlignment.Center &&
            smokeWindow.DoNotDisturbEndBox.TextAlignment ==
                System.Windows.TextAlignment.Center &&
            smokeWindow.DoNotDisturbStartBox.VerticalContentAlignment ==
                System.Windows.VerticalAlignment.Center &&
            smokeWindow.DoNotDisturbEndBox.VerticalContentAlignment ==
                System.Windows.VerticalAlignment.Center;
        var deviceListScrollBar =
            FindVisualDescendant<System.Windows.Controls.Primitives.ScrollBar>(
                smokeWindow.DeviceList,
                scrollBar =>
                    scrollBar.Orientation ==
                        System.Windows.Controls.Orientation.Vertical &&
                    scrollBar.Visibility ==
                        System.Windows.Visibility.Visible);
        var settingsScrollBar =
            FindVisualDescendant<System.Windows.Controls.Primitives.ScrollBar>(
                smokeWindow.SettingsPanel,
                scrollBar =>
                    scrollBar.Orientation ==
                        System.Windows.Controls.Orientation.Vertical &&
                    scrollBar.Visibility ==
                        System.Windows.Visibility.Visible);
        deviceListScrollBarVisible = deviceListScrollBar is not null;
        settingsScrollBarVisible = settingsScrollBar is not null;
        deviceListScrollThumbHeight =
            FindVisualDescendant<System.Windows.Controls.Primitives.Thumb>(
                deviceListScrollBar)?.ActualHeight ?? 0d;
        settingsScrollThumbHeight =
            FindVisualDescendant<System.Windows.Controls.Primitives.Thumb>(
                settingsScrollBar)?.ActualHeight ?? 0d;
        var firstDeviceItem =
            smokeWindow.DeviceList.ItemContainerGenerator.ContainerFromIndex(0)
                as System.Windows.Controls.ListBoxItem;
        firstDeviceItem?.ApplyTemplate();
        var deviceHoverMultiTrigger = firstDeviceItem?.Template.Triggers
            .OfType<System.Windows.MultiTrigger>()
            .FirstOrDefault(trigger =>
                trigger.Conditions.Any(condition =>
                    condition.Property ==
                        System.Windows.UIElement.IsMouseOverProperty &&
                    Equals(condition.Value, true)) &&
                trigger.Conditions.Any(condition =>
                    condition.Property ==
                        System.Windows.Controls.ListBoxItem
                            .IsSelectedProperty &&
                    Equals(condition.Value, false)));
        var deviceHoverEnterAnimation =
            deviceHoverMultiTrigger?.EnterActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault(animation =>
                System.Windows.Media.Animation.Storyboard
                    .GetTargetName(animation) == "HoverBorder");
        var deviceHoverExitAnimation =
            deviceHoverMultiTrigger?.ExitActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault(animation =>
                System.Windows.Media.Animation.Storyboard
                    .GetTargetName(animation) == "HoverBorder");
        var deviceHoverBorder =
            firstDeviceItem?.Template.FindName(
                "HoverBorder",
                firstDeviceItem) as System.Windows.Controls.Border;
        var deviceSelectedBorder =
            firstDeviceItem?.Template.FindName(
                "SelectedBorder",
                firstDeviceItem) as System.Windows.Controls.Border;
        var deviceSelectedTrigger =
            firstDeviceItem?.Template.Triggers
                .OfType<System.Windows.Trigger>()
                .FirstOrDefault(trigger =>
                    trigger.Property ==
                        System.Windows.Controls.ListBoxItem
                            .IsSelectedProperty &&
                    Equals(trigger.Value, true));
        var selectedBorderSetter =
            deviceSelectedTrigger?.Setters
                .OfType<System.Windows.Setter>()
                .FirstOrDefault(setter =>
                    setter.TargetName == "SelectedBorder" &&
                    setter.Property ==
                        System.Windows.Controls.Border.OpacityProperty);
        deviceListHoverAnimationConfigured =
            deviceHoverBorder?.Background is
                System.Windows.Media.SolidColorBrush hoverBrush &&
            hoverBrush.Color ==
                (System.Windows.Media.Color)application.Resources[
                    "DeviceListItemSelectedColor"] &&
            deviceSelectedBorder?.Background is
                System.Windows.Media.SolidColorBrush selectedBrush &&
            selectedBrush.Color ==
                (System.Windows.Media.Color)application.Resources[
                    "DeviceListItemSelectedColor"] &&
            Equals(selectedBorderSetter?.Value, 1d) &&
            deviceHoverEnterAnimation?.Duration.HasTimeSpan == true &&
            deviceHoverEnterAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(240) &&
            deviceHoverEnterAnimation.EasingFunction is
                System.Windows.Media.Animation.CubicEase &&
            deviceHoverExitAnimation?.Duration.HasTimeSpan == true &&
            deviceHoverExitAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(200) &&
            deviceHoverExitAnimation.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase;
        if (!string.IsNullOrWhiteSpace(visualQaImagePath))
        {
            var populatingSettingsField = typeof(MainWindow).GetField(
                "_isPopulatingAppSettings",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            populatingSettingsField.SetValue(smokeWindow, true);
            smokeWindow.DoNotDisturbStartBox.Text = "23:55";
            smokeWindow.DoNotDisturbEndBox.Text = "06:00";
            populatingSettingsField.SetValue(smokeWindow, false);
            smokeWindow.SettingsPanel.ScrollToVerticalOffset(
                smokeWindow.SettingsPanel.ScrollableHeight);
            smokeWindow.UpdateLayout();
            RenderWindowToPng(smokeWindow, visualQaImagePath);
            smokeWindow.SettingsPanel.ScrollToTop();
        }
        if (!string.IsNullOrWhiteSpace(darkVisualQaImagePath))
        {
            ThemeService.Apply(ThemeService.DarkMode);
            typeof(MainWindow).GetMethod(
                    "UpdateSelectedDevice",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(smokeWindow, [testDevices[0], true]);
            smokeWindow.SettingsPanel.Visibility = System.Windows.Visibility.Collapsed;
            smokeWindow.EmptyPanel.Visibility = System.Windows.Visibility.Collapsed;
            smokeWindow.DetailPanel.Visibility = System.Windows.Visibility.Visible;
            smokeWindow.Width = 1100;
            smokeWindow.Height = 900;
            smokeWindow.UpdateLayout();
            var detailStack =
                smokeWindow.DetailPanel.Content as
                    System.Windows.Controls.StackPanel;
            var detailTexts =
                detailStack?.Children
                    .OfType<System.Windows.Controls.TextBlock>()
                    .Select((child, index) =>
                        new { Text = child.Text, Index = index })
                    .ToList() ?? [];
            var settingsSectionIndex =
                detailTexts.FirstOrDefault(item =>
                    item.Text == "设备设置")?.Index ?? -1;
            var historySectionIndex =
                detailTexts.FirstOrDefault(item =>
                    item.Text == "上下线历史")?.Index ?? -1;
            detailSectionOrderConfigured =
                settingsSectionIndex >= 0 &&
                historySectionIndex >= 0 &&
                historySectionIndex < settingsSectionIndex;
            deviceProfileControlsHaveMatchingHeight =
                Math.Abs(
                    smokeWindow.CustomNameBox.ActualHeight -
                    smokeWindow.DeviceTypeCombo.ActualHeight) <= 0.1d &&
                Math.Abs(smokeWindow.CustomNameBox.ActualHeight - 48d) <= 0.1d &&
                smokeWindow.CustomNameBox.TextWrapping ==
                    System.Windows.TextWrapping.NoWrap &&
                smokeWindow.CustomNameBox.Padding ==
                    new System.Windows.Thickness(12, 4, 12, 4) &&
                smokeWindow.CustomNameBox.Text == "iPhone 13 Pro Max";
            detailTitleSpacingConfigured =
                smokeWindow.DetailIconColumn.Width.IsAbsolute &&
                Math.Abs(smokeWindow.DetailIconColumn.Width.Value - 74d) <= 0.1d &&
                smokeWindow.DetailIconColumn.Parent is System.Windows.Controls.Grid detailHeader &&
                detailHeader.Children
                    .OfType<System.Windows.Controls.StackPanel>()
                    .Any(panel =>
                        System.Windows.Controls.Grid.GetColumn(panel) == 1 &&
                        panel.Margin.Left == 16d);
            detailIconThemeConfigured =
                smokeWindow.DetailDeviceIconBackground.Background is
                    System.Windows.Media.SolidColorBrush detailIconBrush &&
                detailIconBrush.Color ==
                    (System.Windows.Media.Color)application.Resources["DeviceIconColor"];
            smokeWindow.DetailPanel.ScrollToVerticalOffset(
                smokeWindow.DetailPanel.ScrollableHeight);
            smokeWindow.UpdateLayout();
            RenderWindowToPng(smokeWindow, darkVisualQaImagePath);
            ThemeService.Apply(ThemeService.LightMode);
        }
        smokeWindow.PrepareForExit();
        smokeWindow.Close();

        var selectionWindow = new MainWindow(new NotificationService())
        {
            Left = -10000,
            Top = -10000,
            Width = 900,
            Height = 560,
            ShowActivated = false,
            ShowInTaskbar = false
        };
        var replaceDevicesMethod = typeof(MainWindow).GetMethod(
            "ReplaceDevices",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        replaceDevicesMethod.Invoke(
            selectionWindow,
            [CreateSelectionRaceDevices(), false, false]);
        selectionWindow.Show();
        selectionWindow.UpdateLayout();
        selectionWindow.DeviceList.SelectedIndex = 1;
        replaceDevicesMethod.Invoke(
            selectionWindow,
            [CreateSelectionRaceDevices(), true, false]);
        deviceSelectionStableDuringRefresh =
            selectionWindow.DeviceList.SelectedItem is
                DeviceItemViewModel selectedAfterRefresh &&
            selectedAfterRefresh.MacAddress == "00:00:00:00:10:02";
        replaceDevicesMethod.Invoke(
            selectionWindow,
            [CreateFilterDevices(), false, false]);
        selectionWindow.OfflineDevicesFilterButton.ApplyTemplate();
        var filterButtonTemplate =
            selectionWindow.OfflineDevicesFilterButton.Template;
        var filterHoverScale =
            filterButtonTemplate?.FindName(
                "HoverScale",
                selectionWindow.OfflineDevicesFilterButton) as
                System.Windows.Media.ScaleTransform;
        var filterHoverFloat =
            filterButtonTemplate?.FindName(
                "HoverFloat",
                selectionWindow.OfflineDevicesFilterButton) as
                System.Windows.Media.TranslateTransform;
        var filterPressedSurface =
            filterButtonTemplate?.FindName(
                "PressedSurface",
                selectionWindow.OfflineDevicesFilterButton) as
                System.Windows.Controls.Border;
        static System.Windows.Media.Animation.BeginStoryboard?
            FilterStoryboard(
                System.Windows.Trigger? trigger,
                bool enter)
        {
            var actions = enter
                ? trigger?.EnterActions
                : trigger?.ExitActions;
            return actions?
                .OfType<
                    System.Windows.Media.Animation.BeginStoryboard>()
                .FirstOrDefault();
        }
        static System.Windows.Media.Animation.DoubleAnimation?
            FilterAnimation(
                System.Windows.Media.Animation.Storyboard?
                    storyboard,
                string targetName) =>
            storyboard?.Children
                .OfType<
                    System.Windows.Media.Animation.DoubleAnimation>()
                .FirstOrDefault(animation =>
                    System.Windows.Media.Animation.Storyboard
                        .GetTargetName(animation) == targetName);
        var filterHoverTrigger =
            filterButtonTemplate?.Triggers
                .OfType<System.Windows.Trigger>()
                .FirstOrDefault(trigger =>
                    trigger.Property ==
                        System.Windows.UIElement.IsMouseOverProperty &&
                    Equals(trigger.Value, true));
        var filterPressedTrigger =
            filterButtonTemplate?.Triggers
                .OfType<System.Windows.Trigger>()
                .FirstOrDefault(trigger =>
                    trigger.Property ==
                        System.Windows.Controls.Primitives
                            .ButtonBase.IsPressedProperty &&
                    Equals(trigger.Value, true));
        var filterHoverEnter = FilterAnimation(
            FilterStoryboard(filterHoverTrigger, enter: true)?.Storyboard,
            "HoverScale");
        var filterHoverExit = FilterAnimation(
            FilterStoryboard(filterHoverTrigger, enter: false)?.Storyboard,
            "HoverScale");
        var filterHoverFloatEnter = FilterAnimation(
            FilterStoryboard(filterHoverTrigger, enter: true)?.Storyboard,
            "HoverFloat");
        var filterHoverFloatExit = FilterAnimation(
            FilterStoryboard(filterHoverTrigger, enter: false)?.Storyboard,
            "HoverFloat");
        var filterPressedEnter = FilterAnimation(
            FilterStoryboard(filterPressedTrigger, enter: true)?.Storyboard,
            "PressedSurface");
        var filterPressedExit = FilterAnimation(
            FilterStoryboard(filterPressedTrigger, enter: false)?.Storyboard,
            "PressedSurface");
        filterHoverAndPressConfigured =
            filterHoverScale is not null &&
            filterHoverFloat is not null &&
            filterPressedSurface?.Background is
                System.Windows.Media.SolidColorBrush pressedBrush &&
            pressedBrush.Color.A == 0x22 &&
            filterHoverEnter?.Duration.HasTimeSpan == true &&
            filterHoverEnter.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(220) &&
            filterHoverEnter.EasingFunction is
                System.Windows.Media.Animation.CubicEase &&
            filterHoverFloatEnter?.Duration.HasTimeSpan == true &&
            filterHoverFloatEnter.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(220) &&
            filterHoverFloatEnter.EasingFunction is
                System.Windows.Media.Animation.CubicEase &&
            filterHoverExit?.Duration.HasTimeSpan == true &&
            filterHoverExit.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(180) &&
            filterHoverExit.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase &&
            filterHoverFloatExit?.Duration.HasTimeSpan == true &&
            filterHoverFloatExit.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(180) &&
            filterHoverFloatExit.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase &&
            filterPressedEnter?.Duration.HasTimeSpan == true &&
            filterPressedEnter.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(120) &&
            filterPressedEnter.EasingFunction is
                System.Windows.Media.Animation.CubicEase &&
            filterPressedExit?.Duration.HasTimeSpan == true &&
            filterPressedExit.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(160) &&
            filterPressedExit.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase;
        selectionWindow.OfflineDevicesFilterButton.RaiseEvent(
            new System.Windows.RoutedEventArgs(
                System.Windows.Controls.Button.ClickEvent));
        var offlineTransitionSlide =
            selectionWindow.DeviceListTransition.RenderTransform as
                System.Windows.Media.TranslateTransform;
        var offlineTransitionVisible =
            selectionWindow.DeviceListTransition.Visibility ==
                System.Windows.Visibility.Visible;
        var offlineListSlide =
            selectionWindow.DeviceList.RenderTransform as
                System.Windows.Media.TranslateTransform;
        var offlineDetailTransform =
            selectionWindow.DetailPanel.RenderTransform as
                System.Windows.Media.TransformGroup;
        var offlineDetailScale =
            offlineDetailTransform?.Children
                .OfType<System.Windows.Media.ScaleTransform>()
                .FirstOrDefault();
        var offlineDetailTranslate =
            offlineDetailTransform?.Children
                .OfType<System.Windows.Media.TranslateTransform>()
                .FirstOrDefault();
        var offlineDetailOpacity =
            selectionWindow.DetailPanel.Opacity;
        var offlineSlideOk =
            Math.Abs(
                (offlineTransitionSlide?.X ?? 0d) - 340d) <= 0.5d &&
            Math.Abs(offlineListSlide?.X ?? 999d) <= 0.5d &&
            offlineTransitionVisible;
        var offlineSlideDiag =
            $"离线滑入 X={offlineTransitionSlide?.X}，" +
            $"旧列表 X={offlineListSlide?.X}，可见={offlineTransitionVisible}";
        var offlineDetailOk =
            Math.Abs(
                (offlineDetailScale?.ScaleX ?? 0d) - 1d) <= 0.01d &&
            Math.Abs(offlineDetailTranslate?.X ?? 999d) <= 0.01d &&
            Math.Abs(offlineDetailTranslate?.Y ?? 999d) <= 0.01d &&
            Math.Abs(offlineDetailOpacity - 1d) <= 0.01d;
        var offlineDetailDiag =
            $"离线缩放={offlineDetailScale?.ScaleX}，" +
            $"离线位移={offlineDetailTranslate?.X},{offlineDetailTranslate?.Y}，" +
            $"透明度={offlineDetailOpacity}";
        PumpWpf(
            selectionWindow.Dispatcher,
            TimeSpan.FromMilliseconds(1300));
        var offlineOrder = selectionWindow.DeviceList.Items
            .Cast<DeviceItemViewModel>()
            .Select(device => device.MacAddress)
            .ToList();
        var deviceManagementParent =
            selectionWindow.DeviceManagementSection.Parent as
                System.Windows.Controls.StackPanel;
        offlineDeviceManagementConfigured =
            selectionWindow.DeviceManagementSection.Visibility ==
                System.Windows.Visibility.Visible &&
            deviceManagementParent is not null &&
            deviceManagementParent.Children.IndexOf(
                selectionWindow.DeviceManagementSection) ==
                deviceManagementParent.Children.Count - 1 &&
            selectionWindow.RemoveDeviceButton.Height ==
                selectionWindow.RestartRouterButton.Height &&
            selectionWindow.RemoveDeviceButton.Padding ==
                selectionWindow.RestartRouterButton.Padding &&
            Equals(
                selectionWindow.RemoveDeviceButton.Background,
                selectionWindow.RestartRouterButton.Background);
        selectionWindow.OnlineDevicesFilterButton.RaiseEvent(
            new System.Windows.RoutedEventArgs(
                System.Windows.Controls.Button.ClickEvent));
        var onlineTransitionSlide =
            selectionWindow.DeviceListTransition.RenderTransform as
                System.Windows.Media.TranslateTransform;
        var onlineListSlide =
            selectionWindow.DeviceList.RenderTransform as
                System.Windows.Media.TranslateTransform;
        var onlineDetailTransform =
            selectionWindow.DetailPanel.RenderTransform as
                System.Windows.Media.TransformGroup;
        var onlineDetailScale =
            onlineDetailTransform?.Children
                .OfType<System.Windows.Media.ScaleTransform>()
                .FirstOrDefault();
        var onlineDetailTranslate =
            onlineDetailTransform?.Children
                .OfType<System.Windows.Media.TranslateTransform>()
                .FirstOrDefault();
        var onlineDetailOpacity =
            selectionWindow.DetailPanel.Opacity;
        var onlineSlideOk =
            Math.Abs(
                (onlineTransitionSlide?.X ?? 0d) + 340d) <= 0.5d &&
            Math.Abs(onlineListSlide?.X ?? 999d) <= 0.5d;
        var onlineSlideDiag =
            $"在线滑入 X={onlineTransitionSlide?.X}，" +
            $"旧列表 X={onlineListSlide?.X}";
        var onlineDetailOk =
            Math.Abs(
                (onlineDetailScale?.ScaleX ?? 0d) - 1d) <= 0.01d &&
            Math.Abs(onlineDetailTranslate?.X ?? 999d) <= 0.01d &&
            Math.Abs(onlineDetailTranslate?.Y ?? 999d) <= 0.01d &&
            Math.Abs(onlineDetailOpacity - 1d) <= 0.01d;
        var onlineDetailDiag =
            $"在线缩放={onlineDetailScale?.ScaleX}，" +
            $"在线位移={onlineDetailTranslate?.X},{onlineDetailTranslate?.Y}，" +
            $"透明度={onlineDetailOpacity}";
        listFilterSlideConfigured =
            !System.Windows.SystemParameters.ClientAreaAnimation ||
            (offlineSlideOk && onlineSlideOk);
        listFilterSlideDiagnostics =
            $"{offlineSlideDiag}，{onlineSlideDiag}";
        detailFilterOriginalAnimationConfigured =
            !System.Windows.SystemParameters.ClientAreaAnimation ||
            (offlineDetailOk && onlineDetailOk);
        detailFilterSlideDiagnostics =
            $"{offlineDetailDiag}，{onlineDetailDiag}";
        PumpWpf(
            selectionWindow.Dispatcher,
            TimeSpan.FromMilliseconds(1300));
        var onlineOrder = selectionWindow.DeviceList.Items
            .Cast<DeviceItemViewModel>()
            .Select(device => device.MacAddress)
            .ToList();
        deviceFilterAndSortConfigured =
            offlineOrder.SequenceEqual(
                ["00:00:00:00:30:04", "00:00:00:00:30:03"]) &&
            onlineOrder.SequenceEqual(
                ["00:00:00:00:30:02", "00:00:00:00:30:01"]);
        offlineDeviceManagementConfigured =
            offlineDeviceManagementConfigured &&
            selectionWindow.DeviceManagementSection.Visibility ==
                System.Windows.Visibility.Collapsed;
        selectionWindow.RemoveDeviceButton.RaiseEvent(
            new System.Windows.RoutedEventArgs(
                System.Windows.Controls.Button.ClickEvent));
        var removeConfirmVisible =
            selectionWindow.RemoveDeviceConfirmBorder.Visibility ==
                System.Windows.Visibility.Visible &&
            Math.Abs(
                selectionWindow.RemoveDeviceConfirmBorder.Opacity) <=
                0.01d;
        var removeConfirmTransform =
            selectionWindow.RemoveDeviceConfirmBorder.RenderTransform as
                System.Windows.Media.TransformGroup;
        var removeConfirmScale =
            removeConfirmTransform?.Children
                .OfType<System.Windows.Media.ScaleTransform>()
                .FirstOrDefault();
        var removeConfirmTranslate =
            removeConfirmTransform?.Children
                .OfType<System.Windows.Media.TranslateTransform>()
                .FirstOrDefault();
        var removeConfirmStyled =
            Equals(
                selectionWindow.RemoveDeviceConfirmBorder.Background,
                selectionWindow.FindResource("ConfirmSurfaceBrush")) &&
            Equals(
                selectionWindow.RemoveDeviceConfirmBorder.BorderBrush,
                selectionWindow.FindResource("BorderBrush"));
        var removeConfirmAnimationStarted =
            Math.Abs(
                (removeConfirmScale?.ScaleX ?? 0d) - 0.96d) <= 0.01d &&
            Math.Abs(
                (removeConfirmTranslate?.Y ?? 0d) + 6d) <= 0.01d;
        replaceDevicesMethod.Invoke(
            selectionWindow,
            [CreateFilterDevices(), true, false]);
        var confirmationSurvivesRefresh =
            selectionWindow.RemoveDeviceConfirmBorder.Visibility ==
                System.Windows.Visibility.Visible;
        selectionWindow.DeviceList.SelectedIndex = 1;
        PumpWpf(
            selectionWindow.Dispatcher,
            TimeSpan.FromMilliseconds(700));
        var confirmationDismissedOnDeviceSwitch =
            selectionWindow.RemoveDeviceConfirmBorder.Visibility ==
                System.Windows.Visibility.Collapsed;
        selectionWindow.RemoveDeviceButton.RaiseEvent(
            new System.Windows.RoutedEventArgs(
                System.Windows.Controls.Button.ClickEvent));
        var outsideClickArgs =
            new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice,
                0,
                System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent =
                    System.Windows.UIElement
                        .PreviewMouseLeftButtonDownEvent,
                Source = selectionWindow.OnlineCountText
            };
        selectionWindow.RaiseEvent(outsideClickArgs);
        PumpWpf(
            selectionWindow.Dispatcher,
            TimeSpan.FromMilliseconds(300));
        confirmationBehaviorConfigured =
            removeConfirmVisible &&
            removeConfirmAnimationStarted &&
            removeConfirmStyled &&
            confirmationSurvivesRefresh &&
            confirmationDismissedOnDeviceSwitch &&
            selectionWindow.RemoveDeviceConfirmBorder.Visibility ==
                System.Windows.Visibility.Collapsed;
        selectionWindow.PrepareForExit();
        selectionWindow.Close();

        var menu = new TrayMenuWindow(
            () => trayMenuOpenInvoked = true,
            () => { })
        {
            Left = -10000,
            Top = -10000
        };
        menu.Show();
        menu.UpdateLayout();
        Check(
            menu.OpenIconViewbox.Width == 16d &&
            menu.ExitIconViewbox.Width == 16d,
            "托盘菜单两个矢量图标应使用一致的 16 DIP 画布");
        var openIconCenter = menu.OpenIconViewbox.TranslatePoint(
            new System.Windows.Point(
                menu.OpenIconViewbox.ActualWidth / 2d,
                menu.OpenIconViewbox.ActualHeight / 2d),
            menu);
        var openTextCenter = menu.OpenText.TranslatePoint(
            new System.Windows.Point(
                menu.OpenText.ActualWidth / 2d,
                menu.OpenText.ActualHeight / 2d),
            menu);
        trayOpenIconTextAligned =
            Math.Abs(openIconCenter.Y - openTextCenter.Y) <= 0.5d;
        var trayHoverTrigger = menu.OpenButton.Template.Triggers
            .OfType<System.Windows.Trigger>()
            .FirstOrDefault(trigger =>
                trigger.Property == System.Windows.UIElement.IsMouseOverProperty &&
                Equals(trigger.Value, true));
        var trayHoverAnimation = trayHoverTrigger?.EnterActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault();
        trayMenuHoverAnimationConfigured =
            trayHoverAnimation?.Duration.HasTimeSpan == true &&
            trayHoverAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(180) &&
            trayHoverAnimation.EasingFunction is
                System.Windows.Media.Animation.CubicEase;
        menu.OpenButton.ApplyTemplate();
        var trayHoverSurface = menu.OpenButton.Template.FindName(
            "HoverSurface",
            menu.OpenButton) as System.Windows.Controls.Border;
        trayHoverColorAndSpacingConfigured =
            trayHoverSurface?.Background is
                System.Windows.Media.SolidColorBrush trayHoverBrush &&
            trayHoverBrush.Color ==
                System.Windows.Media.Color.FromArgb(0x30, 0x60, 0x60, 0x60) &&
            Math.Abs(menu.OpenText.Margin.Left - 8d) <= 0.1d;
        if (!string.IsNullOrWhiteSpace(trayVisualQaImagePath))
        {
            RenderWindowToPng(menu, trayVisualQaImagePath);
        }
        var openMethod = typeof(TrayMenuWindow).GetMethod(
            "OpenButton_Click",
            BindingFlags.Instance | BindingFlags.NonPublic);
        openMethod!.Invoke(
            menu,
            [menu, new System.Windows.RoutedEventArgs()]);
        application.Shutdown();
    }
    catch (TargetInvocationException exception)
    {
        trayMenuOpenException = exception.InnerException ?? exception;
    }
    catch (Exception exception)
    {
        trayMenuOpenException = exception;
    }
});
trayMenuThread.SetApartmentState(ApartmentState.STA);
trayMenuThread.Start();
trayMenuThread.Join(TimeSpan.FromSeconds(20));
Check(
    !trayMenuThread.IsAlive &&
    trayMenuOpenException is null &&
    trayMenuOpenInvoked,
    $"主窗口启动与托盘菜单打开不应触发异常：{trayMenuOpenException}");
Check(
    verticalScrollBarVisible &&
    verticalScrollBarWidth >= 10d &&
    verticalScrollBarMaximum > 0d &&
    verticalScrollBarViewport > 0d &&
    verticalScrollThumbVisible,
    $"内容溢出时应显示 Windows 设置风格细滚动条：可见={verticalScrollBarVisible}，宽度={verticalScrollBarWidth:0.##}，最大值={verticalScrollBarMaximum:0.##}，视口={verticalScrollBarViewport:0.##}，滑块高度={verticalScrollThumbHeight:0.##}，最小高度={verticalScrollThumbMinimumHeight:0.##}");
Check(
    scrollThumbDragAnimationConfigured,
    "滚动条按住与松开应分别使用 300 ms 的二次缓入和缓出动画");
Check(
    scrollThumbHoverAnimationConfigured,
    "滚动条悬停与非悬停应分别使用 300 ms 的非线性动画");
Check(
    deviceListScrollBarVisible &&
    settingsScrollBarVisible &&
    deviceListScrollThumbHeight >= 28d &&
    settingsScrollThumbHeight >= 28d,
    $"主窗口左右板块内容溢出时都应显示具有实际高度的细滚动条：左侧可见={deviceListScrollBarVisible}，左侧滑块={deviceListScrollThumbHeight:0.##}，右侧可见={settingsScrollBarVisible}，右侧滑块={settingsScrollThumbHeight:0.##}");
Check(
    deviceListHoverAnimationConfigured,
    "左侧设备条块的悬停与非悬停状态应使用非线性动画");
Check(
    trayMenuHoverAnimationConfigured,
    "托盘菜单的两个选项应分别使用独立的非线性悬停动画");
Check(
    trayHoverColorAndSpacingConfigured,
    "托盘菜单选项悬停层应加深至 18.8% 不透明度，图标与文字间距应增加 8 DIP");
Check(
    trayOpenIconTextAligned,
    "托盘菜单“打开主界面”的图标与文字应在同一垂直中心线上");
Check(
    themePaletteConfigured,
    "浅深色材质、下拉栏、时间轴与滚动条颜色资源应使用指定的不透明度和主题值");
Check(
    topButtonToolTipsReadableInDarkMode,
    "深色模式下路由器与设置按钮的提示框应使用可读的深色背景与文字");
Check(
    closeCaptionHoverConfigured,
    "关闭按钮悬停态应使用低饱和红色、浅色叉形图标，并以同步动画过渡");
Check(
    detailSectionOrderConfigured,
    "设备详情页中设备设置区域应位于上下线历史区域之后");
Check(
    roundedControlGeometryMatchesDev48,
    "输入框、密码框、选择框、下拉框与卡片的大小比例应与 v0.1.0-dev.48 一致");
Check(
    timelineDurationBadgeBorderConfigured,
    "在线时长徽标应使用 1 DIP 的主题自适应细描边");
Check(
    comboDropDownUsesPageScrollStyle,
    "所有下拉栏应复用普通页面的细滚动条与平滑滚动样式，并以 DIP 而不是条目数滚动");
Check(
    quietTimeEditorsConfigured,
    "免打扰开始与结束时间输入框应同为 86 x 40 DIP，且时间文字水平、垂直居中");
Check(
    deviceProfileControlsHaveMatchingHeight,
    "设备自定义名称输入框应保持完整单行显示，并与设备类型选择框同为 48 DIP 高度");
Check(
    detailTitleSpacingConfigured,
    "设备详情标题文字应在原位置基础上向右移动 8 DIP");
Check(
    detailIconThemeConfigured,
    "深色模式右侧详情设备图标应与左侧设备列表共用适配主题的背景资源");
Check(
    acrylicAccentApplied,
    $"Windows 应实际接受不会随失焦自动切换为回退色的持久 Acrylic 材质写入：{MicaWindowService.LastAcrylicError}");
Check(
    systemBackdropStableDuringWindowTransitions,
    $"最小化、还原和最大化期间应保持同一个持久 Acrylic 背景，且不创建覆盖 WPF 内容的独立合成目标（{systemBackdropTransitionDiagnostics}）");
Check(
    dangerConfirmButtonConfigured,
    "确认移除与确认重启按钮应使用低饱和红色底色和白色文字");
Check(
    deviceSelectionStableDuringRefresh,
    "用户选择新设备后，实时数据刷新不应把左侧选择抢回旧设备");
Check(
    deviceFilterAndSortConfigured,
    "在线与离线统计卡应分别筛选设备，并按最近上线或离线时间倒序排列");
Check(
    filterHoverAndPressConfigured,
    "在线与离线统计卡悬停时应轻微放大上浮，并按非线性动画过渡");
Check(
    listFilterSlideConfigured,
    $"在线与离线切换时设备列表应按按钮左右方向水平滑动（{listFilterSlideDiagnostics}）");
Check(
    detailFilterOriginalAnimationConfigured,
    $"在线与离线切换导致设备详情变化时，右侧详情面板应使用原有切换动画（{detailFilterSlideDiagnostics}）");
Check(
    offlineDeviceManagementConfigured,
    "设备管理栏应位于上下线历史下方、只对离线设备显示，并复用重启路由器按钮样式");
Check(
    confirmationBehaviorConfigured,
    "确认框应使用更亮的圆角矩形样式、非线性弹出动画，并支持点击外部自动收回");
Check(
    WindowPlacementService.HasVisibleArea(
        new SavedWindowPlacement(120, 80, 1300, 840, false),
        [new System.Drawing.Rectangle(0, 0, 1920, 1040)]),
    "位于工作区内的窗口布局应当可以恢复");
Check(
    !WindowPlacementService.HasVisibleArea(
        new SavedWindowPlacement(-2200, 80, -1020, 840, false),
        [new System.Drawing.Rectangle(0, 0, 1920, 1040)]),
    "已离开所有屏幕的窗口布局不应恢复");

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

Console.WriteLine($"自检通过：设备识别、单位换算、通知时段、受控滚动、细滚动条、版本显示、主窗口启动、托盘菜单与窗口布局共 {checksRun} 项；路由器模式另验证上网与 Mesh 信息。");
return 0;

void Check(bool condition, string message)
{
    checksRun++;
    if (!condition)
    {
        failures.Add(message);
    }
}

async Task<bool> MonitoringLoopRunsOffUiContextAsync()
{
    var loopType = typeof(MonitoringService).Assembly.GetType(
        "LanDeviceMonitor.Services.MonitoringLoop");
    if (loopType is null)
    {
        return false;
    }

    var interval = TimeSpan.FromMilliseconds(100);
    var firstPollStarted = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var secondPollStarted = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    using var cancellation = new CancellationTokenSource();
    var synchronizationContexts = new List<SynchronizationContext?>();
    var firstPollFinishedAt = Stopwatch.GetTimestamp();
    var secondPollStartedAt = Stopwatch.GetTimestamp();
    var pollCount = 0;

    var poll = new Func<CancellationToken, Task>(async token =>
    {
        lock (synchronizationContexts)
        {
            synchronizationContexts.Add(SynchronizationContext.Current);
        }

        if (Interlocked.Increment(ref pollCount) == 1)
        {
            firstPollStarted.TrySetResult(true);
            await Task.Delay(TimeSpan.FromMilliseconds(150), token).ConfigureAwait(false);
            firstPollFinishedAt = Stopwatch.GetTimestamp();
            return;
        }

        secondPollStartedAt = Stopwatch.GetTimestamp();
        secondPollStarted.TrySetResult(true);
        cancellation.Cancel();
    });

    var loop = Activator.CreateInstance(loopType, interval, poll);
    var runMethod = loopType.GetMethod("RunAsync");
    if (loop is null || runMethod is null)
    {
        return false;
    }

    var previousContext = SynchronizationContext.Current;
    SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
    try
    {
        var runTask = (Task?)runMethod.Invoke(loop, [cancellation.Token]);
        if (runTask is null)
        {
            return false;
        }

        await firstPollStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await secondPollStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            await runTask;
        }
        catch (OperationCanceledException)
        {
        }

        var delayAfterFirstPoll =
            (secondPollStartedAt - firstPollFinishedAt) / (double)Stopwatch.Frequency;
        lock (synchronizationContexts)
        {
            return synchronizationContexts.Count >= 2 &&
                   synchronizationContexts.All(context => context is null) &&
                   delayAfterFirstPoll >= interval.TotalSeconds * 0.75d;
        }
    }
    finally
    {
        SynchronizationContext.SetSynchronizationContext(previousContext);
        cancellation.Cancel();
    }
}

T? FindVisualDescendant<T>(
    System.Windows.DependencyObject? root,
    Func<T, bool>? predicate = null)
    where T : System.Windows.DependencyObject
{
    if (root is null)
    {
        return null;
    }

    for (var index = 0;
         index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
         index++)
    {
        var child =
            System.Windows.Media.VisualTreeHelper.GetChild(root, index);
        if (child is T match && (predicate is null || predicate(match)))
        {
            return match;
        }

        var descendant = FindVisualDescendant(child, predicate);
        if (descendant is not null)
        {
            return descendant;
        }
    }

    return null;
}

IReadOnlyList<LanDeviceMonitor.Models.DeviceViewData>
    CreateSelectionRaceDevices() =>
[
    new LanDeviceMonitor.Models.DeviceViewData
    {
        Profile = new LanDeviceMonitor.Models.DeviceProfile
        {
            MacAddress = "00:00:00:00:10:01",
            LastRouterName = "旧设备",
            LastKnownOnline = true,
            LastConnectionType = 2
        },
        Identity = new LanDeviceMonitor.Models.DeviceIdentity(
            "手机",
            "\uE8EA",
            "旧设备"),
        Sessions = []
    },
    new LanDeviceMonitor.Models.DeviceViewData
    {
        Profile = new LanDeviceMonitor.Models.DeviceProfile
        {
            MacAddress = "00:00:00:00:10:02",
            LastRouterName = "新设备",
            LastKnownOnline = true,
            LastConnectionType = 2
        },
        Identity = new LanDeviceMonitor.Models.DeviceIdentity(
            "手机",
            "\uE8EA",
            "新设备"),
        Sessions = []
    }
];

IReadOnlyList<LanDeviceMonitor.Models.DeviceViewData>
    CreateFilterDevices() =>
[
    CreateFilterDevice(
        "00:00:00:00:30:01",
        true,
        new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.FromHours(8)),
        null),
    CreateFilterDevice(
        "00:00:00:00:30:02",
        true,
        new DateTimeOffset(2026, 7, 30, 11, 0, 0, TimeSpan.FromHours(8)),
        null),
    CreateFilterDevice(
        "00:00:00:00:30:03",
        false,
        new DateTimeOffset(2026, 7, 29, 9, 0, 0, TimeSpan.FromHours(8)),
        new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.FromHours(8))),
    CreateFilterDevice(
        "00:00:00:00:30:04",
        false,
        new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.FromHours(8)),
        new DateTimeOffset(2026, 7, 30, 10, 0, 0, TimeSpan.FromHours(8)))
];

LanDeviceMonitor.Models.DeviceViewData CreateFilterDevice(
    string macAddress,
    bool isOnline,
    DateTimeOffset onlineAt,
    DateTimeOffset? offlineAt) =>
    new()
    {
        Profile = new LanDeviceMonitor.Models.DeviceProfile
        {
            MacAddress = macAddress,
            LastRouterName = macAddress,
            LastKnownOnline = isOnline,
            FirstSeenAt = onlineAt,
            LastSeenAt = offlineAt ?? onlineAt,
            LastConnectionType = 2
        },
        Identity = new LanDeviceMonitor.Models.DeviceIdentity(
            "手机",
            "\uE8EA",
            macAddress),
        Sessions =
        [
            new LanDeviceMonitor.Models.DeviceSession
            {
                MacAddress = macAddress,
                OnlineAt = onlineAt,
                OfflineAt = offlineAt
            }
        ]
    };

object GetStyleSetterValue(
    System.Windows.Style style,
    System.Windows.DependencyProperty property)
{
    return style.Setters
        .OfType<System.Windows.Setter>()
        .Single(setter => setter.Property == property)
        .Value;
}

void RenderWindowToPng(
    System.Windows.Window window,
    string outputPath)
{
    window.UpdateLayout();
    var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
    var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
    var target = new System.Windows.Media.Imaging.RenderTargetBitmap(
        width,
        height,
        96,
        96,
        System.Windows.Media.PixelFormats.Pbgra32);
    target.Render(window);
    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
    encoder.Frames.Add(
        System.Windows.Media.Imaging.BitmapFrame.Create(target));
    var directory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    using var stream = File.Create(outputPath);
    encoder.Save(stream);
}

void PumpWpf(
    System.Windows.Threading.Dispatcher dispatcher,
    TimeSpan duration)
{
    var frame = new System.Windows.Threading.DispatcherFrame();
    var timer = new System.Windows.Threading.DispatcherTimer(
        System.Windows.Threading.DispatcherPriority.Background,
        dispatcher)
    {
        Interval = duration
    };
    timer.Tick += (_, _) =>
    {
        timer.Stop();
        frame.Continue = false;
    };
    timer.Start();
    System.Windows.Threading.Dispatcher.PushFrame(frame);
}

int RunHotKeyRegistrationSmoke()
{
    using var first = new EmergencyHotKeyService(() => { });
    using var second = new EmergencyHotKeyService(() => { });
    _ = first.Start();
    _ = second.Start();
    Console.WriteLine(
        $"紧急退出快捷键注册冒烟：第一注册={first.Status}，第二注册={second.Status}");

    var firstStatusIsExpected = first.Status is
        EmergencyHotKeyRegistrationStatus.Registered or
        EmergencyHotKeyRegistrationStatus.Conflict;
    var secondStatusIsExpected = second.Status is
        EmergencyHotKeyRegistrationStatus.Conflict or
        EmergencyHotKeyRegistrationStatus.Registered;
    var duplicateWasRejected =
        first.Status != EmergencyHotKeyRegistrationStatus.Registered ||
        second.Status == EmergencyHotKeyRegistrationStatus.Conflict;
    return firstStatusIsExpected && secondStatusIsExpected && duplicateWasRejected
        ? 0
        : 1;
}

int RunStartupSmoke()
{
    Exception? startupException = null;
    var windowShown = false;
    var firstFrameMaterialReady = false;
    var thread = new Thread(() =>
    {
        LanDeviceMonitor.App? application = null;
        try
        {
            application = new LanDeviceMonitor.App();
            application.DispatcherUnhandledException += (_, eventArgs) =>
            {
                startupException = eventArgs.Exception;
                eventArgs.Handled = true;
                application.Shutdown(2);
            };
            application.InitializeComponent();
            var activationTimer = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromSeconds(1.5),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                async (sender, _) =>
                {
                    ((System.Windows.Threading.DispatcherTimer)sender!).Stop();
                    await SingleInstanceService.SignalPrimaryAsync();
                },
                application.Dispatcher);
            var timer = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromSeconds(4),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                (_, _) =>
                {
                    windowShown = application.Windows
                        .OfType<MainWindow>()
                        .Any(window => window.IsVisible);
                    firstFrameMaterialReady =
                        MicaWindowService.LastAcrylicApplySucceeded &&
                        MicaWindowService.LastSystemBackdropApplySucceeded &&
                        MicaWindowService.LastFrameRefreshSucceeded;
                    application.Shutdown();
                },
                application.Dispatcher);
            activationTimer.Start();
            timer.Start();
            application.Run();
        }
        catch (Exception exception)
        {
            startupException = exception;
            application?.Shutdown(2);
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join(TimeSpan.FromSeconds(55));

    if (thread.IsAlive)
    {
        Console.Error.WriteLine("启动冒烟测试超时，应用未能在预期时间内退出。");
        return 1;
    }

    if (startupException is not null)
    {
        Console.Error.WriteLine($"启动冒烟测试异常：{startupException}");
        return 1;
    }

    if (!windowShown)
    {
        Console.Error.WriteLine("启动冒烟测试未检测到可见主窗口。");
        return 1;
    }

    if (!firstFrameMaterialReady)
    {
        Console.Error.WriteLine(
            "启动冒烟测试未检测到首次显示时的 Acrylic 与窗口框架刷新。");
        return 1;
    }

    Console.WriteLine(
        "启动冒烟测试通过：主窗口首次显示即完成 Acrylic 与窗口框架刷新。");
    return 0;
}

async Task<int> RunTrafficDiagnosticAsync()
{
    var credentialService = new RouterCredentialService();
    var password = credentialService.ReadPassword("10.77.0.1");
    if (string.IsNullOrWhiteSpace(password))
    {
        Console.Error.WriteLine("流量诊断无法开始：Windows 凭据管理器中没有路由器密码。");
        return 1;
    }

    using var client = new XiaomiRouterClient();
    client.Configure("10.77.0.1", password);
    var counters = new Dictionary<string, long[]>(StringComparer.OrdinalIgnoreCase);
    var profiles = new Dictionary<string, DeviceProfile>(StringComparer.OrdinalIgnoreCase);
    var diagnosticStatePath = Path.Combine(
        Path.GetTempPath(),
        "LanDeviceMonitor.Tests",
        $"traffic-diagnostic-{Guid.NewGuid():N}",
        "state.json");
    var diagnosticRepository = new DeviceRepository(diagnosticStatePath);
client.TrafficSampleObserved += (_, sample) =>
{
        if (!counters.TryGetValue(sample.MacAddress, out var values))
        {
            values = new long[8];
            counters[sample.MacAddress] = values;
        }

        values[0]++;
        if (sample.DeviceListUploadSpeed > 0 && sample.StatusUploadSpeed == 0)
        {
            values[1]++;
        }

        if (sample.DeviceListDownloadSpeed > 0 && sample.StatusDownloadSpeed == 0)
        {
            values[2]++;
        }

        if (sample.UploadSpeed == 0)
        {
            values[3]++;
        }

        if (sample.DownloadSpeed == 0)
        {
            values[4]++;
        }

        values[5] = Math.Max(values[5], sample.UploadSpeed);
        values[6] = Math.Max(values[6], sample.DownloadSpeed);

        if (sample.MacAddress.Equals(
                "02:00:00:00:00:01",
                StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                $"{sample.Timestamp:HH:mm:ss} 测试设备3原始值：设备列表 ↑{FormatOptionalRate(sample.DeviceListUploadSpeed)} ↓{FormatOptionalRate(sample.DeviceListDownloadSpeed)}，"
                + $"累计 ↑{FormatOptionalBytes(sample.DeviceListTotalUpload)} ↓{FormatOptionalBytes(sample.DeviceListTotalDownload)}，"
                + $"status ↑{sample.StatusUploadSpeed?.ToString("N0") ?? "缺失"} ↓{sample.StatusDownloadSpeed?.ToString("N0") ?? "缺失"}，"
                + $"累计 ↑{FormatOptionalBytes(sample.StatusTotalUpload)} ↓{FormatOptionalBytes(sample.StatusTotalDownload)}，"
                + $"合并 ↑{sample.UploadSpeed:N0} ↓{sample.DownloadSpeed:N0}，"
                + $"累计 ↑{sample.TotalUpload:N0} ↓{sample.TotalDownload:N0}");
        }
    };

    await client.AuthenticateAsync();
    Console.WriteLine("开始流量原始采样诊断（20 秒）…");
    var startedAt = DateTimeOffset.UtcNow;
    var sampleRounds = 0;
    while (DateTimeOffset.UtcNow - startedAt < TimeSpan.FromSeconds(20))
    {
        var snapshots = await client.GetDeviceSnapshotsAsync();
        var update = await diagnosticRepository.ApplyPollAsync(snapshots, DateTimeOffset.Now);
        foreach (var device in update.Devices)
        {
            profiles[device.Profile.MacAddress] = device.Profile;
        }
        sampleRounds++;
        profiles.TryGetValue("02:00:00:00:00:01", out var targetProfile);
        Console.WriteLine(
            $"第 {sampleRounds,2} 次：{snapshots.Count} 台设备，"
            + $"总上传 {snapshots.Sum(item => item.UploadSpeedBytes):N0} B/s，"
            + $"总下载 {snapshots.Sum(item => item.DownloadSpeedBytes):N0} B/s，"
            + $"测试设备3最终 ↑{targetProfile?.LastUploadSpeedBytes:N0} ↓{targetProfile?.LastDownloadSpeedBytes:N0}");
        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    Console.WriteLine("--- 原始接口合并统计（总采样 / 主接口正值但 status=0 / 合并后上传为 0 / 合并后下载为 0）---");
    foreach (var entry in counters.OrderByDescending(item => item.Value[5]))
    {
        var values = entry.Value;
        Console.WriteLine(
            $"{entry.Key}: {values[0]} / {values[1]} / {values[3]} / {values[4]}，"
            + $"峰值 {values[5]:N0} B/s ↑、{values[6]:N0} B/s ↓");
    }

    var diagnosticDirectory = Path.GetDirectoryName(diagnosticStatePath);
    if (!string.IsNullOrWhiteSpace(diagnosticDirectory) &&
        Directory.Exists(diagnosticDirectory))
    {
        Directory.Delete(diagnosticDirectory, recursive: true);
    }

    return 0;
}

string FormatOptionalRate(long? value) =>
    value?.ToString("N0") ?? "缺失";

string FormatOptionalBytes(long? value) =>
    value?.ToString("N0") ?? "缺失";
