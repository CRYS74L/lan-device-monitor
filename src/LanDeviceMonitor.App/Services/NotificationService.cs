using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System.Runtime.InteropServices;

namespace LanDeviceMonitor.Services;

public sealed class NotificationService : IDisposable
{
    private bool _registered;
    private Action<string, string>? _fallbackNotification;

    public void Register()
    {
        if (_registered)
        {
            return;
        }

        try
        {
            if (!AppNotificationManager.IsSupported())
            {
                return;
            }

            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException or COMException)
        {
            // 自包含、未打包应用无法使用依赖 Singleton MSIX 包的通知组件时，
            // 由现有托盘图标发送 Windows Shell 通知。
            _registered = false;
        }
    }

    public void UseFallback(Action<string, string> showNotification)
    {
        _fallbackNotification = showNotification;
    }

    public void ShowDeviceState(string displayName, bool isOnline, DateTimeOffset occurredAt)
    {
        var state = isOnline ? "已上线" : "已离线";
        var title = $"{displayName}{state}";
        var message = $"发生时间：{occurredAt:yyyy年MM月dd日 HH:mm:ss}";
        if (_registered)
        {
            var notification = new AppNotificationBuilder()
                .AddArgument("device", displayName)
                .AddText(title)
                .AddText(message)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
            return;
        }

        _fallbackNotification?.Invoke(title, message);
    }

    public void ShowIpv6NonNative(string connectionType)
    {
        var displayType = string.IsNullOrWhiteSpace(connectionType)
            ? "未知"
            : connectionType.Trim();
        const string title = "当前 IPv6 连接类型不是 Native";
        var message = $"当前连接类型：{displayType}，建议重启路由器";
        if (_registered)
        {
            var notification = new AppNotificationBuilder()
                .AddArgument("category", "ipv6")
                .AddText(title)
                .AddText(message)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
            return;
        }

        _fallbackNotification?.Invoke(title, message);
    }

    public void Dispose()
    {
        _fallbackNotification = null;
        if (!_registered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.Unregister();
        }
        catch (UnauthorizedAccessException)
        {
            // 进程退出时无需因系统通知组件不可用而阻塞关闭。
        }

        _registered = false;
    }
}
