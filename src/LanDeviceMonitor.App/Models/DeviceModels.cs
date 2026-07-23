using System.Text.Json.Serialization;

namespace LanDeviceMonitor.Models;

public sealed record DeviceSnapshot(
    string MacAddress,
    string RouterName,
    string OriginalName,
    bool IsOnline,
    string IpAddress,
    int ConnectionType,
    long OnlineDurationSeconds,
    long UploadSpeedBytes,
    long DownloadSpeedBytes,
    long TotalUploadBytes,
    long TotalDownloadBytes);

public sealed class DeviceProfile
{
    public string MacAddress { get; set; } = string.Empty;
    public string CustomName { get; set; } = string.Empty;
    public string CustomType { get; set; } = string.Empty;
    public string CustomIconGlyph { get; set; } = string.Empty;
    public bool NotifyWhenOnline { get; set; }
    public bool NotifyWhenOffline { get; set; }
    public bool HasObservedState { get; set; }
    public bool LastKnownOnline { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string LastRouterName { get; set; } = string.Empty;
    public string LastOriginalName { get; set; } = string.Empty;
    public string LastIpAddress { get; set; } = string.Empty;
    public int LastConnectionType { get; set; }
    public long LastUploadSpeedBytes { get; set; }
    public long LastDownloadSpeedBytes { get; set; }
    public long LastTotalUploadBytes { get; set; }
    public long LastTotalDownloadBytes { get; set; }
}

public sealed class DeviceSession
{
    public string MacAddress { get; set; } = string.Empty;
    public DateTimeOffset OnlineAt { get; set; }
    public DateTimeOffset? OfflineAt { get; set; }
}

public sealed class AppSettings
{
    public string RouterAddress { get; set; } = "192.168.31.1";
    public int RefreshIntervalSeconds { get; set; } = 3;
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; }
}

public sealed class PersistedState
{
    public int SchemaVersion { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public Dictionary<string, DeviceProfile> Devices { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<DeviceSession> Sessions { get; set; } = [];
}

public sealed record DeviceTransition(
    string MacAddress,
    string DisplayName,
    bool IsOnline,
    DateTimeOffset OccurredAt,
    bool ShouldNotify);

public sealed record MonitorUpdate(
    IReadOnlyList<DeviceViewData> Devices,
    IReadOnlyList<DeviceTransition> Transitions,
    DateTimeOffset RefreshedAt);

public sealed record RouterInternetInfo(
    bool IsConnected,
    string Ipv4ConnectionType,
    string Ipv4Address,
    string Ipv4SubnetMask,
    string Ipv4Gateway,
    string Ipv4Dns,
    string Ipv6ConnectionType,
    string WanIpv6Address,
    string WanIpv6Gateway,
    string LanIpv6Address,
    string LanIpv6Prefix,
    string Ipv6Dns);

public sealed record DeviceIdentity(string TypeName, string IconGlyph, string SuggestedName);

public sealed class RouterApiException : Exception
{
    public RouterApiException(string message, int? code = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public int? Code { get; }
}

public sealed class DeviceViewData
{
    public required DeviceProfile Profile { get; init; }
    public required DeviceIdentity Identity { get; init; }
    public required IReadOnlyList<DeviceSession> Sessions { get; init; }

    [JsonIgnore]
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Profile.CustomName)
            ? Profile.CustomName
            : IsMeaningfulRouterName(Profile.LastRouterName, Profile.LastOriginalName)
                ? Profile.LastRouterName
                : !string.IsNullOrWhiteSpace(Identity.SuggestedName)
                    ? Identity.SuggestedName
                    : Profile.LastOriginalName;

    [JsonIgnore]
    public string TypeName =>
        !string.IsNullOrWhiteSpace(Profile.CustomType) ? Profile.CustomType : Identity.TypeName;

    [JsonIgnore]
    public string IconGlyph =>
        !string.IsNullOrWhiteSpace(Profile.CustomIconGlyph)
            ? Profile.CustomIconGlyph
            : Identity.IconGlyph;

    private static bool IsMeaningfulRouterName(string routerName, string originalName)
    {
        if (string.IsNullOrWhiteSpace(routerName))
        {
            return false;
        }

        var compact = routerName.Replace(":", string.Empty).Replace("-", string.Empty);
        var looksLikeMac = compact.Length == 12 && compact.All(Uri.IsHexDigit);
        return !looksLikeMac &&
               !routerName.Equals(originalName, StringComparison.OrdinalIgnoreCase);
    }
}
