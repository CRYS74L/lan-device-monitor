using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Application = System.Windows.Application;

namespace LanDeviceMonitor.Services;

public sealed class TrayService : IDisposable
{
    private readonly NativeTrayIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly Controls.TrayCommandMenu _menu;
    private bool _isDisposed;

    public TrayService(Action showWindow, Action exitApplication)
    {
        _menu = new Controls.TrayCommandMenu(showWindow, exitApplication);
        _icon = LoadSystemIcon();
        try { _notifyIcon = new NativeTrayIcon(_icon, BuildNotifyIconText(), showWindow, point => _menu.Show(point)); }
        catch { _icon.Dispose(); throw; }
    }

    public void ShowSystemNotification(string title, string message)
    {
        if (_isDisposed)
        {
            return;
        }

        if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                () => ShowSystemNotification(title, message));
            return;
        }

        _notifyIcon.ShowNotification(title, message);
    }

    internal static string BuildNotifyIconText() =>
        "局域网设备助手";

    private static Icon LoadSystemIcon()
    {
        var systemIconPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "SystemIcon.ico");
        if (File.Exists(systemIconPath))
        {
            var dpi = GetDpiForSystem();
            var iconSize = GetSystemMetricsForDpi(SmCxSmallIcon, dpi);
            using var systemIcon = new Icon(
                systemIconPath,
                Math.Max(iconSize, 16),
                Math.Max(iconSize, 16));
            return (Icon)systemIcon.Clone();
        }

        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            using var extracted = Icon.ExtractAssociatedIcon(executablePath);
            if (extracted is not null)
            {
                return (Icon)extracted.Clone();
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _menu.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    public void PrepareForProcessExit()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _menu.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    private const int SmCxSmallIcon = 49;
}
