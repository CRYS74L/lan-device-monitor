using System.IO;
using System.Text.Json;
using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Services;

public sealed class DeviceRepository
{
    // 小米设备接口通常按数秒节奏刷新；在这段窗口内返回的 0 可能只是
    // 同一轮计数快照尚未生成，而不是设备真的停止了传输。
    private static readonly TimeSpan PlaceholderZeroGracePeriod = TimeSpan.FromSeconds(8);

    // 设备累计字节会以约数秒为单位批量更新，偶尔还会延迟一两轮；在确认
    // 空闲前保留最近可信速度，不能把尚未更新的同一份累计值误绘成归零点。
    private static readonly TimeSpan CumulativeTrafficIdleConfirmationPeriod =
        TimeSpan.FromSeconds(20);

    // Xiaomi 的 devicelist 偶尔会在一次成功返回中遗漏已在线设备，或将
    // online 暂时写成 0。必须连续观察到反向证据一个确认窗口，才能把它
    // 当作真正离线；否则会产生用户截图中的单点垂直归零和误通知。
    private static readonly TimeSpan RouterOfflineConfirmationPeriod =
        TimeSpan.FromSeconds(15);

    // 累计字节批量更新时，差分除以本轮观察间隔会形成不真实尖峰。正的
    // 瞬时字段与上一轮可信速度都能作为保护基准；它们不能单独取代累计值。
    private const double MaximumDerivedToInstantaneousRatio = 2.5;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _statePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private PersistedState _state = new();

