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
        var startHidden = e.Args.Contains(
            StartupRegistrationService.BackgroundArgument,
            StringComparer.OrdinalIgnoreCase);

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
        _mainWindow.SourceInitialized += (_, _) =>
            MicaWindowService.Apply(
                _mainWindow,
                ThemeService.ResolveIsDark(ThemeService.CurrentMode));
        _trayService = new TrayService(
            showWindow: ShowMainWindow,
            exitApplication: ExitApplication);

        _mainWindow.MinimizeToTrayRequested += (_, _) => _mainWindow.Hide();
        if (!startHidden)
        {
            _mainWindow.Show();
        }

        await _mainWindow.InitializeAsync();
    }

    private void ShowMainWindow()
    {
        if (_hasExited || _mainWindow is null)
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

    private async void ExitApplication()
    {
        if (_hasExited)
        {
            return;
        }

        _hasExited = true;
        // 先移除所有可见元素，让“退出”在点击后立即得到视觉响应。
        _trayService?.Dispose();
        _trayService = null;
        if (_mainWindow is not null)
        {
            await _mainWindow.PrepareForExitAsync();
        }

        // Windows App SDK 的通知注销在部分系统上会阻塞数秒，甚至让进程继续存活。
        // 退出时不再调用它；进程结束后 Windows 会回收该进程的通知注册资源。
        _notificationService = null;

        _singleInstanceService?.Dispose();
        _singleInstanceService = null;
        Shutdown();
        Environment.Exit(0);
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
