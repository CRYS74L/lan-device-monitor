using System.IO;
using System.Text.Json;
using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Services;

public sealed class DeviceRepository
{
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
            var onlineMacs = snapshots
                .Where(snapshot => snapshot.IsOnline)
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

                var identity = DeviceIdentityService.Identify(
                    snapshot.RouterName,
                    snapshot.OriginalName,
                    snapshot.ConnectionType);
                var displayName = GetDisplayName(profile, identity);

                if (!profile.HasObservedState)
                {
                    profile.HasObservedState = true;
                    profile.LastKnownOnline = snapshot.IsOnline;
                    if (snapshot.IsOnline)
                    {
                        var estimatedStart = now - TimeSpan.FromSeconds(
                            Math.Max(0, snapshot.OnlineDurationSeconds));
                        AddOpenSessionIfMissing(snapshot.MacAddress, estimatedStart);
                        profile.FirstSeenAt = estimatedStart;
                    }
                }
                else if (!profile.LastKnownOnline && snapshot.IsOnline)
                {
                    profile.LastKnownOnline = true;
                    AddOpenSessionIfMissing(snapshot.MacAddress, now);
                    transitions.Add(new DeviceTransition(
                        snapshot.MacAddress,
                        displayName,
                        true,
                        now,
                        profile.NotifyWhenOnline));
                }

                profile.LastSeenAt = now;
                profile.LastRouterName = snapshot.RouterName;
                profile.LastOriginalName = snapshot.OriginalName;
                profile.LastIpAddress = snapshot.IpAddress;
                profile.LastConnectionType = snapshot.ConnectionType;
                profile.LastUploadSpeedBytes = snapshot.UploadSpeedBytes;
                profile.LastDownloadSpeedBytes = snapshot.DownloadSpeedBytes;
                profile.LastTotalUploadBytes = snapshot.TotalUploadBytes;
                profile.LastTotalDownloadBytes = snapshot.TotalDownloadBytes;
            }

            foreach (var profile in _state.Devices.Values)
            {
                if (!profile.HasObservedState ||
                    !profile.LastKnownOnline ||
                    onlineMacs.Contains(profile.MacAddress))
                {
                    continue;
                }

                profile.LastKnownOnline = false;
                profile.LastUploadSpeedBytes = 0;
                profile.LastDownloadSpeedBytes = 0;
                CloseOpenSession(profile.MacAddress, now);
                var identity = DeviceIdentityService.Identify(
                    profile.LastRouterName,
                    profile.LastOriginalName,
                    profile.LastConnectionType);
                transitions.Add(new DeviceTransition(
                    profile.MacAddress,
                    GetDisplayName(profile, identity),
                    false,
                    now,
                    profile.NotifyWhenOffline));
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
            _state.Settings.RefreshIntervalSeconds = Math.Clamp(refreshIntervalSeconds, 2, 60);
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
        FirstSeenAt = profile.FirstSeenAt,
        LastSeenAt = profile.LastSeenAt,
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
