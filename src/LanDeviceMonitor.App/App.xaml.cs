using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LanDeviceMonitor.Services;

namespace LanDeviceMonitor;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;
    private TrayService? _trayService;
    private NotificationService? _notificationService;
    private SingleInstanceService? _singleInstanceService;
    private bool _hasExited;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceService = new SingleInstanceService();
        if (!_singleInstanceService.IsPrimary)
        {
            await SingleInstanceService.SignalPrimaryAsync();
            _singleInstanceService.Dispose();
            _singleInstanceService = null;
            Shutdown();
            return;
        }

        _singleInstanceService.StartListening(() =>
            Dispatcher.BeginInvoke(ShowMainWindow));

        _notificationService = new NotificationService();
        _notificationService.Register();

        _mainWindow = new MainWindow(_notificationService);
        _mainWindow.SourceInitialized += (_, _) => MicaWindowService.Apply(_mainWindow);
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

        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Activate();

        var handle = new WindowInteropHelper(_mainWindow).Handle;
        if (handle != IntPtr.Zero)
        {
            _ = ShowWindow(handle, 9);
            _ = SetForegroundWindow(handle);
        }
    }

    private void ExitApplication()
    {
        if (_hasExited)
        {
            return;
        }

        _hasExited = true;
        _mainWindow?.RequestExit();
        _trayService?.Dispose();
        _notificationService?.Dispose();
        _singleInstanceService?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_hasExited)
        {
            _hasExited = true;
            _trayService?.Dispose();
            _notificationService?.Dispose();
            _singleInstanceService?.Dispose();
        }

        base.OnExit(e);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);
}
