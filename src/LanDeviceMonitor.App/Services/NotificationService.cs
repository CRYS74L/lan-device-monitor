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
            .AddText($"设备{state}")
            .AddText($"{displayName} {state} · {occurredAt:MM月dd日 HH:mm:ss}")
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
