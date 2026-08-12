using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System.IO;

namespace LanDeviceMonitor.Services;

public enum NotificationDeliveryChannel
{
    Pending,
    WindowsToast,
    TrayFallback,
    Unavailable
}

public sealed record NotificationDeliveryResult(
    NotificationDeliveryChannel Channel,
    string StatusText,
    string? FailureDetail = null)
{
    public bool WasSubmittedToWindows => Channel == NotificationDeliveryChannel.WindowsToast;

    public bool IsNativeWindowsNotificationReady => Channel is
        NotificationDeliveryChannel.Pending or NotificationDeliveryChannel.WindowsToast;
}

public sealed class NotificationService : IDisposable
{
    private const string DisplayName = "局域网设备助手";
    private const string IconRelativePath = "Assets\\SystemIcon.png";

    private readonly object _syncRoot = new();
    private bool _registered;
    private bool _repairAttempted;
    private Action<string, string>? _fallbackNotification;
    private NotificationDeliveryResult _lastResult = new(
        NotificationDeliveryChannel.Pending,
        "Windows 系统通知正在初始化。");

    public NotificationDeliveryResult LastResult
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastResult;
            }
        }
    }

    public void Register()
    {
        lock (_syncRoot)
        {
            if (_registered)
            {
                return;
            }

            _lastResult = TryRegister(out var failureDetail)
                ? CreateStatus(NotificationDeliveryChannel.Pending, null)
                : CreateStatus(NotificationDeliveryChannel.Unavailable, failureDetail);
        }
    }

    public void UseFallback(Action<string, string> showNotification)
    {
        lock (_syncRoot)
        {
            _fallbackNotification = showNotification;
        }
    }

    public NotificationDeliveryResult ShowDeviceState(
        string displayName,
        bool isOnline,
        DateTimeOffset occurredAt)
    {
        var state = isOnline ? "已上线" : "已离线";
        return Show(
            title: $"{displayName}{state}",
            message: $"发生时间：{occurredAt:yyyy年MM月dd日 HH:mm:ss}",
            argumentName: "device",
            argumentValue: displayName);
    }

    public NotificationDeliveryResult ShowIpv6NonNative(string connectionType)
    {
        var displayType = string.IsNullOrWhiteSpace(connectionType)
            ? "未知"
            : connectionType.Trim();
        return Show(
            title: "当前 IPv6 连接类型不是 Native",
            message: $"当前连接类型：{displayType}",
            argumentName: "category",
            argumentValue: "ipv6");
    }

    // 设置页测试和设备上下线共用同一个投递路径，避免出现“测试能发、实际不能发”。
    public NotificationDeliveryResult ShowTestNotification() => Show(
        title: "Windows 系统通知测试",
        message: "局域网设备助手已将这条通知提交到 Windows。",
        argumentName: "category",
        argumentValue: "test",
        resetRepairAttempt: true);

    public void Dispose()
    {
        lock (_syncRoot)
        {
            // 不在退出时注销。Windows App SDK 在部分系统上注销可能阻塞，
            // 而进程结束后 Windows 会自行回收这一进程的通知注册资源。
            _fallbackNotification = null;
            _registered = false;
        }
    }

    internal static string GetDeliveryStatusText(
        NotificationDeliveryChannel channel,
        string? failureDetail)
    {
        return channel switch
        {
            NotificationDeliveryChannel.Pending =>
                "Windows 系统通知已就绪；可使用下方按钮验证投递。",
            NotificationDeliveryChannel.WindowsToast =>
                "Windows 系统通知已提交到通知中心。若开启了专注助手，横幅可能由系统延后显示。",
            NotificationDeliveryChannel.TrayFallback =>
                $"Windows 系统通知未能提交：{failureDetail ?? "未知错误"}。已改用托盘提示。",
            _ =>
                $"Windows 系统通知不可用：{failureDetail ?? "未知错误"}。可使用下方按钮重新检测。"
        };
    }

    private NotificationDeliveryResult Show(
        string title,
        string message,
        string argumentName,
        string argumentValue,
        bool resetRepairAttempt = false)
    {
        lock (_syncRoot)
        {
            if (resetRepairAttempt)
            {
                _repairAttempted = false;
            }

            AppNotification notification;
            try
            {
                notification = new AppNotificationBuilder()
                    .AddArgument(argumentName, argumentValue)
                    .AddText(title)
                    .AddText(message)
                    .BuildNotification();
            }
            catch (Exception exception)
            {
                return PublishFallback(title, message, DescribeException(exception));
            }

            if (TryRegister(out var failureDetail) &&
                TryShow(notification, out failureDetail))
            {
                return Publish(NotificationDeliveryChannel.WindowsToast, null);
            }

            var firstFailure = failureDetail;
            if (TryRepairRegistration(out var repairFailure) &&
                TryShow(notification, out repairFailure))
            {
                return Publish(NotificationDeliveryChannel.WindowsToast, null);
            }

            var finalFailure = string.IsNullOrWhiteSpace(repairFailure)
                ? firstFailure
                : repairFailure;
            return PublishFallback(title, message, finalFailure);
        }
    }

    private bool TryRegister(out string? failureDetail)
    {
        failureDetail = null;
        if (_registered)
        {
            return true;
        }

        try
        {
            if (!AppNotificationManager.IsSupported())
            {
                failureDetail = "当前 Windows 版本不支持 App SDK 系统通知";
                return false;
            }

            AppNotificationManager.Default.Register(DisplayName, GetIconUri());
            _registered = true;
            return true;
        }
        catch (Exception exception)
        {
            _registered = false;
            failureDetail = DescribeException(exception);
            return false;
        }
    }

    private bool TryRepairRegistration(out string? failureDetail)
    {
        failureDetail = null;
        if (_repairAttempted)
        {
            failureDetail = "本次运行已尝试修复 Windows 通知注册";
            return false;
        }

        _repairAttempted = true;
        _registered = false;

        string? unregisterFailure = null;
        try
        {
            // 清理旧版本留下的注册资料后仅重试一次，避免高频状态变化反复改写系统注册。
            AppNotificationManager.Default.UnregisterAll();
        }
        catch (Exception exception)
        {
            unregisterFailure = DescribeException(exception);
        }

        if (TryRegister(out var registerFailure))
        {
            return true;
        }

        failureDetail = string.Join(
            "；",
            new[] { unregisterFailure, registerFailure }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        return false;
    }

    private bool TryShow(AppNotification notification, out string? failureDetail)
    {
        try
        {
            AppNotificationManager.Default.Show(notification);
            failureDetail = null;
            return true;
        }
        catch (Exception exception)
        {
            _registered = false;
            failureDetail = DescribeException(exception);
            return false;
        }
    }

    private NotificationDeliveryResult PublishFallback(
        string title,
        string message,
        string? failureDetail)
    {
        if (_fallbackNotification is null)
        {
            return Publish(NotificationDeliveryChannel.Unavailable, failureDetail);
        }

        try
        {
            _fallbackNotification(title, message);
            return Publish(NotificationDeliveryChannel.TrayFallback, failureDetail);
        }
        catch (Exception exception)
        {
            var combinedFailure = string.Join(
                "；",
                new[] { failureDetail, DescribeException(exception) }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            return Publish(NotificationDeliveryChannel.Unavailable, combinedFailure);
        }
    }

    private NotificationDeliveryResult Publish(
        NotificationDeliveryChannel channel,
        string? failureDetail)
    {
        _lastResult = CreateStatus(channel, failureDetail);
        return _lastResult;
    }

    private static NotificationDeliveryResult CreateStatus(
        NotificationDeliveryChannel channel,
        string? failureDetail) => new(
        channel,
        GetDeliveryStatusText(channel, failureDetail),
        failureDetail);

    private static Uri GetIconUri()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, IconRelativePath);
        if (!File.Exists(iconPath))
        {
            throw new FileNotFoundException("Windows 系统通知图标不存在", iconPath);
        }

        return new Uri(iconPath);
    }

    private static string DescribeException(Exception exception)
    {
        var rootException = exception.GetBaseException();
        var hresult = rootException.HResult == 0
            ? string.Empty
            : $"，HRESULT 0x{rootException.HResult:X8}";
        var message = string.IsNullOrWhiteSpace(rootException.Message)
            ? "没有提供错误说明"
            : rootException.Message.Trim();
        return $"{rootException.GetType().Name}{hresult}：{message}";
    }
}
