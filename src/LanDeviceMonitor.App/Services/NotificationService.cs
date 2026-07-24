using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace LanDeviceMonitor.Services;

public sealed class NotificationService : IDisposable
{
    private bool _registered;

    public void Register()
    {
        if (_registered)
        {
            return;
        }

        AppNotificationManager.Default.Register();
        _registered = true;
    }

    public void ShowDeviceState(string displayName, bool isOnline, DateTimeOffset occurredAt)
    {
        if (!_registered)
        {
            return;
        }

        var state = isOnline ? "已上线" : "已离线";
        var notification = new AppNotificationBuilder()
            .AddArgument("device", displayName)
            .AddText($"{displayName}{state}")
            .AddText($"发生时间：{occurredAt:yyyy年MM月dd日 HH:mm:ss}")
            .BuildNotification();

        AppNotificationManager.Default.Show(notification);
    }

    public void ShowIpv6NonNative(string connectionType)
    {
        if (!_registered)
        {
            return;
        }

        var displayType = string.IsNullOrWhiteSpace(connectionType)
            ? "未知"
            : connectionType.Trim();
        var notification = new AppNotificationBuilder()
            .AddArgument("category", "ipv6")
            .AddText("当前 IPv6 连接类型不是 Native")
            .AddText($"当前连接类型：{displayType}")
            .BuildNotification();

        AppNotificationManager.Default.Show(notification);
    }

    public void Dispose()
    {
        if (!_registered)
        {
            return;
        }

        AppNotificationManager.Default.Unregister();
        _registered = false;
    }
}
