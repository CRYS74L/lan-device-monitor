using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.ViewModels;

public sealed class DeviceItemViewModel
{
    public DeviceItemViewModel(DeviceViewData data)
    {
        Data = data;
        Sessions = data.Sessions.Select(session => new SessionItemViewModel(session)).ToList();
    }

    public DeviceViewData Data { get; }
    public DeviceProfile Profile => Data.Profile;
    public string MacAddress => Profile.MacAddress;
    public string DisplayName => Data.DisplayName;
    public string TypeName => Data.TypeName;
    public string IconGlyph => Data.IconGlyph;
    public bool IsOnline => Profile.LastKnownOnline;
    public string StatusText => IsOnline ? "在线" : "离线";
    public string IpAddress => string.IsNullOrWhiteSpace(Profile.LastIpAddress) ? "—" : Profile.LastIpAddress;
    public string ConnectionText => ConnectionTypeToText(Profile.LastConnectionType);
    public string UploadSpeedText => FormatSpeed(Profile.LastUploadSpeedBytes);
    public string DownloadSpeedText => FormatSpeed(Profile.LastDownloadSpeedBytes);
    public string TotalUploadText => FormatBytes(Profile.LastTotalUploadBytes);
    public string TotalDownloadText => FormatBytes(Profile.LastTotalDownloadBytes);
    public string TotalUsageText => FormatBytes(
        Profile.LastTotalUploadBytes + Profile.LastTotalDownloadBytes);
    public string LastSeenText => Profile.LastSeenAt == default
        ? "尚无记录"
        : Profile.LastKnownOnline
            ? "当前在线"
            : $"{Profile.LastSeenAt:MM月dd日 HH:mm} 最后在线";
    public IReadOnlyList<SessionItemViewModel> Sessions { get; }

    public static string FormatSpeed(long bytesPerSecond)
    {
        if (bytesPerSecond < 1024)
        {
            return $"{bytesPerSecond} B/s";
        }

        if (bytesPerSecond < 1024 * 1024)
        {
            return $"{bytesPerSecond / 1024d:0.0} KB/s";
        }

        if (bytesPerSecond < 1024L * 1024 * 1024)
        {
            return $"{bytesPerSecond / 1024d / 1024d:0.00} MB/s";
        }

        return $"{bytesPerSecond / 1024d / 1024d / 1024d:0.00} GB/s";
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:0.0} KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024d:0.00} MB";
        }

        return $"{bytes / 1024d / 1024d / 1024d:0.00} GB";
    }

    private static string ConnectionTypeToText(int connectionType) => connectionType switch
    {
        0 => "网线接入",
        1 => "2.4GHz",
        2 => "5GHz",
        3 => "访客 Wi-Fi",
        4 => "Mesh 接入",
        5 => "5GHz-Game",
        _ => "Wi-Fi"
    };
}

public sealed class SessionItemViewModel
{
    public SessionItemViewModel(DeviceSession session)
    {
        OnlineAt = session.OnlineAt;
        OfflineAt = session.OfflineAt;
    }

    public DateTimeOffset OnlineAt { get; }
    public DateTimeOffset? OfflineAt { get; }
    public string OnlineDateText => OnlineAt.ToString("MM月dd日");
    public string OnlineTimeText => OnlineAt.ToString("HH:mm");
    public string OfflineDateText =>
        OfflineAt is DateTimeOffset offlineAt
            ? offlineAt.ToString("MM月dd日")
            : "当前仍在线";
    public string OfflineTimeText =>
        OfflineAt?.ToString("HH:mm") ?? string.Empty;
    public string DurationText
    {
        get
        {
            var duration = (OfflineAt ?? DateTimeOffset.Now) - OnlineAt;
            if (duration.TotalDays >= 1)
            {
                return $"{(int)duration.TotalDays}天 {duration.Hours}小时";
            }

            if (duration.TotalHours >= 1)
            {
                return $"{(int)duration.TotalHours}小时 {duration.Minutes}分钟";
            }

            return $"{Math.Max(0, (int)duration.TotalMinutes)}分钟";
        }
    }
}

public sealed record DeviceTypeOption(string TypeName, string IconGlyph)
{
    public string DisplayText => $"{IconGlyph}  {TypeName}";
}
