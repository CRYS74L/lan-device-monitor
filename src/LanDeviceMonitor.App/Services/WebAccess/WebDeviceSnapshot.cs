using LanDeviceMonitor.Models;
using LanDeviceMonitor.ViewModels;

namespace LanDeviceMonitor.Services.WebAccess;

internal sealed record WebDeviceSnapshot(string Id, string Name, string Type, string IconType,
    bool Online, string Ip, string Mac, string Connection, long Upload, long Download,
    long TotalUpload, long TotalDownload, DateTimeOffset FirstSeen, DateTimeOffset LastSeen,
    bool NotifyOnline, bool NotifyOffline, string OriginalName, string RouterName,
    string CustomName, string CustomType, string RecordRange, DateTimeOffset? OnlineSince,
    int SessionCount)
{
    public static WebDeviceSnapshot From(DeviceViewData d)
    {
        var p = d.Profile;
        return new(p.MacAddress, d.DisplayName, d.TypeName,
            (DeviceIdentityService.AvailableTypes.FirstOrDefault(t => t.IconGlyph == d.IconGlyph && t.TypeName == d.TypeName)
                ?? DeviceIdentityService.AvailableTypes.FirstOrDefault(t => t.IconGlyph == d.IconGlyph))?.TypeName ?? d.TypeName,
            p.LastKnownOnline, p.LastIpAddress, p.MacAddress, DeviceItemViewModel.ConnectionTypeToText(p.LastConnectionType),
            p.LastUploadSpeedBytes, p.LastDownloadSpeedBytes, p.LastTotalUploadBytes, p.LastTotalDownloadBytes,
            p.FirstSeenAt, p.LastSeenAt, p.NotifyWhenOnline, p.NotifyWhenOffline,
            p.LastOriginalName, p.LastRouterName, p.CustomName, DeviceIdentity.NormalizeTypeName(p.CustomType),
            DeviceItemViewModel.FormatDateRange(p.FirstSeenAt, p.LastSeenAt),
            p.LastKnownOnline ? d.Sessions.Where(s => s.OfflineAt is null).Select(s => (DateTimeOffset?)s.OnlineAt).Max() : null,
            d.Sessions.Count);
    }
}
