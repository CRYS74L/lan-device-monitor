using System.Globalization;
using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.ViewModels;

public sealed class DeviceItemViewModel
{
    public DeviceItemViewModel(
        DeviceViewData data,
        IReadOnlyList<TrafficSample>? trafficSamples = null)
    {
        Data = data;
        Sessions = data.Sessions.Select(session => new SessionItemViewModel(session)).ToList();
        TrafficSamples = trafficSamples ?? [];
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
    public string MacAddressText => MacAddress;
    public string ConnectionText => ConnectionTypeToText(Profile.LastConnectionType);
    public string UploadSpeedText => FormatSpeed(Profile.LastUploadSpeedBytes);
    public string DownloadSpeedText => FormatSpeed(Profile.LastDownloadSpeedBytes);
    public string UploadSpeedValueText => FormatSpeedValue(Profile.LastUploadSpeedBytes);
    public string UploadSpeedUnitText => FormatSpeedUnit(Profile.LastUploadSpeedBytes);
    public string DownloadSpeedValueText => FormatSpeedValue(Profile.LastDownloadSpeedBytes);
    public string DownloadSpeedUnitText => FormatSpeedUnit(Profile.LastDownloadSpeedBytes);
    public string TotalUploadText => FormatBytes(Profile.LastTotalUploadBytes);
    public string TotalDownloadText => FormatBytes(Profile.LastTotalDownloadBytes);
    public string TotalUploadValueText => FormatBytesValue(Profile.LastTotalUploadBytes);
    public string TotalUploadUnitText => FormatBytesUnit(Profile.LastTotalUploadBytes);
    public string TotalDownloadValueText => FormatBytesValue(Profile.LastTotalDownloadBytes);
    public string TotalDownloadUnitText => FormatBytesUnit(Profile.LastTotalDownloadBytes);
    public string TotalUsageText => FormatBytes(
        Profile.LastTotalUploadBytes + Profile.LastTotalDownloadBytes);
    public string LastSeenText => Profile.LastSeenAt == default
        ? "尚无记录"
        : Profile.LastKnownOnline
            ? "当前在线"
            : $"{Profile.LastSeenAt:MM月dd日 HH:mm} 最后在线";
    public IReadOnlyList<SessionItemViewModel> Sessions { get; }
    public IReadOnlyList<TrafficSample> TrafficSamples { get; }

    public static string FormatSpeed(long bytesPerSecond)
    {
        return $"{FormatSpeedValue(bytesPerSecond)} {FormatSpeedUnit(bytesPerSecond)}";
    }

    public static string FormatBytes(long bytes)
    {
        return $"{FormatBytesValue(bytes)} {FormatBytesUnit(bytes)}";
    }

    public static string FormatSpeedValue(long bytesPerSecond) =>
        FormatRateParts(bytesPerSecond, "/s").Value;

    public static string FormatSpeedUnit(long bytesPerSecond) =>
        FormatRateParts(bytesPerSecond, "/s").Unit;

    public static string FormatBytesValue(long bytes) =>
        FormatRateParts(bytes, string.Empty).Value;

    public static string FormatBytesUnit(long bytes) =>
        FormatRateParts(bytes, string.Empty).Unit;

    private static (string Value, string Unit) FormatRateParts(
        long bytes,
        string suffix)
    {
        bytes = Math.Max(0, bytes);
        var units = new[] { "B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
        var exponent = bytes < 1
            ? 0
            : Math.Min(
                (int)Math.Floor(Math.Log2(bytes) / 10d),
                units.Length - 1);
        var scaled = bytes / Math.Pow(1024d, exponent);
        var value = scaled >= 1000d
            ? scaled.ToString("0", CultureInfo.CurrentCulture)
            : FormatThreeSignificantDigits(scaled);
        return (value, $"{units[Math.Max(0, exponent)]}{suffix}");
    }

    private static string FormatThreeSignificantDigits(double value)
    {
        if (value == 0)
        {
            return "0.00";
        }

        var digitsBeforeDecimal = (int)Math.Floor(Math.Log10(Math.Abs(value))) + 1;
        var decimalPlaces = Math.Max(0, 3 - digitsBeforeDecimal);
        return value.ToString(
            $"F{decimalPlaces}",
            CultureInfo.CurrentCulture);
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
            : "当前在线";
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
