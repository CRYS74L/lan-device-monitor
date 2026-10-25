using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace LanDeviceMonitor.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly Action _showWindow;
    private readonly Controls.TrayCommandMenu _menu;
    private bool _isDisposed;

    public TrayService(Action showWindow, Action exitApplication)
    {
        _menu = new Controls.TrayCommandMenu(showWindow, exitApplication);
        _showWindow = showWindow;
        _icon = LoadSystemIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = BuildNotifyIconText(),
            Visible = true
        };

        _notifyIcon.MouseClick += NotifyIcon_MouseClick;
        _notifyIcon.MouseUp += NotifyIcon_MouseUp;
    }

    private void NotifyIcon_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            // NotifyIcon suppresses the second MouseClick in a double click,
            // so the first release opens immediately without activating twice.
            Application.Current.Dispatcher.Invoke(_showWindow);
        }
    }

    private void NotifyIcon_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        // Capture the native release position before queuing WPF menu mode,
        // as ReTray does when forwarding its native tray callback.
        var cursor = Cursor.Position;
        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _menu.Show(new System.Windows.Point(cursor.X, cursor.Y))));
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

        _notifyIcon.ShowBalloonTip(
            5000,
            title,
            message,
            ToolTipIcon.None);
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
        _notifyIcon.Visible = false;
        _notifyIcon.MouseClick -= NotifyIcon_MouseClick;
        _notifyIcon.MouseUp -= NotifyIcon_MouseUp;
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
        _notifyIcon.MouseClick -= NotifyIcon_MouseClick;
        _notifyIcon.MouseUp -= NotifyIcon_MouseUp;
        _notifyIcon.Visible = false;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    private const int SmCxSmallIcon = 49;
}