    public DeviceRepository()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanDeviceMonitor");
        _statePath = Path.Combine(dataDirectory, "state.json");
    }

    internal DeviceRepository(string statePath)
    {
        _statePath = statePath;
    }

    public AppSettings Settings => _state.Settings;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_statePath))
            {
                return;
            }

            await using (var stream = File.OpenRead(_statePath))
            {
                _state = await JsonSerializer.DeserializeAsync<PersistedState>(
                    stream,
                    _jsonOptions,
                    cancellationToken) ?? new PersistedState();
            }

            _state.Devices = new Dictionary<string, DeviceProfile>(
                _state.Devices,
                StringComparer.OrdinalIgnoreCase);
            _state.Settings.ThemeMode = ThemeService.NormalizeMode(_state.Settings.ThemeMode);

            var stateChanged = false;
            foreach (var profile in _state.Devices.Values)
            {
                if (profile.CustomType == "其他设备" &&
                    profile.CustomIconGlyph == "\uE7F4")
                {
                    profile.CustomIconGlyph = "\uE772";
                    stateChanged = true;
                }

                if (profile.CustomType == "灯" &&
                    profile.CustomIconGlyph == "\uE781")
                {
                    profile.CustomIconGlyph = "\uEA80";
                    stateChanged = true;
                }

                if (_state.SchemaVersion < 3)
                {
                    var notifyWhenStatusChanges =
                        profile.NotifyWhenOnline ||
                        profile.NotifyWhenOffline;
                    profile.NotifyWhenOnline = notifyWhenStatusChanges;
                    profile.NotifyWhenOffline = notifyWhenStatusChanges;
                    stateChanged = true;
                }
            }

            if (!_state.Settings.CloseToTray)
            {
                _state.Settings.CloseToTray = true;
                stateChanged = true;
            }

            var startMinutes = Math.Clamp(
                _state.Settings.DoNotDisturbStartMinutes,
                0,
                1439);
            var endMinutes = Math.Clamp(
                _state.Settings.DoNotDisturbEndMinutes,
                0,
                1439);
            if (startMinutes != _state.Settings.DoNotDisturbStartMinutes ||
                endMinutes != _state.Settings.DoNotDisturbEndMinutes ||
                _state.SchemaVersion < 2)
            {
                _state.Settings.DoNotDisturbStartMinutes = startMinutes;
                _state.Settings.DoNotDisturbEndMinutes = endMinutes;
                _state.SchemaVersion = 2;
                stateChanged = true;
            }

            if (_state.SchemaVersion < 3)
            {
                _state.SchemaVersion = 3;
                stateChanged = true;
            }

            if (_state.SchemaVersion < 4)
            {
                _state.SchemaVersion = 4;
                stateChanged = true;
            }

            if (_state.SchemaVersion < 5)
            {
                _state.Settings.RefreshIntervalSeconds = 1;
                _state.SchemaVersion = 5;
                stateChanged = true;
            }
            else if (_state.Settings.RefreshIntervalSeconds is < 1 or > 60)
            {
                _state.Settings.RefreshIntervalSeconds = 1;
                stateChanged = true;
            }

            if (_state.SchemaVersion < 6)
            {
                foreach (var profile in _state.Devices.Values)
                {
                    // 旧 state.json 没有明确在线时间；保留已有的最后可见
                    // 时间作为历史基线，真正的离线确认仍从本进程的连续证据
                    // 开始，避免升级后第一轮错误快照立刻触发离线。
                    if (profile.LastKnownOnline && profile.LastOnlineSeenAt == default)
                    {
                        profile.LastOnlineSeenAt = profile.LastSeenAt;
                    }
                }

                _state.SchemaVersion = 6;
                stateChanged = true;
            }

            if (stateChanged)
            {
                await SaveCoreAsync(cancellationToken);
            }
        }
        catch (JsonException)
        {
            var backupPath = $"{_statePath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(_statePath, backupPath, overwrite: false);
            _state = new PersistedState();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MonitorUpdate> ApplyPollAsync(
        IReadOnlyList<DeviceSnapshot> snapshots,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var transitions = new List<DeviceTransition>();
            var reportedOnlineMacs = snapshots
                .Where(snapshot => snapshot.IsOnline)
                .Select(snapshot => snapshot.MacAddress)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var reportedMacs = snapshots
                .Select(snapshot => snapshot.MacAddress)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var snapshot in snapshots)
            {
                if (!_state.Devices.TryGetValue(snapshot.MacAddress, out var profile))
                {
                    profile = new DeviceProfile
                    {
                        MacAddress = snapshot.MacAddress,
                        FirstSeenAt = now,
                        LastSeenAt = now
                    };
                    _state.Devices[snapshot.MacAddress] = profile;
                }

                var wasOnline = profile.HasObservedState && profile.LastKnownOnline;
                var previousUploadTotal = profile.LastTotalUploadBytes;
                var previousDownloadTotal = profile.LastTotalDownloadBytes;
                var previousUploadTotalChangedAt = profile.LastUploadTotalChangedAt;
                var previousDownloadTotalChangedAt = profile.LastDownloadTotalChangedAt;

                var identity = DeviceIdentityService.Identify(
                    snapshot.RouterName,
                    snapshot.OriginalName,
                    snapshot.ConnectionType);
                var displayName = GetDisplayName(profile, identity);

                bool effectiveOnline;
                if (!profile.HasObservedState)
                {
                    profile.HasObservedState = true;
                    profile.LastKnownOnline = snapshot.IsOnline;
                    profile.OfflineEvidenceStartedAt = default;
                    effectiveOnline = snapshot.IsOnline;
                    if (snapshot.IsOnline)
                    {
                        var estimatedStart = now - TimeSpan.FromSeconds(
                            Math.Max(0, snapshot.OnlineDurationSeconds));
                        AddOpenSessionIfMissing(snapshot.MacAddress, estimatedStart);
                        profile.FirstSeenAt = estimatedStart;
                        profile.LastOnlineSeenAt = now;
                    }
                }
                else
                {
                    effectiveOnline = ResolveEffectiveOnlineState(
                        profile,
                        snapshot.IsOnline,
                        now,
                        displayName,
                        transitions);
                }

                if (!wasOnline && snapshot.IsOnline)
                {
                    // 新一轮上线时不把离线期间的累计增量误算为当前速度。
                    previousUploadTotalChangedAt = default;
                    previousDownloadTotalChangedAt = default;
                }

                profile.LastSeenAt = now;
                profile.LastRouterName = snapshot.RouterName;
                profile.LastOriginalName = snapshot.OriginalName;
                profile.LastIpAddress = snapshot.IpAddress;
                profile.LastConnectionType = snapshot.ConnectionType;

                // raw online=false 的这一帧没有资格改写流量数据，即便我们
                // 仍在状态确认窗口内把设备逻辑上保持在线。这样短暂的路由器
                // 误报既不会清零速度，也不会污染累计字节差分的时间基准。
                if (!effectiveOnline || !snapshot.IsOnline)
                {
                    continue;
                }

                if (snapshot.HasValidTotalUploadData)
                {
                    profile.LastTotalUploadBytes = ApplyOnlineTotalSample(
                        profile.LastTotalUploadBytes,
                        snapshot.TotalUploadBytes,
                        effectiveOnline);
                }

                if (snapshot.HasValidTotalDownloadData)
                {
                    profile.LastTotalDownloadBytes = ApplyOnlineTotalSample(
                        profile.LastTotalDownloadBytes,
                        snapshot.TotalDownloadBytes,
                        effectiveOnline);
                }

                var derivedUploadSpeed = CalculateDerivedSpeed(
                    previousUploadTotal,
                    profile.LastTotalUploadBytes,
                    previousUploadTotalChangedAt,
                    now,
                    effectiveOnline && snapshot.HasValidTotalUploadData,
                    out var lastUploadTotalChangedAt);
                var derivedDownloadSpeed = CalculateDerivedSpeed(
                    previousDownloadTotal,
                    profile.LastTotalDownloadBytes,
                    previousDownloadTotalChangedAt,
                    now,
                    effectiveOnline && snapshot.HasValidTotalDownloadData,
                    out var lastDownloadTotalChangedAt);
                profile.LastUploadTotalChangedAt = lastUploadTotalChangedAt;
                profile.LastDownloadTotalChangedAt = lastDownloadTotalChangedAt;

                profile.LastUploadSpeedBytes = ApplyOnlineSpeedSample(
                    profile.LastUploadSpeedBytes,
                    snapshot.HasValidUploadSpeedData ? snapshot.UploadSpeedBytes : 0,
                    snapshot.HasValidTotalUploadData,
                    effectiveOnline,
                    derivedUploadSpeed,
                    profile.LastUploadTotalChangedAt,
                    now,
                    ref profile.LastUploadPositiveSampleAt);
                profile.LastDownloadSpeedBytes = ApplyOnlineSpeedSample(
                    profile.LastDownloadSpeedBytes,
                    snapshot.HasValidDownloadSpeedData ? snapshot.DownloadSpeedBytes : 0,
                    snapshot.HasValidTotalDownloadData,
                    effectiveOnline,
                    derivedDownloadSpeed,
                    profile.LastDownloadTotalChangedAt,
                    now,
                    ref profile.LastDownloadPositiveSampleAt);
            }

            foreach (var profile in _state.Devices.Values)
            {
                if (!profile.HasObservedState ||
                    !profile.LastKnownOnline ||
                    reportedOnlineMacs.Contains(profile.MacAddress) ||
                    reportedMacs.Contains(profile.MacAddress))
                {
                    continue;
                }

                var identity = DeviceIdentityService.Identify(
                    profile.LastRouterName,
                    profile.LastOriginalName,
                    profile.LastConnectionType);
                ResolveEffectiveOnlineState(
                    profile,
                    reportedOnline: false,
                    now,
                    GetDisplayName(profile, identity),
                    transitions);
            }

            if (_state.Sessions.Count > 20000)
            {
                _state.Sessions = _state.Sessions
                    .OrderByDescending(session => session.OnlineAt)
                    .Take(20000)
                    .OrderBy(session => session.OnlineAt)
                    .ToList();
            }

            await SaveCoreAsync(cancellationToken);
            return new MonitorUpdate(BuildViewData(), transitions, now);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<DeviceViewData>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return BuildViewData();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateDeviceProfileAsync(
        string macAddress,
        string customName,
        string customType,
        string customIconGlyph,
        bool notifyWhenStatusChanges,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_state.Devices.TryGetValue(macAddress, out var profile))
            {
                throw new InvalidOperationException("找不到要修改的设备。");
            }

            profile.CustomName = customName.Trim();
            profile.CustomType = customType.Trim();
            profile.CustomIconGlyph = customIconGlyph;
            profile.NotifyWhenOnline = notifyWhenStatusChanges;
            profile.NotifyWhenOffline = notifyWhenStatusChanges;
            await SaveCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveDeviceUntilNextOnlineAsync(
        string macAddress,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_state.Devices.TryGetValue(macAddress, out var profile))
            {
                throw new InvalidOperationException("找不到要移除的设备。");
            }

            profile.IsRemovedFromList = true;
            await SaveCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateRouterAddressAsync(
        string routerAddress,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _state.Settings.RouterAddress = routerAddress.Trim();
            await SaveCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateBehaviorSettingsAsync(
        int refreshIntervalSeconds,
        bool startWithWindows,
        string themeMode,
        bool doNotDisturbEnabled,
        int doNotDisturbStartMinutes,
        int doNotDisturbEndMinutes,
        bool notifyWhenIpv6NotNative,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _state.Settings.RefreshIntervalSeconds = Math.Clamp(refreshIntervalSeconds, 1, 60);
            _state.Settings.CloseToTray = true;
            _state.Settings.StartWithWindows = startWithWindows;
            _state.Settings.ThemeMode = ThemeService.NormalizeMode(themeMode);
            _state.Settings.DoNotDisturbEnabled = doNotDisturbEnabled;
            _state.Settings.DoNotDisturbStartMinutes =
                Math.Clamp(doNotDisturbStartMinutes, 0, 1439);
            _state.Settings.DoNotDisturbEndMinutes =
                Math.Clamp(doNotDisturbEndMinutes, 0, 1439);
            _state.Settings.NotifyWhenIpv6NotNative = notifyWhenIpv6NotNative;
            await SaveCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private List<DeviceViewData> BuildViewData() =>
        _state.Devices.Values
            .Where(profile => !profile.IsRemovedFromList)
            .Select(profile =>
            {
                var identity = DeviceIdentityService.Identify(
                    profile.LastRouterName,
                    profile.LastOriginalName,
                    profile.LastConnectionType);
                var sessions = _state.Sessions
                    .Where(session => session.MacAddress.Equals(
                        profile.MacAddress,
                        StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(session => session.OnlineAt)
                    .Take(100)
                    .Select(CloneSession)
                    .ToList();

                return new DeviceViewData
                {
                    Profile = CloneProfile(profile),
                    Identity = identity,
                    Sessions = sessions
                };
            })
            .OrderByDescending(device => device.Profile.LastKnownOnline)
            .ThenBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private void AddOpenSessionIfMissing(string macAddress, DateTimeOffset onlineAt)
    {
        if (_state.Sessions.Any(session =>
                session.MacAddress.Equals(macAddress, StringComparison.OrdinalIgnoreCase) &&
                session.OfflineAt is null))
        {
            return;
        }

        _state.Sessions.Add(new DeviceSession
        {
            MacAddress = macAddress,
            OnlineAt = onlineAt
        });
    }

    private void CloseOpenSession(string macAddress, DateTimeOffset offlineAt)
    {
        var session = _state.Sessions
            .Where(item =>
                item.MacAddress.Equals(macAddress, StringComparison.OrdinalIgnoreCase) &&
                item.OfflineAt is null)
            .OrderByDescending(item => item.OnlineAt)
            .FirstOrDefault();
        if (session is not null)
        {
            session.OfflineAt = offlineAt;
        }
    }

    /// <summary>
    /// 将单次路由器原始 online 值转换为真正用于 UI、通知和流量统计的状态。
    /// Xiaomi 固件可在一个成功轮询中临时漏掉设备或报告 online=0；这种单帧
    /// 错误不能立刻关闭会话或写入 (0, 0) 的流量样本。
    /// </summary>
    private bool ResolveEffectiveOnlineState(
        DeviceProfile profile,
        bool reportedOnline,
        DateTimeOffset now,
        string displayName,
        ICollection<DeviceTransition> transitions)
    {
        if (reportedOnline)
        {
            profile.LastOnlineSeenAt = now;
            profile.OfflineEvidenceStartedAt = default;
            if (profile.LastKnownOnline)
            {
                return true;
            }

            profile.LastKnownOnline = true;
            profile.IsRemovedFromList = false;
            AddOpenSessionIfMissing(profile.MacAddress, now);
            transitions.Add(new DeviceTransition(
                profile.MacAddress,
                displayName,
                true,
                now,
                profile.NotifyWhenOnline));
            return true;
        }

        if (!profile.LastKnownOnline)
        {
            return false;
        }

        if (profile.OfflineEvidenceStartedAt == default)
        {
            profile.OfflineEvidenceStartedAt = now;
            return true;
        }

        if (now - profile.OfflineEvidenceStartedAt < RouterOfflineConfirmationPeriod)
        {
            return true;
        }

        var firstOfflineEvidenceAt = profile.OfflineEvidenceStartedAt;
        profile.LastKnownOnline = false;
        profile.LastUploadSpeedBytes = 0;
        profile.LastDownloadSpeedBytes = 0;
        profile.LastUploadPositiveSampleAt = default;
        profile.LastDownloadPositiveSampleAt = default;
        profile.LastUploadTotalChangedAt = default;
        profile.LastDownloadTotalChangedAt = default;
        profile.OfflineEvidenceStartedAt = default;
        // 在线历史记录的是首次持续反向证据出现的时间；通知仍在当前确认
        // 时刻发出，避免把 15 秒防抖窗口错误计入“在线时长”。
        CloseOpenSession(profile.MacAddress, firstOfflineEvidenceAt);
        transitions.Add(new DeviceTransition(
            profile.MacAddress,
            displayName,
            false,
            now,
            profile.NotifyWhenOffline));
        return false;
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_statePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_statePath}.tmp";

        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         4096,
                         FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, _state, _jsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, _statePath, overwrite: true);
    }

    private static string GetDisplayName(DeviceProfile profile, DeviceIdentity identity) =>
        !string.IsNullOrWhiteSpace(profile.CustomName)
            ? profile.CustomName
            : !string.IsNullOrWhiteSpace(profile.LastRouterName) &&
              !LooksLikeMacAddress(profile.LastRouterName) &&
              !profile.LastRouterName.Equals(profile.LastOriginalName, StringComparison.OrdinalIgnoreCase)
                ? profile.LastRouterName
                : !string.IsNullOrWhiteSpace(identity.SuggestedName)
                    ? identity.SuggestedName
                    : profile.LastOriginalName;

    private static bool LooksLikeMacAddress(string value)
    {
        var compact = value.Replace(":", string.Empty).Replace("-", string.Empty);
        return compact.Length == 12 && compact.All(Uri.IsHexDigit);
    }

    private static long ApplyOnlineSpeedSample(
        long previous,
        long instantaneousSpeed,
        bool hasCumulativeTotal,
        bool isOnline,
        long derivedSpeed,
        DateTimeOffset lastTotalChangedAt,
        DateTimeOffset now,
        ref DateTimeOffset lastPositiveSampleAt)
    {
        instantaneousSpeed = Math.Max(0, instantaneousSpeed);
        derivedSpeed = Math.Max(0, derivedSpeed);
        if (!isOnline)
        {
            lastPositiveSampleAt = default;
            return 0;
        }

        // 不再把 upspeed/downspeed 作为主数据源：小米固件会周期性将这两个
        // 瞬时字段写成 0，即使同一设备的累计 upload/download 仍持续增长。
        // 有累计字段时，只有“累计字节在实际时间内增长”才会生成新速度；
        // 其余轮次沿用该累计区间的平均速度，直至足够长时间没有新增字节。
        if (hasCumulativeTotal)
        {
            if (derivedSpeed > 0)
            {
                var referenceSpeed = Math.Max(previous, instantaneousSpeed);
                if (referenceSpeed > 0 &&
                    derivedSpeed > referenceSpeed *
                        MaximumDerivedToInstantaneousRatio)
                {
                    // 同一累计快照可能在两个轮询之间积累了完整的 5 秒，
                    // 但本应用恰好只间隔约 1 秒读到它。此时不能把它除以
                    // 1 秒画成尖峰，也不能用瞬时字段的一个短暂低值把曲线
                    // 压低。优先沿用已经连续显示的速度；没有历史时才退回
                    // 本轮瞬时值建立基线。
                    if (previous > 0)
                    {
                        return previous;
                    }

                    lastPositiveSampleAt = now;
                    return instantaneousSpeed;
                }

                lastPositiveSampleAt = now;
                return derivedSpeed;
            }

            if (previous > 0)
            {
                // 应用重启后运行时的时间戳不会写入 state.json。首个累计样本
                // 仅建立新的差分基线，因此先短暂保留上次已保存的显示速度。
                if (lastTotalChangedAt == default)
                {
                    if (lastPositiveSampleAt == default)
                    {
                        lastPositiveSampleAt = now;
                    }

                    return now - lastPositiveSampleAt <=
                           CumulativeTrafficIdleConfirmationPeriod
                        ? previous
                        : 0;
                }

                if (now - lastTotalChangedAt <=
                    CumulativeTrafficIdleConfirmationPeriod)
                {
                    return previous;
                }

                // 若累计字段长时间没有刷新但瞬时字段仍稳定报告正值，说明
                // 是累计计数通道停滞而非设备空闲。只在此失效转移场景采用
                // 瞬时值，避免重新引入它的周期性 0。
                if (instantaneousSpeed > 0)
                {
                    lastPositiveSampleAt = now;
                    return instantaneousSpeed;
                }

                return 0;
            }

            // 刚开始监控而累计值还没有完成第一个更新周期时，可临时使用一次
            // 瞬时值避免图表起始阶段空白；一旦累计值发生变化，上方分支会
            // 永久接管正常流量的显示与绘图。
            if (instantaneousSpeed > 0)
            {
                lastPositiveSampleAt = now;
                return instantaneousSpeed;
            }

            return 0;
        }

        // 部分旧型号没有累计字节字段，才退回到瞬时速度并保留已有的短暂
        // 占位 0 防抖逻辑，确保该兼容路径不影响有累计计数的设备。
        if (instantaneousSpeed > 0)
        {
            lastPositiveSampleAt = now;
            return instantaneousSpeed;
        }

        if (previous <= 0)
        {
            return 0;
        }

        // 应用重启后，最近一次可靠速度会从 state.json 恢复，但进程内的
        // 时间戳不会持久化。先用它建立短暂的保护窗口，避免首轮恰逢固件
        // 占位 0 时把历史中的连续流量错误地写成 0。
        if (lastPositiveSampleAt == default)
        {
            lastPositiveSampleAt = now;
            return previous;
        }

        // 两路接口同时报 0 时，不能再以“第几个 0”判定。设备统计本身
        // 的刷新节奏可慢于用户选择的轮询频率；只要仍处于短暂的可信窗口，
        // 就沿用最后一个可靠速度，避免把固件占位值写入所有展示位置和图表。
        return now - lastPositiveSampleAt <= PlaceholderZeroGracePeriod
            ? previous
            : 0;
    }

    private static long ApplyOnlineTotalSample(
        long previous,
        long current,
        bool isOnline)
    {
        current = Math.Max(0, current);
        if (!isOnline)
        {
            return previous > 0 ? previous : current;
        }

        if (previous > 0 && current < previous)
        {
            return previous;
        }

        return current;
    }

    private static long CalculateDerivedSpeed(
        long previousTotal,
        long currentTotal,
        DateTimeOffset previousTotalChangedAt,
        DateTimeOffset now,
        bool isValid,
        out DateTimeOffset lastTotalChangedAt)
    {
        lastTotalChangedAt = previousTotalChangedAt;
        if (!isValid)
        {
            return 0;
        }

        if (lastTotalChangedAt == default)
        {
            // 第一次见到累计计数只建立基线，不能把应用启动前的全部流量
            // 误算进当前一秒的网速。
            lastTotalChangedAt = now;
            return 0;
        }

        if (previousTotal < 0 || currentTotal <= previousTotal)
        {
            return 0;
        }

        var elapsedSeconds = (now - lastTotalChangedAt).TotalSeconds;
        lastTotalChangedAt = now;
        if (elapsedSeconds <= 0)
        {
            return 0;
        }

        var speed = (currentTotal - previousTotal) / elapsedSeconds;
        return speed >= long.MaxValue ? long.MaxValue : (long)Math.Round(speed);
    }

    private static DeviceSession CloneSession(DeviceSession session) => new()
    {
        MacAddress = session.MacAddress,
        OnlineAt = session.OnlineAt,
        OfflineAt = session.OfflineAt
    };

    private static DeviceProfile CloneProfile(DeviceProfile profile) => new()
    {
        MacAddress = profile.MacAddress,
        CustomName = profile.CustomName,
        CustomType = profile.CustomType,
        CustomIconGlyph = profile.CustomIconGlyph,
        NotifyWhenOnline = profile.NotifyWhenOnline,
        NotifyWhenOffline = profile.NotifyWhenOffline,
        HasObservedState = profile.HasObservedState,
        LastKnownOnline = profile.LastKnownOnline,
        IsRemovedFromList = profile.IsRemovedFromList,
        FirstSeenAt = profile.FirstSeenAt,
        LastSeenAt = profile.LastSeenAt,
        LastOnlineSeenAt = profile.LastOnlineSeenAt,
        LastRouterName = profile.LastRouterName,
        LastOriginalName = profile.LastOriginalName,
        LastIpAddress = profile.LastIpAddress,
        LastConnectionType = profile.LastConnectionType,
        LastUploadSpeedBytes = profile.LastUploadSpeedBytes,
        LastDownloadSpeedBytes = profile.LastDownloadSpeedBytes,
        LastTotalUploadBytes = profile.LastTotalUploadBytes,
        LastTotalDownloadBytes = profile.LastTotalDownloadBytes
    };
}
