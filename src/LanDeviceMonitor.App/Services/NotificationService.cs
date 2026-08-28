namespace LanDeviceMonitor.Services;

public enum NotificationDeliveryChannel
{
    Pending,
    WindowsShell,
    Unavailable
}

public sealed record NotificationDeliveryResult(
    NotificationDeliveryChannel Channel,
    string StatusText,
    string? FailureDetail = null)
{
    public bool WasSubmittedToWindows =>
        Channel == NotificationDeliveryChannel.WindowsShell;

    public bool IsWindowsNotificationReady => Channel is
        NotificationDeliveryChannel.Pending or NotificationDeliveryChannel.WindowsShell;
}

public sealed class NotificationService : IDisposable
{
    private readonly object _syncRoot = new();
    private Action<string, string>? _windowsNotification;
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

    public void UseWindowsShellNotification(Action<string, string> showNotification)
    {
        ArgumentNullException.ThrowIfNull(showNotification);

        lock (_syncRoot)
        {
            _windowsNotification = showNotification;
            _lastResult = CreateStatus(NotificationDeliveryChannel.Pending, null);
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
            message: $"发生时间：{occurredAt:yyyy年MM月dd日 HH:mm:ss}");
    }

    public NotificationDeliveryResult ShowIpv6NonNative(string connectionType)
    {
        var displayType = string.IsNullOrWhiteSpace(connectionType)
            ? "未知"
            : connectionType.Trim();
        return Show(
            title: "当前 IPv6 连接类型不是 Native",
            message: $"当前连接类型：{displayType}");
    }

    public NotificationDeliveryResult ShowTestNotification() => Show(
        title: "Windows 系统通知测试",
        message: "局域网设备助手已通过 Windows 通知区域发送这条通知。");

    public void Dispose()
    {
        lock (_syncRoot)
        {
            _windowsNotification = null;
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
            NotificationDeliveryChannel.WindowsShell =>
                "Windows 系统通知已通过通知区域提交。",
            _ =>
                $"Windows 系统通知不可用：{failureDetail ?? "未知错误"}。可使用下方按钮重新检测。"
        };
    }

    private NotificationDeliveryResult Show(string title, string message)
    {
        lock (_syncRoot)
        {
            if (_windowsNotification is null)
            {
                return Publish(
                    NotificationDeliveryChannel.Unavailable,
                    "Windows 通知区域尚未初始化");
            }

            try
            {
                _windowsNotification(title, message);
                return Publish(NotificationDeliveryChannel.WindowsShell, null);
            }
            catch (Exception exception)
            {
                return Publish(
                    NotificationDeliveryChannel.Unavailable,
                    DescribeException(exception));
            }
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
