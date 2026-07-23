using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using Application = System.Windows.Application;

namespace LanDeviceMonitor.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly Action _showWindow;
    private readonly Action _exitApplication;
    private TrayMenuWindow? _menu;
    private bool _isDisposed;

    public TrayService(Action showWindow, Action exitApplication)
    {
        _showWindow = showWindow;
        _exitApplication = exitApplication;
        _icon = LoadSystemIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "局域网设备助手",
            Visible = true
        };

        _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
        _notifyIcon.MouseUp += NotifyIcon_MouseUp;
    }

    private void NotifyIcon_DoubleClick(object? sender, EventArgs e) =>
        Application.Current.Dispatcher.Invoke(_showWindow);

    private void NotifyIcon_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        Application.Current.Dispatcher.Invoke(ShowMenu);
    }

    private void ShowMenu()
    {
        _menu?.Close();
        _menu = new TrayMenuWindow(_showWindow, _exitApplication);
        _menu.Closed += (_, _) => _menu = null;
        _menu.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));

        var cursor = Cursor.Position;
        var screen = Screen.FromPoint(cursor);
        var dpi = GetDpiForSystem();
        var scale = dpi <= 0 ? 1d : dpi / 96d;
        var menuWidth = _menu.DesiredSize.Width > 0 ? _menu.DesiredSize.Width : _menu.Width;
        var menuHeight = _menu.DesiredSize.Height > 0 ? _menu.DesiredSize.Height : 156;
        var workingArea = screen.WorkingArea;

        _menu.Left = Math.Clamp(
            (cursor.X / scale) - menuWidth + 10,
            workingArea.Left / scale,
            (workingArea.Right / scale) - menuWidth);
        _menu.Top = Math.Clamp(
            (cursor.Y / scale) - menuHeight - 8,
            workingArea.Top / scale,
            (workingArea.Bottom / scale) - menuHeight);
        _menu.Show();
        _menu.Activate();
    }

    private static Icon LoadSystemIcon()
    {
        var systemIconPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "SystemIcon.png");
        if (File.Exists(systemIconPath))
        {
            using var bitmap = new Bitmap(systemIconPath);
            var iconHandle = bitmap.GetHicon();
            try
            {
                using var systemIcon = Icon.FromHandle(iconHandle);
                return (Icon)systemIcon.Clone();
            }
            finally
            {
                _ = DestroyIcon(iconHandle);
            }
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
        _menu?.Close();
        _notifyIcon.Visible = false;
        _notifyIcon.DoubleClick -= NotifyIcon_DoubleClick;
        _notifyIcon.MouseUp -= NotifyIcon_MouseUp;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}
