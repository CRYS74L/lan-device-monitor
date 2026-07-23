using System.Windows;
using LanDeviceMonitor.Services;

namespace LanDeviceMonitor;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;
    private TrayService? _trayService;
    private NotificationService? _notificationService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _notificationService = new NotificationService();
        _notificationService.Register();

        _mainWindow = new MainWindow(_notificationService);
        _trayService = new TrayService(
            showWindow: ShowMainWindow,
            exitApplication: ExitApplication);

        _mainWindow.MinimizeToTrayRequested += (_, _) => _mainWindow.Hide();
        _mainWindow.Show();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    private void ExitApplication()
    {
        _mainWindow?.RequestExit();
        _trayService?.Dispose();
        _notificationService?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayService?.Dispose();
        _notificationService?.Dispose();
        base.OnExit(e);
    }
}
